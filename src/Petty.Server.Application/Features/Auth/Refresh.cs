using Mediator;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Accounts;

namespace Petty.Server.Application.Features.Auth;

public sealed record Refresh(string RefreshToken) : ICommand<TokenResponse>;

public sealed class RefreshHandler(IAccountRepository accounts, ISessionRepository sessions, IUnitOfWork unitOfWork,
    IAccessTokenIssuer tokens, TimeProvider clock) : ICommandHandler<Refresh, TokenResponse>
{
    public async ValueTask<TokenResponse> Handle(Refresh command, CancellationToken cancellationToken)
    {
        if (!RefreshTokens.TryGetSessionId(command.RefreshToken, out Guid sessionId))
        {
            throw new RequestFailure(FailureKind.Unauthorized, "INVALID_REFRESH_TOKEN");
        }

        Guid userId = await sessions.FindUserIdAsync(sessionId, cancellationToken)
            ?? throw new RequestFailure(FailureKind.Unauthorized, "INVALID_REFRESH_TOKEN");
        await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await accounts.LockAsync(userId, cancellationToken);
        UserSession? session = await sessions.GetAsync(sessionId, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        if (session is null || !session.IsActive(now))
        {
            throw new RequestFailure(FailureKind.Unauthorized, "SESSION_REVOKED");
        }

        if (!RefreshTokens.Matches(command.RefreshToken, session.RefreshTokenHash))
        {
            throw new RequestFailure(FailureKind.Unauthorized, "INVALID_REFRESH_TOKEN");
        }

        string refreshToken = RefreshTokens.Create(sessionId);
        session.RefreshTokenHash = RefreshTokens.Hash(refreshToken);
        session.RefreshExpiresAtUtc = now.AddDays(30);
        string accessToken = tokens.Issue(userId, sessionId, now, now.AddMinutes(15));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TokenResponse(accessToken, refreshToken, sessionId, now.AddMinutes(15));
    }
}
