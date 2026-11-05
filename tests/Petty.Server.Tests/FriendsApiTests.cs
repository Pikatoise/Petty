using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Application.Features.Friends;
using Petty.Server.Domain.Friends;
using Petty.Server.Infrastructure.Persistence;

namespace Petty.Server.Tests;

[Collection("PostgreSQL")]
public sealed class FriendsApiTests(PostgreSqlDatabase database)
{
    [PostgreSqlFact]
    public async Task RequestsRequireRecipientConsentAndExposeOnlyPublicParticipantData()
    {
        using TestUser first = await CreateUser();
        using TestUser second = await CreateUser();
        using TestUser stranger = await CreateUser();
        using HttpClient anonymous = database.Factory.CreateClient();
        using HttpResponseMessage unauthorized = await anonymous.GetAsync("/api/v1/friends");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using HttpResponseMessage profile = await second.Client.PutAsJsonAsync("/api/v1/me/pet",
            new
            {
                petName = "Мурка",
                assetId = "british-shorthair-blue"
            });
        Assert.Equal(HttpStatusCode.NoContent, profile.StatusCode);

        using HttpResponseMessage sent = await first.Client.PostAsJsonAsync("/api/v1/friends/requests",
            new
            {
                login = " " + second.Login.ToUpperInvariant() + " ",
                userId = stranger.Id
            });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Guid requestId = (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
        FriendRequestsDto outgoing = (await first.Client.GetFromJsonAsync<FriendRequestsDto>("/api/v1/friends/requests"))!;
        Assert.Empty(outgoing.Incoming);
        Assert.Equal(second.Id, Assert.Single(outgoing.Outgoing).User.UserId);
        FriendRequestsDto incoming = (await second.Client.GetFromJsonAsync<FriendRequestsDto>("/api/v1/friends/requests"))!;
        Assert.Equal(first.Id, Assert.Single(incoming.Incoming).User.UserId);
        Assert.Empty(incoming.Outgoing);
        Assert.Empty((await first.Client.GetFromJsonAsync<FriendDto[]>("/api/v1/friends"))!);
        Assert.Empty((await stranger.Client.GetFromJsonAsync<FriendDto[]>("/api/v1/friends"))!);
        FriendRequestsDto unrelated = (await stranger.Client.GetFromJsonAsync<FriendRequestsDto>("/api/v1/friends/requests"))!;
        Assert.Empty(unrelated.Incoming);
        Assert.Empty(unrelated.Outgoing);

        using HttpResponseMessage wrongActor = await stranger.Client.PostAsync($"/api/v1/friends/requests/{requestId}/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, wrongActor.StatusCode);
        using HttpResponseMessage senderAccept = await first.Client.PostAsync($"/api/v1/friends/requests/{requestId}/accept", null);
        Assert.Equal(HttpStatusCode.Forbidden, senderAccept.StatusCode);
        using HttpResponseMessage senderDecline = await first.Client.PostAsync($"/api/v1/friends/requests/{requestId}/decline", null);
        Assert.Equal(HttpStatusCode.Forbidden, senderDecline.StatusCode);
        await Accept(second, requestId);
        await Accept(second, requestId);
        JsonElement friends = await first.Client.GetFromJsonAsync<JsonElement>("/api/v1/friends");
        JsonElement friend = Assert.Single(friends.EnumerateArray());
        Assert.Equal(second.Id, friend.GetProperty("userId").GetGuid());
        Assert.Equal("Мурка", friend.GetProperty("petName").GetString());
        Assert.Equal(new[] { "login", "petAssetId", "petName", "userId" },
            friend.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Empty((await second.Client.GetFromJsonAsync<FriendRequestsDto>("/api/v1/friends/requests"))!.Incoming);
    }

    [PostgreSqlFact]
    public async Task ConcurrentCrossedRequestsAndDuplicatesLeaveOneCanonicalPair()
    {
        using TestUser first = await CreateUser();
        using TestUser second = await CreateUser();
        using HttpResponseMessage selfRequest = await first.Client.PostAsJsonAsync("/api/v1/friends/requests", new
        {
            login = first.Login
        });
        Assert.Equal(HttpStatusCode.BadRequest, selfRequest.StatusCode);
        using HttpResponseMessage selfBlock = await first.Client.PostAsync($"/api/v1/users/{first.Id}/block", null);
        Assert.Equal(HttpStatusCode.BadRequest, selfBlock.StatusCode);
        HttpResponseMessage[] responses = await Task.WhenAll(
            first.Client.PostAsJsonAsync("/api/v1/friends/requests", new
            {
                login = second.Login
            }),
            second.Client.PostAsJsonAsync("/api/v1/friends/requests", new
            {
                login = first.Login
            }));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            HttpResponseMessage duplicate = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("FRIEND_REQUEST_EXISTS", await ErrorCode(duplicate));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        using HttpResponseMessage repeated = await first.Client.PostAsJsonAsync("/api/v1/friends/requests", new
        {
            login = second.Login
        });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        using IServiceScope scope = database.Factory.Services.CreateScope();
        ServerDbContext context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        (Guid lower, Guid higher) = Friendship.OrderPair(first.Id, second.Id);
        Friendship pair = await context.Friendships.SingleAsync(friendship => friendship.LowerUserId == lower && friendship.HigherUserId == higher);
        Assert.Equal(FriendshipStatus.Pending, pair.Status);
        context.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            LowerUserId = lower,
            HigherUserId = higher,
            RequestedById = first.Id,
            Status = FriendshipStatus.Removed,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task DeclineAndRemovalPreservePairAndInvalidateOldRequestIds()
    {
        using TestUser first = await CreateUser();
        using TestUser second = await CreateUser();
        Guid initialRequest = await Request(first, second);
        using (HttpResponseMessage declined = await second.Client.PostAsync($"/api/v1/friends/requests/{initialRequest}/decline", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);
        }
        using (HttpResponseMessage repeated = await second.Client.PostAsync($"/api/v1/friends/requests/{initialRequest}/decline", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        }
        Guid nextRequest = await Request(second, first);
        Assert.NotEqual(initialRequest, nextRequest);
        using (HttpResponseMessage stale = await second.Client.PostAsync($"/api/v1/friends/requests/{initialRequest}/accept", null))
        {
            Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        }
        await Accept(first, nextRequest);
        using IServiceScope before = database.Factory.Services.CreateScope();
        ServerDbContext beforeContext = before.ServiceProvider.GetRequiredService<ServerDbContext>();
        Guid pairId = await beforeContext.Friendships.Where(friendship => friendship.RequestId == nextRequest).Select(friendship => friendship.Id).SingleAsync();
        using (HttpResponseMessage removed = await first.Client.DeleteAsync($"/api/v1/friends/{second.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }
        using (HttpResponseMessage repeated = await first.Client.DeleteAsync($"/api/v1/friends/{second.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        }
        Assert.Empty((await second.Client.GetFromJsonAsync<FriendDto[]>("/api/v1/friends"))!);
        Guid thirdRequest = await Request(first, second);
        using (HttpResponseMessage stale = await first.Client.PostAsync($"/api/v1/friends/requests/{nextRequest}/accept", null))
        {
            Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        }
        await Accept(second, thirdRequest);
        using IServiceScope after = database.Factory.Services.CreateScope();
        ServerDbContext afterContext = after.ServiceProvider.GetRequiredService<ServerDbContext>();
        Friendship restored = await afterContext.Friendships.SingleAsync(friendship => friendship.Id == pairId);
        Assert.Equal(thirdRequest, restored.RequestId);
        Assert.Equal(FriendshipStatus.Accepted, restored.Status);
    }

    [PostgreSqlFact]
    public async Task MutualBlocksArePrivateAndUnblockingRequiresNewConsent()
    {
        using TestUser first = await CreateUser();
        using TestUser second = await CreateUser();
        Guid requestId = await Request(first, second);
        await Accept(second, requestId);
        await SetBlock(first, second, block: true);
        await SetBlock(first, second, block: true);
        await SetBlock(second, first, block: true);
        await SetBlock(first, second, block: false);
        await SetBlock(first, second, block: false);
        Assert.Empty((await first.Client.GetFromJsonAsync<FriendDto[]>("/api/v1/friends"))!);
        Assert.Empty((await second.Client.GetFromJsonAsync<FriendDto[]>("/api/v1/friends"))!);
        using HttpResponseMessage hidden = await first.Client.PostAsJsonAsync("/api/v1/friends/requests", new
        {
            login = second.Login
        });
        using HttpResponseMessage reverse = await second.Client.PostAsJsonAsync("/api/v1/friends/requests", new
        {
            login = first.Login
        });
        using HttpResponseMessage missing = await first.Client.PostAsJsonAsync("/api/v1/friends/requests", new
        {
            login = "missing_" + Guid.NewGuid().ToString("N")[..16]
        });
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(hidden.StatusCode, reverse.StatusCode);
        Assert.Equal(hidden.StatusCode, missing.StatusCode);
        string? hiddenCode = await ErrorCode(hidden);
        Assert.Equal(await ErrorCode(missing), hiddenCode);
        Assert.Equal(hiddenCode, await ErrorCode(reverse));
        await SetBlock(second, first, block: false);
        Assert.Empty((await first.Client.GetFromJsonAsync<FriendDto[]>("/api/v1/friends"))!);
        using HttpResponseMessage stale = await second.Client.PostAsync($"/api/v1/friends/requests/{requestId}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Guid fresh = await Request(first, second);
        Assert.NotEqual(requestId, fresh);
        await Accept(second, fresh);
        Assert.Single((await first.Client.GetFromJsonAsync<FriendDto[]>("/api/v1/friends"))!);
    }

    [PostgreSqlFact]
    public async Task ConcurrentAcceptAndBlockCannotLeaveAcceptedFriendship()
    {
        using TestUser first = await CreateUser();
        using TestUser second = await CreateUser();
        Guid requestId = await Request(first, second);
        HttpResponseMessage[] responses = await Task.WhenAll(
            second.Client.PostAsync($"/api/v1/friends/requests/{requestId}/accept", null),
            first.Client.PostAsync($"/api/v1/users/{second.Id}/block", null));
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);
            Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound });
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
        Assert.Empty((await first.Client.GetFromJsonAsync<FriendDto[]>("/api/v1/friends"))!);
        Assert.Empty((await second.Client.GetFromJsonAsync<FriendRequestsDto>("/api/v1/friends/requests"))!.Incoming);
        using IServiceScope scope = database.Factory.Services.CreateScope();
        ServerDbContext context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        Assert.Equal(FriendshipStatus.Removed, (await context.Friendships.SingleAsync(friendship => friendship.RequestId == requestId)).Status);
        Assert.True(await context.UserBlocks.AnyAsync(block => block.BlockerId == first.Id && block.BlockedId == second.Id));
    }

    [PostgreSqlFact]
    public async Task ConcurrentFirstRequestAndBlockShareOnePersistentPair()
    {
        using TestUser first = await CreateUser();
        using TestUser second = await CreateUser();
        HttpResponseMessage[] responses = await Task.WhenAll(
            first.Client.PostAsJsonAsync("/api/v1/friends/requests", new
            {
                login = second.Login
            }),
            second.Client.PostAsync($"/api/v1/users/{first.Id}/block", null));
        try
        {
            Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.NotFound });
            Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
        Assert.Empty((await first.Client.GetFromJsonAsync<FriendRequestsDto>("/api/v1/friends/requests"))!.Outgoing);
        using IServiceScope scope = database.Factory.Services.CreateScope();
        ServerDbContext context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        (Guid lower, Guid higher) = Friendship.OrderPair(first.Id, second.Id);
        Friendship pair = await context.Friendships.SingleAsync(friendship => friendship.LowerUserId == lower && friendship.HigherUserId == higher);
        Assert.Equal(FriendshipStatus.Removed, pair.Status);
        IFriendshipRepository repository = scope.ServiceProvider.GetRequiredService<IFriendshipRepository>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LockAsync(first.Id, second.Id, DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task FriendMutationRateLimitDoesNotConsumeAuthOrReadBudgets()
    {
        using TestUser first = await CreateUser();
        using TestUser second = await CreateUser();
        using WebApplicationFactory<Program> limitedFactory = database.Factory.WithWebHostBuilder(builder => builder.UseSetting("Friends:RequestsPerMinute", "1"));
        using HttpClient limited = limitedFactory.CreateClient();
        limited.DefaultRequestHeaders.Authorization = first.Client.DefaultRequestHeaders.Authorization;
        using HttpResponseMessage sent = await limited.PostAsJsonAsync("/api/v1/friends/requests", new
        {
            login = second.Login
        });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        using HttpResponseMessage throttled = await limited.PostAsJsonAsync("/api/v1/friends/requests", new
        {
            login = second.Login
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal("RATE_LIMITED", await ErrorCode(throttled));
        using HttpResponseMessage list = await limited.GetAsync("/api/v1/friends/requests");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using HttpResponseMessage auth = await limited.PostAsJsonAsync("/api/v1/auth/register", new
        {
        });
        Assert.Equal(HttpStatusCode.BadRequest, auth.StatusCode);
    }

    private async Task<TestUser> CreateUser()
    {
        string login = "friend_" + Guid.NewGuid().ToString("N")[..20];
        const string password = "TestPassword12345";
        HttpClient client = database.Factory.CreateClient();
        using HttpResponseMessage registered = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            login,
            password,
            confirmPassword = password
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Guid userId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();
        using HttpResponseMessage signedIn = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            login,
            password
        });
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        TokenResponse token = (await signedIn.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return new TestUser(userId, login, client);
    }

    private static async Task<Guid> Request(TestUser sender, TestUser recipient)
    {
        using HttpResponseMessage response = await sender.Client.PostAsJsonAsync("/api/v1/friends/requests", new
        {
            login = recipient.Login
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
    }

    private static async Task Accept(TestUser recipient, Guid requestId)
    {
        using HttpResponseMessage response = await recipient.Client.PostAsync($"/api/v1/friends/requests/{requestId}/accept", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task SetBlock(TestUser actor, TestUser other, bool block)
    {
        using HttpResponseMessage response = block ? await actor.Client.PostAsync($"/api/v1/users/{other.Id}/block", null)
            : await actor.Client.DeleteAsync($"/api/v1/users/{other.Id}/block");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private sealed record TestUser(Guid Id, string Login, HttpClient Client) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
