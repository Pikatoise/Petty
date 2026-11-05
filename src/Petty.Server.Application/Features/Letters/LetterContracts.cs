using Petty.Server.Application.Features.Friends;
using Petty.Server.Domain.Letters;

namespace Petty.Server.Application.Features.Letters;

public sealed record MailQuota(int UnreadCount, int Limit = LetterRules.UnreadLimit);
public sealed record SentLetter(Guid LetterId, DateTimeOffset SentAtUtc, int UnreadCount, int Limit = LetterRules.UnreadLimit);
public sealed record ReadLetterConfirmation(Guid LetterId, DateTimeOffset ReadAtUtc);
public sealed record ReadLettersResult(IReadOnlyList<ReadLetterConfirmation> Letters, int UnreadCount, int Limit = LetterRules.UnreadLimit);
public sealed record LetterHistoryItem(Guid Id, Guid SenderId, Guid RecipientId, string Body, DateTimeOffset SentAtUtc,
    DateTimeOffset? ReadAtUtc, FriendDto OtherUser);
public sealed record LetterHistoryPage(IReadOnlyList<LetterHistoryItem> Items, string? NextCursor);
public sealed record LetterCursor(DateTimeOffset Timestamp, Guid LetterId);

public interface ILetterRepository
{
    Task<Letter?> FindSentAsync(Guid senderId, Guid clientRequestId, CancellationToken cancellationToken);
    Task<int> CountUnreadAsync(Guid senderId, Guid recipientId, CancellationToken cancellationToken);
    Task<Guid?> FindReceivedSenderAsync(Guid recipientId, Guid letterId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Letter>> GetReceivedAsync(Guid recipientId, Guid[] letterIds, CancellationToken cancellationToken);
    void Add(Letter letter);
    Task<IReadOnlyList<LetterHistoryItem>> GetHistoryAsync(Guid userId, bool incoming, LetterCursor? cursor, int count,
        CancellationToken cancellationToken);
}
