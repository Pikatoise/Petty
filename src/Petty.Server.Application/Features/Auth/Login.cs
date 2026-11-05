using Mediator;
using Petty.Server.Application.Notifications;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Accounts;

namespace Petty.Server.Application.Features.Auth;

public sealed record Login(string UserName, string Password) : ICommand<TokenResponse>;
public sealed record TokenResponse(string AccessToken, string RefreshToken, Guid SessionId, DateTimeOffset ExpiresAtUtc);

public sealed class LoginHandler(IIdentityService identity, IAccountRepository accounts, ISessionRepository sessions,
    IUnitOfWork unitOfWork, IAccessTokenIssuer tokens, TimeProvider clock, IMailNotifier notifier) : ICommandHandler<Login, TokenResponse>
{
    public async ValueTask<TokenResponse> Handle(Login command, CancellationToken cancellationToken)
    {
        string login = RegisterHandler.NormalizeLogin(command.UserName);
        if (command.Password is not { Length: > 0 and <= 128 })
        {
            throw new RequestFailure(FailureKind.Unauthorized, "INVALID_CREDENTIALS");
        }

        Guid userId = await accounts.FindIdByLoginAsync(login, cancellationToken)
            ?? throw new RequestFailure(FailureKind.Unauthorized, "INVALID_CREDENTIALS");

        await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await accounts.LockAsync(userId, cancellationToken);
        if (await identity.VerifyPasswordAsync(login, command.Password, cancellationToken) != userId)
        {
            throw new RequestFailure(FailureKind.Unauthorized, "INVALID_CREDENTIALS");
        }

        DateTimeOffset now = clock.GetUtcNow();
        UserSession? previous = await sessions.GetUnrevokedAsync(userId, cancellationToken);
        if (previous is not null)
        {
            previous.RevokedAtUtc = now;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var sessionId = Guid.NewGuid();
        string refreshToken = RefreshTokens.Create(sessionId);
        sessions.Add(new UserSession
        {
            Id = sessionId,
            UserId = userId,
            CreatedAtUtc = now,
            RefreshExpiresAtUtc = now.AddDays(30),
            RefreshTokenHash = RefreshTokens.Hash(refreshToken)
        });
        string accessToken = tokens.Issue(userId, sessionId, now, now.AddMinutes(15));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (previous is not null)
        {
            await notifier.SessionRevokedAsync(previous.Id, cancellationToken);
        }

        return new TokenResponse(accessToken, refreshToken, sessionId, now.AddMinutes(15));
    }
}
