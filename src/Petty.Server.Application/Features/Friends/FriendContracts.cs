using Petty.Server.Domain.Friends;

namespace Petty.Server.Application.Features.Friends;

public sealed record FriendDto(Guid UserId, string Login, string? PetName, string? PetAssetId);
public sealed record BlockedUserDto(Guid UserId, string Login);
public sealed record FriendRequestDto(Guid RequestId, FriendDto User, DateTimeOffset RequestedAtUtc);
public sealed record FriendRequestsDto(IReadOnlyList<FriendRequestDto> Incoming, IReadOnlyList<FriendRequestDto> Outgoing);

public interface IFriendshipRepository
{
    // Mutations acquire this shared pair lock inside the caller's Unit of Work transaction.
    Task<Friendship> LockAsync(Guid actorId, Guid otherUserId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<Friendship?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);
    Task<bool> IsBlockedAsync(Guid first, Guid second, CancellationToken cancellationToken);
    Task<UserBlock?> GetBlockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);
    void AddBlock(UserBlock block);
    void RemoveBlock(UserBlock block);
    Task<IReadOnlyList<FriendDto>> GetFriendsAsync(Guid userId, CancellationToken cancellationToken);
    Task<FriendRequestsDto> GetRequestsAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BlockedUserDto>> GetBlockedAsync(Guid userId, CancellationToken cancellationToken);
}
