using Microsoft.EntityFrameworkCore;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Domain.Accounts;

namespace Petty.Server.Infrastructure.Persistence;

public sealed class SessionRepository(ServerDbContext database) : ISessionRepository
{
    public Task<Guid?> FindUserIdAsync(Guid sessionId, CancellationToken cancellationToken) =>
        database.UserSessions.Where(session => session.Id == sessionId).Select(session => (Guid?)session.UserId)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<UserSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken) =>
        database.UserSessions.SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public Task<UserSession?> GetUnrevokedAsync(Guid userId, CancellationToken cancellationToken) =>
        database.UserSessions.SingleOrDefaultAsync(session => session.UserId == userId && session.RevokedAtUtc == null, cancellationToken);

    public Task<bool> IsActiveAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.UserSessions.AnyAsync(session => session.Id == sessionId && session.UserId == userId
            && session.RevokedAtUtc == null && session.RefreshExpiresAtUtc > now, cancellationToken);

    public void Add(UserSession session) => database.UserSessions.Add(session);
}
