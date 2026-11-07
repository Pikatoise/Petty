using Mediator;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Application.Notifications;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Friends;

namespace Petty.Server.Application.Features.Friends;

public sealed record RequestFriend(Guid UserId, string Login) : ICommand<Guid>;

public sealed class RequestFriendHandler(IAccountRepository accounts, IFriendshipRepository friendships,
    IUnitOfWork unitOfWork, TimeProvider clock, IMailNotifier notifier) : ICommandHandler<RequestFriend, Guid>
{
    public async ValueTask<Guid> Handle(RequestFriend command, CancellationToken cancellationToken)
    {
        string login = RegisterHandler.NormalizeLogin(command.Login);
        Guid target = await accounts.FindIdByLoginAsync(login, cancellationToken)
            ?? throw new RequestFailure(FailureKind.NotFound, "USER_NOT_AVAILABLE");
        if (target == command.UserId)
        {
            throw new RequestFailure(FailureKind.Invalid, "SELF_FRIENDSHIP_NOT_ALLOWED");
        }

        await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        Friendship friendship = await friendships.LockAsync(command.UserId, target, now, cancellationToken);
        if (await friendships.IsBlockedAsync(command.UserId, target, cancellationToken))
        {
            throw new RequestFailure(FailureKind.NotFound, "USER_NOT_AVAILABLE");
        }

        if (friendship.Status != FriendshipStatus.Removed)
        {
            throw new RequestFailure(FailureKind.Conflict, friendship.Status == FriendshipStatus.Pending
                ? "FRIEND_REQUEST_EXISTS" : "FRIENDSHIP_ALREADY_EXISTS");
        }

        var requestId = Guid.NewGuid();
        friendship.RequestId = requestId;
        friendship.RequestedById = command.UserId;
        friendship.Status = FriendshipStatus.Pending;
        friendship.UpdatedAtUtc = now;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifier.FriendshipsChangedAsync(command.UserId, target, refreshMailbox: false, cancellationToken);
        return requestId;
    }
}
