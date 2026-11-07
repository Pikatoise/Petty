using Mediator;
using Petty.Server.Application.Features.Friends;
using Petty.Server.Application.Notifications;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Friends;
using Petty.Server.Domain.Letters;

namespace Petty.Server.Application.Features.Letters;

public sealed record ReadLetters(Guid UserId, Guid[] LetterIds) : ICommand<ReadLettersResult>;

public sealed class ReadLettersHandler(IFriendshipRepository friendships, ILetterRepository letters, IUnitOfWork unitOfWork,
    TimeProvider clock, IMailNotifier notifier) : ICommandHandler<ReadLetters, ReadLettersResult>
{
    public async ValueTask<ReadLettersResult> Handle(ReadLetters command, CancellationToken cancellationToken)
    {
        if (command.LetterIds is not { Length: >= 1 and <= LetterRules.UnreadLimit }
            || command.LetterIds.Contains(Guid.Empty) || command.LetterIds.Distinct().Count() != command.LetterIds.Length)
        {
            throw new RequestFailure(FailureKind.Invalid, "INVALID_LETTER_IDS");
        }

        Guid sender = await letters.FindReceivedSenderAsync(command.UserId, command.LetterIds[0], cancellationToken)
            ?? throw new RequestFailure(FailureKind.NotFound, "LETTER_NOT_AVAILABLE");
        await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        Friendship friendship = await friendships.LockAsync(command.UserId, sender, clock.GetUtcNow(), cancellationToken);
        IReadOnlyList<Letter> selected = await letters.GetReceivedAsync(command.UserId, command.LetterIds, cancellationToken);
        if (selected.Count != command.LetterIds.Length)
        {
            throw new RequestFailure(FailureKind.NotFound, "LETTER_NOT_AVAILABLE");
        }

        if (selected.Any(letter => letter.SenderId != sender))
        {
            throw new RequestFailure(FailureKind.Invalid, "MIXED_LETTER_SENDERS");
        }

        if (friendship.Status != FriendshipStatus.Accepted || await friendships.IsBlockedAsync(command.UserId, sender, cancellationToken))
        {
            throw new RequestFailure(FailureKind.Forbidden, "FRIENDSHIP_REQUIRED");
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        bool changed = selected.Any(letter => letter.ReadAtUtc is null);
        foreach (Letter letter in selected)
        {
            letter.ReadAtUtc ??= now;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        int unread = await letters.CountUnreadAsync(sender, command.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (changed)
        {
            await notifier.MailboxChangedAsync(command.UserId, cancellationToken);
        }

        return new ReadLettersResult(selected.Select(letter => new ReadLetterConfirmation(letter.Id, letter.ReadAtUtc!.Value)).ToArray(), unread);
    }
}
