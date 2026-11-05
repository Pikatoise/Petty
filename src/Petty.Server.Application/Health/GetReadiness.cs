using Mediator;

namespace Petty.Server.Application.Health;

public interface IDatabaseReadiness
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public sealed record GetReadiness : IQuery<bool>;

public sealed class GetReadinessHandler(IDatabaseReadiness database) : IQueryHandler<GetReadiness, bool>
{
    public async ValueTask<bool> Handle(GetReadiness query, CancellationToken cancellationToken) =>
        await database.IsReadyAsync(cancellationToken);
}
