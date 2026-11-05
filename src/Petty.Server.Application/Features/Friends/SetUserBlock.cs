using Mediator;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Friends;

namespace Petty.Server.Application.Features.Friends;

public sealed record SetUserBlock(Guid UserId, Guid OtherUserId, bool Block) : ICommand<Unit>;

public sealed class SetUserBlockHandler(IAccountRepository accounts, IFriendshipRepository friendships,
    IUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<SetUserBlock, Unit>
{
    public async ValueTask<Unit> Handle(SetUserBlock command, CancellationToken cancellationToken)
    {
        if (command.UserId == command.OtherUserId)
        {
            throw new RequestFailure(FailureKind.Invalid, "SELF_BLOCK_NOT_ALLOWED");
        }

        if (!await accounts.ExistsAsync(command.OtherUserId, cancellationToken))
        {
            throw new RequestFailure(FailureKind.NotFound, "USER_NOT_AVAILABLE");
        }

        await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        Friendship friendship = await friendships.LockAsync(command.UserId, command.OtherUserId, now, cancellationToken);
        UserBlock? block = await friendships.GetBlockAsync(command.UserId, command.OtherUserId, cancellationToken);
        if (command.Block)
        {
            if (block is null)
            {
                friendships.AddBlock(new UserBlock { BlockerId = command.UserId, BlockedId = command.OtherUserId, CreatedAtUtc = now });
            }

            friendship.Status = FriendshipStatus.Removed;
            friendship.UpdatedAtUtc = now;
        }
        else if (block is not null)
        {
            friendships.RemoveBlock(block);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}
