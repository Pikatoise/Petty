using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Accounts;
using Petty.Server.Domain.Friends;
using Petty.Server.Infrastructure.Identity;

namespace Petty.Server.Infrastructure.Persistence;

public sealed class ServerDbContext(DbContextOptions<ServerDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options), IUnitOfWork
{
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Friendship>(friendship =>
        {
            friendship.HasKey(value => value.Id);
            friendship.HasIndex(value => new { value.LowerUserId, value.HigherUserId }).IsUnique();
            friendship.HasIndex(value => value.RequestId).IsUnique();
            friendship.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.LowerUserId).OnDelete(DeleteBehavior.Restrict);
            friendship.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.HigherUserId).OnDelete(DeleteBehavior.Restrict);
            friendship.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.RequestedById).OnDelete(DeleteBehavior.Restrict);
            friendship.ToTable("Friendships", table =>
            {
                table.HasCheckConstraint("CK_Friendships_CanonicalPair", "\"LowerUserId\" < \"HigherUserId\"");
                table.HasCheckConstraint("CK_Friendships_Requester", "\"RequestedById\" IN (\"LowerUserId\", \"HigherUserId\")");
                table.HasCheckConstraint("CK_Friendships_Status", "\"Status\" BETWEEN 0 AND 2 AND (\"Status\" = 0 OR \"RequestId\" IS NOT NULL)");
            });
        });
        builder.Entity<UserBlock>(block =>
        {
            block.HasKey(value => new { value.BlockerId, value.BlockedId });
            block.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.BlockerId).OnDelete(DeleteBehavior.Restrict);
            block.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.BlockedId).OnDelete(DeleteBehavior.Restrict);
            block.ToTable("UserBlocks", table => table.HasCheckConstraint("CK_UserBlocks_DifferentUsers", "\"BlockerId\" <> \"BlockedId\""));
        });
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
