using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Accounts;
using Petty.Server.Infrastructure.Identity;

namespace Petty.Server.Infrastructure.Persistence;

public sealed class ServerDbContext(DbContextOptions<ServerDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options), IUnitOfWork
{
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(value => value.UserName).HasMaxLength(32);
            user.Property(value => value.NormalizedUserName).HasMaxLength(32);
            user.Property(value => value.PetName).HasMaxLength(24);
            user.Property(value => value.PetAssetId).HasMaxLength(64);
        });
        builder.Entity<UserSession>(session =>
        {
            session.HasKey(value => value.Id);
            session.Property(value => value.RefreshTokenHash).HasMaxLength(64).IsRequired();
            session.HasIndex(value => value.UserId).IsUnique().HasFilter("\"RevokedAtUtc\" IS NULL");
            session.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new UnitOfWorkTransaction(await Database.BeginTransactionAsync(cancellationToken));

    private sealed class UnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
