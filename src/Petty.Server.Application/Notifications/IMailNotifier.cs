namespace Petty.Server.Application.Notifications;

// Best-effort wakeups after commit. REST remains authoritative; implementations do not propagate push failures.
public interface IMailNotifier
{
    Task MailboxChangedAsync(Guid userId, CancellationToken cancellationToken);
    Task FriendshipsChangedAsync(Guid firstUserId, Guid secondUserId, bool refreshMailbox, CancellationToken cancellationToken);
    Task SessionRevokedAsync(Guid sessionId, CancellationToken cancellationToken);
}

public static class MailSignals
{
    public const string MailboxChanged = "MailboxChanged";
    public const string FriendshipsChanged = "FriendshipsChanged";
    public const string SessionRevoked = "SessionRevoked";
}
