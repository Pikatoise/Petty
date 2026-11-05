using Microsoft.EntityFrameworkCore;
using Petty.Server.Application.Features.Friends;
using Petty.Server.Domain.Friends;

namespace Petty.Server.Infrastructure.Persistence;

public sealed class FriendshipRepository(ServerDbContext database) : IFriendshipRepository
{
    public async Task<Friendship> LockAsync(Guid actorId, Guid otherUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Friendship changes require a Unit of Work transaction.");
        }

        (Guid lower, Guid higher) = Friendship.OrderPair(actorId, otherUserId);
        // The persistent pair row also serializes blocking and mail quota checks.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Friendships" ("Id", "LowerUserId", "HigherUserId", "RequestedById", "Status", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES ({Guid.NewGuid()}, {lower}, {higher}, {actorId}, {(int)FriendshipStatus.Removed}, {now}, {now})
            ON CONFLICT ("LowerUserId", "HigherUserId") DO NOTHING
            """, cancellationToken);
        return await database.Friendships.FromSql($"""
            SELECT * FROM "Friendships" WHERE "LowerUserId" = {lower} AND "HigherUserId" = {higher} FOR UPDATE
            """).SingleAsync(cancellationToken);
    }

    public Task<Friendship?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken) =>
        database.Friendships.AsNoTracking().SingleOrDefaultAsync(friendship => friendship.RequestId == requestId, cancellationToken);

    public Task<bool> IsBlockedAsync(Guid first, Guid second, CancellationToken cancellationToken) =>
        database.UserBlocks.AnyAsync(block => (block.BlockerId == first && block.BlockedId == second)
            || (block.BlockerId == second && block.BlockedId == first), cancellationToken);

    public Task<UserBlock?> GetBlockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        database.UserBlocks.SingleOrDefaultAsync(block => block.BlockerId == blockerId && block.BlockedId == blockedId, cancellationToken);

    public void AddBlock(UserBlock block) => database.UserBlocks.Add(block);
    public void RemoveBlock(UserBlock block) => database.UserBlocks.Remove(block);

    public async Task<IReadOnlyList<FriendDto>> GetFriendsAsync(Guid userId, CancellationToken cancellationToken) =>
        await (from friendship in VisibleTo(userId)
               where friendship.Status == FriendshipStatus.Accepted
               join user in database.Users.AsNoTracking()
                   on (friendship.LowerUserId == userId ? friendship.HigherUserId : friendship.LowerUserId) equals user.Id
               orderby user.NormalizedUserName, user.Id
               select new FriendDto(user.Id, user.UserName!, user.PetName, user.PetAssetId)).ToListAsync(cancellationToken);

    public async Task<FriendRequestsDto> GetRequestsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var requests = await (from friendship in VisibleTo(userId)
                              where friendship.Status == FriendshipStatus.Pending
                              join user in database.Users.AsNoTracking()
                                  on (friendship.LowerUserId == userId ? friendship.HigherUserId : friendship.LowerUserId) equals user.Id
                              orderby friendship.UpdatedAtUtc, friendship.RequestId
                              select new
                              {
                                  Outgoing = friendship.RequestedById == userId,
                                  Request = new FriendRequestDto(friendship.RequestId!.Value,
                                      new FriendDto(user.Id, user.UserName!, user.PetName, user.PetAssetId), friendship.UpdatedAtUtc)
                              }).ToListAsync(cancellationToken);
        return new FriendRequestsDto(requests.Where(request => !request.Outgoing).Select(request => request.Request).ToArray(),
            requests.Where(request => request.Outgoing).Select(request => request.Request).ToArray());
    }

    private IQueryable<Friendship> VisibleTo(Guid userId) => database.Friendships.AsNoTracking()
        .Where(friendship => (friendship.LowerUserId == userId || friendship.HigherUserId == userId)
            && !database.UserBlocks.Any(block => (block.BlockerId == friendship.LowerUserId && block.BlockedId == friendship.HigherUserId)
                || (block.BlockerId == friendship.HigherUserId && block.BlockedId == friendship.LowerUserId)));
}
