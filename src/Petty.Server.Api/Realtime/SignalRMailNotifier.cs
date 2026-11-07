using Microsoft.AspNetCore.SignalR;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Application.Notifications;
using Petty.Server.Application.Persistence;

namespace Petty.Server.Api.Realtime;

public sealed class SignalRMailNotifier(IHubContext<MailHub> hub, IAccountRepository accounts, ISessionRepository sessions,
    IUnitOfWork unitOfWork, TimeProvider clock, ILogger<SignalRMailNotifier> logger) : IMailNotifier
{
    public Task MailboxChangedAsync(Guid userId, CancellationToken cancellationToken) =>
        NotifyUser(userId, [MailSignals.MailboxChanged], cancellationToken);

    public async Task FriendshipsChangedAsync(Guid firstUserId, Guid secondUserId, bool refreshMailbox, CancellationToken cancellationToken)
    {
        string[] events = refreshMailbox ? [MailSignals.FriendshipsChanged, MailSignals.MailboxChanged] : [MailSignals.FriendshipsChanged];
        await NotifyUser(firstUserId, events, cancellationToken);
        await NotifyUser(secondUserId, events, cancellationToken);
    }

    public async Task SessionRevokedAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await hub.Clients.User(sessionId.ToString("N")).SendCoreAsync(MailSignals.SessionRevoked, [], timeout.Token);
        }
        catch (Exception exception)
        {
            LogFailure(MailSignals.SessionRevoked, exception);
        }
    }

    private async Task NotifyUser(Guid userId, string[] events, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(timeout.Token);
            // Login/logout use the same account lock, so revocation cannot race the routing decision.
            await accounts.LockAsync(userId, timeout.Token);
            Guid? active = await sessions.GetActiveIdAsync(userId, clock.GetUtcNow(), timeout.Token);
            if (active is Guid sessionId)
            {
                foreach (string signal in events)
                {
                    await hub.Clients.User(sessionId.ToString("N")).SendCoreAsync(signal, [], timeout.Token);
                }
            }

            await transaction.CommitAsync(timeout.Token);
        }
        catch (Exception exception)
        {
            LogFailure("Mailbox/Friendships", exception);
        }
    }

    private void LogFailure(string signal, Exception exception) =>
        logger.LogWarning("SignalR {Signal} notification failed ({FailureType}); clients must resynchronize through REST.", signal, exception.GetType().Name);
}
