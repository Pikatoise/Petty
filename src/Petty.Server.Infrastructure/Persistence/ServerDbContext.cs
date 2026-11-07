using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Accounts;
using Petty.Server.Domain.Friends;
using Petty.Server.Domain.Letters;
using Petty.Server.Infrastructure.Identity;

namespace Petty.Server.Infrastructure.Persistence;

public sealed class ServerDbContext(DbContextOptions<ServerDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options), IUnitOfWork
{
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<Letter> Letters => Set<Letter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Letter>(letter =>
        {
            letter.HasKey(value => value.Id);
            letter.Property(value => value.Body).HasColumnType("text").IsRequired();
            letter.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.SenderId).OnDelete(DeleteBehavior.Restrict);
            letter.HasOne<ApplicationUser>().WithMany().HasForeignKey(value => value.RecipientId).OnDelete(DeleteBehavior.Restrict);
            letter.HasIndex(value => new { value.SenderId, value.ClientRequestId }).IsUnique();
            letter.HasIndex(value => new { value.SenderId, value.RecipientId }).HasFilter("\"ReadAtUtc\" IS NULL");
            letter.HasIndex(value => new { value.RecipientId, value.SentAtUtc, value.Id }).HasFilter("\"ReadAtUtc\" IS NULL");
            letter.HasIndex(value => new { value.SenderId, value.SentAtUtc, value.Id }).IsDescending(false, true, true);
            letter.HasIndex(value => new { value.RecipientId, value.ReadAtUtc, value.Id }).IsDescending(false, true, true);
            letter.ToTable("Letters", table =>
            {
                table.HasCheckConstraint("CK_Letters_DifferentUsers", "\"SenderId\" <> \"RecipientId\"");
                table.HasCheckConstraint("CK_Letters_BodyLength", "char_length(\"Body\") BETWEEN 1 AND 500");
                table.HasCheckConstraint("CK_Letters_ClientRequestId", "\"ClientRequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            });
        });
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
