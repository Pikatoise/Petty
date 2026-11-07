using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Domain.Accounts;
using Petty.Server.Infrastructure.Identity;
using Petty.Server.Infrastructure.Persistence;

namespace Petty.Server.Tests;

[Collection("PostgreSQL")]
public sealed class AuthApiTests(PostgreSqlDatabase database)
{
    private const string Password = "TestPassword12345";

    [PostgreSqlFact]
    public async Task RegistrationValidatesAndEnforcesCaseInsensitiveUniquenessUnderConcurrency()
    {
        using HttpClient client = database.Factory.CreateClient();
        string login = NewLogin() + "abcdefg";
        using HttpResponseMessage invalid = await client.PostAsJsonAsync("/api/v1/auth/register",
            new
            {
                login,
                password = Password,
                confirmPassword = "Different1234"
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using HttpResponseMessage invalidName = await client.PostAsJsonAsync("/api/v1/auth/register",
            new
            {
                login = "../invalid",
                password = Password,
                confirmPassword = Password
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidName.StatusCode);

        HttpResponseMessage[] registration = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                login = " " + login + " ",
                password = Password,
                confirmPassword = Password
            }),
            client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                login = login.ToUpperInvariant(),
                password = Password,
                confirmPassword = Password
            }));
        try
        {
            Assert.Single(registration, response => response.StatusCode == HttpStatusCode.Created);
            HttpResponseMessage duplicate = Assert.Single(registration, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("USERNAME_TAKEN", await ErrorCode(duplicate));
        }
        finally
        {
            foreach (HttpResponseMessage response in registration)
            {
                response.Dispose();
            }
        }
    }

    [PostgreSqlFact]
    public async Task SecondLoginRevokesOldAccessAndRefreshAndRotationRejectsReplay()
    {
        string login = await Register();
        TokenResponse first = await Login(login);
        using HttpClient firstClient = Authorized(first.AccessToken);
        using HttpResponseMessage initialProfile = await firstClient.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, initialProfile.StatusCode);
        JsonElement profile = await initialProfile.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("petName").ValueKind);
        Assert.True(profile.GetProperty("sessionActive").GetBoolean());

        using HttpClient anonymous = database.Factory.CreateClient();
        using HttpResponseMessage wrong = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new
        {
            login,
            password = "WrongPassword123"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await ErrorCode(wrong));

        TokenResponse second = await Login(login.ToUpperInvariant());
        using HttpResponseMessage revoked = await firstClient.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal("SESSION_REVOKED", await ErrorCode(revoked));
        using HttpResponseMessage oldRefresh = await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            first.RefreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        using HttpResponseMessage oldLogout = await firstClient.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, oldLogout.StatusCode);
        using HttpClient secondClient = Authorized(second.AccessToken);
        using HttpResponseMessage stillActive = await secondClient.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);

        string invalidSecret = second.RefreshToken[..33] + (second.RefreshToken[33] == 'A' ? "B" : "A") + second.RefreshToken[34..];
        using HttpResponseMessage wrongSecret = await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            refreshToken = invalidSecret
        });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);
        Assert.Equal("INVALID_REFRESH_TOKEN", await ErrorCode(wrongSecret));

        using HttpResponseMessage rotated = await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            second.RefreshToken
        });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        TokenResponse current = (await rotated.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(second.RefreshToken, current.RefreshToken);
        using HttpResponseMessage replay = await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            second.RefreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        using HttpClient currentClient = Authorized(current.AccessToken);
        using HttpResponseMessage logout = await currentClient.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using HttpResponseMessage repeatLogout = await currentClient.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, repeatLogout.StatusCode);
        using HttpResponseMessage refreshAfterLogout = await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            current.RefreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [PostgreSqlFact]
    public async Task ConcurrentLoginAndRefreshLeaveOneActiveSessionAndOneRefreshWinner()
    {
        string login = await Register();
        // Exercise Identity's automatic rehash as well as the session replacement race.
        using (IServiceScope setup = database.Factory.Services.CreateScope())
        {
            ServerDbContext setupContext = setup.ServiceProvider.GetRequiredService<ServerDbContext>();
            ApplicationUser user = await setupContext.Users.SingleAsync(user => user.NormalizedUserName == login.ToUpperInvariant());
            var legacyHasher = new PasswordHasher<ApplicationUser>(Options.Create(new PasswordHasherOptions { IterationCount = 1000 }));
            user.PasswordHash = legacyHasher.HashPassword(user, Password);
            await setupContext.SaveChangesAsync();
        }

        TokenResponse[] tokens = await Task.WhenAll(Login(login), Login(login));
        using HttpClient left = Authorized(tokens[0].AccessToken);
        using HttpClient right = Authorized(tokens[1].AccessToken);
        using HttpResponseMessage leftStatus = await left.GetAsync("/api/v1/me");
        using HttpResponseMessage rightStatus = await right.GetAsync("/api/v1/me");
        Assert.Equal(1, new[] { leftStatus, rightStatus }.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, new[] { leftStatus, rightStatus }.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
        TokenResponse active = leftStatus.IsSuccessStatusCode ? tokens[0] : tokens[1];

        using HttpClient anonymous = database.Factory.CreateClient();
        HttpResponseMessage[] refreshes = await Task.WhenAll(
            anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new
            {
                active.RefreshToken
            }),
            anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new
            {
                active.RefreshToken
            }));
        try
        {
            Assert.Single(refreshes, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(refreshes, response => response.StatusCode == HttpStatusCode.Unauthorized);
        }
        finally
        {
            foreach (HttpResponseMessage response in refreshes)
            {
                response.Dispose();
            }
        }

        using IServiceScope scope = database.Factory.Services.CreateScope();
        ServerDbContext context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        Guid userId = await context.Users.Where(user => user.NormalizedUserName == login.ToUpperInvariant()).Select(user => user.Id).SingleAsync();
        Assert.Equal(1, await context.UserSessions.CountAsync(session => session.UserId == userId && session.RevokedAtUtc == null));
        context.UserSessions.Add(new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RefreshExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            RefreshTokenHash = new string('0', 64)
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task StorageProtectsSecretsAndExpiredOrForgedTokensAreRejected()
    {
        string login = await Register();
        TokenResponse token = await Login(login);
        using IServiceScope scope = database.Factory.Services.CreateScope();
        ServerDbContext context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        ApplicationUser user = await context.Users.SingleAsync(user => user.NormalizedUserName == login.ToUpperInvariant());
        UserSession session = await context.UserSessions.SingleAsync(session => session.Id == token.SessionId);
        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.Equal(RefreshTokens.Hash(token.RefreshToken), session.RefreshTokenHash);
        Assert.NotEqual(token.RefreshToken, session.RefreshTokenHash);

        IAccessTokenIssuer issuer = scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>();
        string expired = issuer.Issue(user.Id, session.Id, DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow.AddMinutes(-1));
        using HttpClient expiredClient = Authorized(expired);
        using HttpResponseMessage expiredResponse = await expiredClient.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);

        string[] parts = token.AccessToken.Split('.');
        parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        using HttpClient forgedClient = Authorized(string.Join('.', parts));
        using HttpResponseMessage forged = await forgedClient.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);

        session.RefreshExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        await context.SaveChangesAsync();
        using HttpClient anonymous = database.Factory.CreateClient();
        using HttpResponseMessage expiredRefresh = await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            token.RefreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, expiredRefresh.StatusCode);
        using HttpResponseMessage unauthenticated = await anonymous.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [PostgreSqlFact]
    public async Task PetProfileUsesAuthenticatedActorAndRejectsPathsAndInvalidNames()
    {
        TokenResponse first = await Login(await Register());
        TokenResponse second = await Login(await Register());
        using HttpClient firstClient = Authorized(first.AccessToken);
        using HttpClient secondClient = Authorized(second.AccessToken);
        using HttpResponseMessage secondProfile = await secondClient.GetAsync("/api/v1/me");
        JsonElement secondData = await secondProfile.Content.ReadFromJsonAsync<JsonElement>();
        using HttpResponseMessage update = await firstClient.PutAsJsonAsync("/api/v1/me/pet",
            new
            {
                petName = "Мурка",
                assetId = "british-shorthair-blue",
                userId = secondData.GetProperty("userId").GetGuid()
            });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        JsonElement firstData = await firstClient.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal("Мурка", firstData.GetProperty("petName").GetString());
        JsonElement unchanged = await secondClient.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("petName").ValueKind);

        foreach (var invalid in new[]
        {
            new { petName = "Kitty", assetId = "https://example.com/pet.png" },
            new { petName = "Kitty", assetId = "../custom" },
            new { petName = "bad\nname", assetId = "british-shorthair-blue" },
            new { petName = new string('a', 25), assetId = "british-shorthair-blue" }
        })
        {
            using HttpResponseMessage response = await firstClient.PutAsJsonAsync("/api/v1/me/pet", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    private static string NewLogin() => "user_" + Guid.NewGuid().ToString("N")[..20];

    private async Task<string> Register()
    {
        string login = NewLogin();
        using HttpClient client = database.Factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new
            {
                login,
                password = Password,
                confirmPassword = Password
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return login;
    }

    private async Task<TokenResponse> Login(string login)
    {
        using HttpClient client = database.Factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            login,
            password = Password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private HttpClient Authorized(string token)
    {
        HttpClient client = database.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}
