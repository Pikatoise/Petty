using Mediator;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Application.Features.Friends;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Friends;
using Petty.Server.Domain.Letters;

namespace Petty.Server.Application.Features.Letters;

public sealed record SendLetter(Guid UserId, Guid RecipientUserId, string Body, Guid ClientRequestId) : ICommand<SentLetter>;

public sealed class SendLetterHandler(IAccountRepository accounts, IFriendshipRepository friendships, ILetterRepository letters,
    IUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<SendLetter, SentLetter>
{
    public async ValueTask<SentLetter> Handle(SendLetter command, CancellationToken cancellationToken)
    {
        if (command.RecipientUserId == Guid.Empty || command.RecipientUserId == command.UserId
            || command.ClientRequestId == Guid.Empty || !LetterRules.IsValidBody(command.Body))
        {
            throw new RequestFailure(FailureKind.Invalid, "INVALID_LETTER");
        }

        Letter? existing = await letters.FindSentAsync(command.UserId, command.ClientRequestId, cancellationToken);
        if (existing is not null)
        {
            return await Replay(existing, command, cancellationToken);
        }

        if (!await accounts.ExistsAsync(command.RecipientUserId, cancellationToken))
        {
            throw new RequestFailure(FailureKind.Forbidden, "FRIENDSHIP_REQUIRED");
        }

        await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        Friendship friendship = await friendships.LockAsync(command.UserId, command.RecipientUserId, clock.GetUtcNow(), cancellationToken);
        existing = await letters.FindSentAsync(command.UserId, command.ClientRequestId, cancellationToken);
        if (existing is not null)
        {
            SentLetter replay = await Replay(existing, command, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        if (friendship.Status != FriendshipStatus.Accepted
            || await friendships.IsBlockedAsync(command.UserId, command.RecipientUserId, cancellationToken))
        {
            throw new RequestFailure(FailureKind.Forbidden, "FRIENDSHIP_REQUIRED");
        }

        int unread = await letters.CountUnreadAsync(command.UserId, command.RecipientUserId, cancellationToken);
        if (unread >= LetterRules.UnreadLimit)
        {
            throw new RequestFailure(FailureKind.Conflict, "UNREAD_MAIL_LIMIT_REACHED", new Dictionary<string, object?>
            {
                ["unreadCount"] = unread,
                ["limit"] = LetterRules.UnreadLimit
            });
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        var letter = new Letter
        {
            Id = Guid.NewGuid(),
            SenderId = command.UserId,
            RecipientId = command.RecipientUserId,
            Body = command.Body,
            ClientRequestId = command.ClientRequestId,
            SentAtUtc = now
        };
        letters.Add(letter);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SentLetter(letter.Id, letter.SentAtUtc, unread + 1);
    }

    private async Task<SentLetter> Replay(Letter letter, SendLetter command, CancellationToken cancellationToken)
    {
        if (letter.RecipientId != command.RecipientUserId || !string.Equals(letter.Body, command.Body, StringComparison.Ordinal))
        {
            throw new RequestFailure(FailureKind.Conflict, "IDEMPOTENCY_KEY_REUSED");
        }

        return new SentLetter(letter.Id, letter.SentAtUtc,
            await letters.CountUnreadAsync(command.UserId, command.RecipientUserId, cancellationToken));
    }
}
