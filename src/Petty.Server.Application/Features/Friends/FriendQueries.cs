using Mediator;

namespace Petty.Server.Application.Features.Friends;

public sealed record GetFriends(Guid UserId) : IQuery<IReadOnlyList<FriendDto>>;

public sealed class GetFriendsHandler(IFriendshipRepository friendships) : IQueryHandler<GetFriends, IReadOnlyList<FriendDto>>
{
    public async ValueTask<IReadOnlyList<FriendDto>> Handle(GetFriends query, CancellationToken cancellationToken) =>
        await friendships.GetFriendsAsync(query.UserId, cancellationToken);
}

public sealed record GetFriendRequests(Guid UserId) : IQuery<FriendRequestsDto>;

public sealed class GetFriendRequestsHandler(IFriendshipRepository friendships) : IQueryHandler<GetFriendRequests, FriendRequestsDto>
{
    public async ValueTask<FriendRequestsDto> Handle(GetFriendRequests query, CancellationToken cancellationToken) =>
        await friendships.GetRequestsAsync(query.UserId, cancellationToken);
}
