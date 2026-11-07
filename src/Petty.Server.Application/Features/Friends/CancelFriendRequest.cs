using Mediator;
using Petty.Server.Application.Notifications;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Friends;

namespace Petty.Server.Application.Features.Friends;

public sealed record CancelFriendRequest(Guid UserId, Guid RequestId) : ICommand<Unit>;

public sealed class CancelFriendRequestHandler(IFriendshipRepository friendships, IUnitOfWork unitOfWork,
    TimeProvider clock, IMailNotifier notifier) : ICommandHandler<CancelFriendRequest, Unit>
{
    public async ValueTask<Unit> Handle(CancelFriendRequest command, CancellationToken cancellationToken)
    {
        Friendship initial = await friendships.FindByRequestIdAsync(command.RequestId, cancellationToken)
            ?? throw new RequestFailure(FailureKind.NotFound, "FRIEND_REQUEST_NOT_AVAILABLE");
        if (initial.LowerUserId != command.UserId && initial.HigherUserId != command.UserId)
        {
            throw new RequestFailure(FailureKind.NotFound, "FRIEND_REQUEST_NOT_AVAILABLE");
        }
        if (initial.RequestedById != command.UserId)
        {
            throw new RequestFailure(FailureKind.Forbidden, "FRIEND_REQUEST_SENDER_REQUIRED");
        }
        Guid other = initial.LowerUserId == command.UserId ? initial.HigherUserId : initial.LowerUserId;
        await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        Friendship friendship = await friendships.LockAsync(command.UserId, other, now, cancellationToken);
        if (friendship.RequestId != command.RequestId || friendship.RequestedById != command.UserId)
        {
            throw new RequestFailure(FailureKind.NotFound, "FRIEND_REQUEST_NOT_AVAILABLE");
        }
        if (friendship.Status != FriendshipStatus.Pending)
        {
            throw new RequestFailure(FailureKind.Conflict, "FRIEND_REQUEST_NOT_PENDING");
        }
        friendship.Status = FriendshipStatus.Removed;
        friendship.UpdatedAtUtc = now;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifier.FriendshipsChangedAsync(command.UserId, other, refreshMailbox: false, cancellationToken);
        return Unit.Value;
    }
}
