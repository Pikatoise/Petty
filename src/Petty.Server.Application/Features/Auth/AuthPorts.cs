using Petty.Server.Domain.Accounts;

namespace Petty.Server.Application.Features.Auth;

public interface IIdentityService
{
    Task<Guid> RegisterAsync(string login, string password, CancellationToken cancellationToken);
    Task<Guid?> VerifyPasswordAsync(string login, string password, CancellationToken cancellationToken);
}

public interface IAccountRepository
{
    Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken);
    Task<Guid?> FindIdByLoginAsync(string login, CancellationToken cancellationToken);
    Task LockAsync(Guid userId, CancellationToken cancellationToken);
    Task<AccountProfile> GetProfileAsync(Guid userId, CancellationToken cancellationToken);
    Task UpdatePetAsync(Guid userId, string petName, string assetId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface ISessionRepository
{
    Task<Guid?> GetActiveIdAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<Guid?> FindUserIdAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<UserSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<UserSession?> GetUnrevokedAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> IsActiveAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken);
    void Add(UserSession session);
}

public interface IAccessTokenIssuer
{
    string Issue(Guid userId, Guid sessionId, DateTimeOffset now, DateTimeOffset expiresAt);
}
