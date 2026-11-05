using Mediator;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Accounts;

namespace Petty.Server.Application.Features.Auth;

public sealed record Logout(Guid UserId, Guid SessionId) : ICommand<Unit>;

public sealed class LogoutHandler(IAccountRepository accounts, ISessionRepository sessions, IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<Logout, Unit>
{
    public async ValueTask<Unit> Handle(Logout command, CancellationToken cancellationToken)
    {
        await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await accounts.LockAsync(command.UserId, cancellationToken);
        UserSession? session = await sessions.GetAsync(command.SessionId, cancellationToken);
        if (session is not null && session.UserId == command.UserId && session.RevokedAtUtc is null)
        {
            session.RevokedAtUtc = clock.GetUtcNow();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}
