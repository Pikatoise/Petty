using Microsoft.EntityFrameworkCore;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Domain.Accounts;

namespace Petty.Server.Infrastructure.Persistence;

public sealed class AccountRepository(ServerDbContext database) : IAccountRepository
{
    public Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        database.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    public Task<Guid?> FindIdByLoginAsync(string login, CancellationToken cancellationToken) =>
        database.Users.Where(user => user.NormalizedUserName == login.ToUpperInvariant())
            .Select(user => (Guid?)user.Id).SingleOrDefaultAsync(cancellationToken);

    public async Task LockAsync(Guid userId, CancellationToken cancellationToken)
    {
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE", cancellationToken);
    }

    public Task<AccountProfile> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        database.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => new AccountProfile(user.Id, user.UserName!, user.PetName, user.PetAssetId, user.ProfileUpdatedUtc))
            .SingleAsync(cancellationToken);

    public async Task UpdatePetAsync(Guid userId, string petName, string assetId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Identity.ApplicationUser user = await database.Users.SingleAsync(user => user.Id == userId, cancellationToken);
        user.PetName = petName;
        user.PetAssetId = assetId;
        user.ProfileUpdatedUtc = now;
    }
}
