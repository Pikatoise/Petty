namespace Petty.Online.Client;

// Wire contracts intentionally have no dependency on server or local pet assemblies.
public sealed record AccountProfile(Guid UserId, string Login, string? PetName, string? PetAssetId, DateTimeOffset? ProfileUpdatedUtc);
public sealed record PetProfile(string PetName, string AssetId);
public sealed record Friend(Guid UserId, string Login, string? PetName, string? PetAssetId);
public sealed record BlockedUser(Guid UserId, string Login);
public sealed record FriendRequest(Guid RequestId, Friend User, DateTimeOffset RequestedAtUtc);
public sealed record FriendRequests(IReadOnlyList<FriendRequest> Incoming, IReadOnlyList<FriendRequest> Outgoing);
public sealed record MailQuota(int UnreadCount, int Limit);
public sealed record SendLetter(Guid RecipientUserId, string Body, Guid ClientRequestId);
public sealed record SentLetter(Guid LetterId, DateTimeOffset SentAtUtc, int UnreadCount, int Limit);
public sealed record ReadLetter(Guid LetterId, DateTimeOffset ReadAtUtc);
public sealed record ReadLettersResult(IReadOnlyList<ReadLetter> Letters, int UnreadCount, int Limit);
public sealed record LetterHistoryItem(Guid Id, Guid SenderId, Guid RecipientId, string Body, DateTimeOffset SentAtUtc,
    DateTimeOffset? ReadAtUtc, Friend OtherUser);
public sealed record LetterHistoryPage(IReadOnlyList<LetterHistoryItem> Items, string? NextCursor);
public sealed record DeliveryLetter(Guid Id, string Body, DateTimeOffset SentAtUtc);
public sealed record MailboxVisit(Friend Sender, IReadOnlyList<DeliveryLetter> Letters);

// Do not log or bind credentials to UI diagnostics (including generated record.ToString()).
public sealed class SessionTokens
{
    public required string AccessToken
    {
        get; init;
    }
    public required string RefreshToken
    {
        get; init;
    }
    public Guid SessionId
    {
        get; init;
    }
    public DateTimeOffset ExpiresAtUtc
    {
        get; init;
    }
    public override string ToString() => "[Protected session]";
}

public interface ITokenStore
{
    Task<SessionTokens?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
