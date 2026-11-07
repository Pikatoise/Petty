using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Petty.Server.Infrastructure.Persistence;

/// <summary>Build/run migration bundles without starting the API or requiring JWT keys.</summary>
public sealed class ServerDbContextDesignFactory : IDesignTimeDbContextFactory<ServerDbContext>
{
    public ServerDbContext CreateDbContext(string[] args)
    {
        const string secretPath = "/run/petty/ConnectionStrings__Petty";
        string connection = File.Exists(secretPath) ? File.ReadAllText(secretPath).Trim()
            : Environment.GetEnvironmentVariable("ConnectionStrings__Petty")
                ?? "Host=localhost;Database=petty;Username=petty_owner";
        return new ServerDbContext(new DbContextOptionsBuilder<ServerDbContext>().UseNpgsql(connection).Options);
    }
}
