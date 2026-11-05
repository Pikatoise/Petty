using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
        }
    }
}
