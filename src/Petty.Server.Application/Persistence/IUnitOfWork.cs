namespace Petty.Server.Application.Persistence;

public interface IUnitOfWork
{
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
