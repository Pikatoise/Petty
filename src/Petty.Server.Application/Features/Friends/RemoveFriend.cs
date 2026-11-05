using Mediator;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Application.Notifications;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Friends;

namespace Petty.Server.Application.Features.Friends;

public sealed record RemoveFriend(Guid UserId, Guid OtherUserId) : ICommand<Unit>;

public sealed class RemoveFriendHandler(IAccountRepository accounts, IFriendshipRepository friendships,
    IUnitOfWork unitOfWork, TimeProvider clock, IMailNotifier notifier) : ICommandHandler<RemoveFriend, Unit>
{
    public async ValueTask<Unit> Handle(RemoveFriend command, CancellationToken cancellationToken)
    {
        if (command.UserId == command.OtherUserId || !await accounts.ExistsAsync(command.OtherUserId, cancellationToken))
        {
            throw new RequestFailure(FailureKind.NotFound, "USER_NOT_AVAILABLE");
        }

        await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        Friendship friendship = await friendships.LockAsync(command.UserId, command.OtherUserId, now, cancellationToken);
        bool changed = friendship.Status == FriendshipStatus.Accepted;
        if (changed)
        {
            friendship.Status = FriendshipStatus.Removed;
            friendship.UpdatedAtUtc = now;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        if (changed)
        {
            await notifier.FriendshipsChangedAsync(command.UserId, command.OtherUserId, refreshMailbox: false, cancellationToken);
        }

        return Unit.Value;
    }
}
