using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Petty.Server.Application.Health;

namespace Petty.Server.Infrastructure.Persistence;

public sealed class DatabaseReadiness(ServerDbContext database) : IDatabaseReadiness
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await database.Database.CanConnectAsync(cancellationToken))
            {
                return false;
            }

            return !(await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
        }
        catch (DbException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
