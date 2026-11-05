using Mediator;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Friends;

namespace Petty.Server.Application.Features.Friends;

public sealed record RespondToFriendRequest(Guid UserId, Guid RequestId, bool Accept) : ICommand<Unit>;

public sealed class RespondToFriendRequestHandler(IFriendshipRepository friendships, IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RespondToFriendRequest, Unit>
{
    public async ValueTask<Unit> Handle(RespondToFriendRequest command, CancellationToken cancellationToken)
    {
        Friendship initial = await friendships.FindByRequestIdAsync(command.RequestId, cancellationToken)
            ?? throw new RequestFailure(FailureKind.NotFound, "FRIEND_REQUEST_NOT_AVAILABLE");
        if (initial.LowerUserId != command.UserId && initial.HigherUserId != command.UserId)
        {
            throw new RequestFailure(FailureKind.NotFound, "FRIEND_REQUEST_NOT_AVAILABLE");
        }

        Guid other = initial.LowerUserId == command.UserId ? initial.HigherUserId : initial.LowerUserId;
        await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        Friendship friendship = await friendships.LockAsync(command.UserId, other, now, cancellationToken);
        if (friendship.RequestId != command.RequestId || await friendships.IsBlockedAsync(command.UserId, other, cancellationToken))
        {
            throw new RequestFailure(FailureKind.NotFound, "FRIEND_REQUEST_NOT_AVAILABLE");
        }

        if (friendship.RequestedById == command.UserId)
        {
            throw new RequestFailure(FailureKind.Forbidden, "FRIEND_REQUEST_RECIPIENT_REQUIRED");
        }

        FriendshipStatus desired = command.Accept ? FriendshipStatus.Accepted : FriendshipStatus.Removed;
        if (friendship.Status != desired)
        {
            if (friendship.Status != FriendshipStatus.Pending)
            {
                throw new RequestFailure(FailureKind.Conflict, "FRIEND_REQUEST_NOT_PENDING");
            }

            friendship.Status = desired;
            friendship.UpdatedAtUtc = now;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}
