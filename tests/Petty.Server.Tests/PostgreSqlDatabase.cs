using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Infrastructure.Persistence;

namespace Petty.Server.Tests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PETTY_TEST_POSTGRES")))
        {
            Skip = "Set PETTY_TEST_POSTGRES to an isolated PostgreSQL database named petty_test_*.";
        }
    }
}

[CollectionDefinition("PostgreSQL")]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlDatabase>;

public sealed class PostgreSqlDatabase : IAsyncLifetime
{
    public async Task<TestUser> CreateUserAsync()
    {
        string login = "test_" + Guid.NewGuid().ToString("N")[..20];
        const string password = "TestPassword12345";
        HttpClient client = Factory.CreateClient();
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

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        string? connectionString = Environment.GetEnvironmentVariable("PETTY_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (settings.Database?.StartsWith("petty_test_", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException("Integration tests require an isolated database named petty_test_*.");
        }

        Factory = new OnlineFactory(connectionString);
        using IServiceScope scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ServerDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }

    private sealed class OnlineFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Petty", connectionString);
            builder.UseSetting("Auth:SigningKey", Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()));
            builder.UseSetting("Auth:RequestsPerMinute", "1000");
            builder.UseSetting("Friends:RequestsPerMinute", "1000");
            builder.UseSetting("Letters:RequestsPerMinute", "1000");
        }
    }
}
public sealed record TestUser(Guid Id, string Login, HttpClient Client) : IDisposable
{
    public void Dispose() => Client.Dispose();
}
