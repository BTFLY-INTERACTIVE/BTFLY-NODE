using Btfly.API.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Btfly.API.Data;

public class BtflyDbContext(DbContextOptions<BtflyDbContext> options) : DbContext(options)
{
    public DbSet<CloudlightAccount> CloudlightAccounts => Set<CloudlightAccount>();
    public DbSet<NodeServer>        NodeServers        => Set<NodeServer>();
    public DbSet<NodeAccount>       NodeAccounts       => Set<NodeAccount>();
    public DbSet<PendingLoginKey>   PendingLoginKeys   => Set<PendingLoginKey>();
    public DbSet<Post>              Posts              => Set<Post>();
    public DbSet<PostLike>          PostLikes          => Set<PostLike>();
    public DbSet<Follow>            Follows            => Set<Follow>();
    public DbSet<NodeBan>           NodeBans           => Set<NodeBan>();
    public DbSet<PostBackup>        PostBackups        => Set<PostBackup>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // ── CloudlightAccount ──────────────────────────────────────────────────
        model.Entity<CloudlightAccount>(e =>
        {
            e.HasIndex(a => a.Auth0Sub).IsUnique();
            e.HasIndex(a => a.Email).IsUnique();
        });

        // ── NodeServer ─────────────────────────────────────────────────────────
        model.Entity<NodeServer>(e =>
        {
            e.HasIndex(n => n.Domain).IsUnique();
        });

        // ── NodeAccount ────────────────────────────────────────────────────────
        model.Entity<NodeAccount>(e =>
        {
            e.HasIndex(a => new { a.CloudlightAccountId, a.NodeServerId }).IsUnique();
            e.HasIndex(a => new { a.NodeServerId, a.Username }).IsUnique();

            e.HasOne(a => a.CloudlightAccount)
             .WithMany(c => c.NodeAccounts)
             .HasForeignKey(a => a.CloudlightAccountId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(a => a.NodeServer)
             .WithMany(n => n.Accounts)
             .HasForeignKey(a => a.NodeServerId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Follow ─────────────────────────────────────────────────────────────
        model.Entity<Follow>(e =>
        {
            e.HasIndex(f => new { f.FollowerId, f.FollowingId }).IsUnique();

            e.HasOne(f => f.Follower)
             .WithMany(a => a.Following)
             .HasForeignKey(f => f.FollowerId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(f => f.Following)
             .WithMany(a => a.Followers)
             .HasForeignKey(f => f.FollowingId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PostLike ───────────────────────────────────────────────────────────
        model.Entity<PostLike>(e =>
        {
            e.HasIndex(l => new { l.PostId, l.NodeAccountId }).IsUnique();
        });

        // ── Post ───────────────────────────────────────────────────────────────
        model.Entity<Post>(e =>
        {
            e.HasOne(p => p.Author)
             .WithMany(a => a.Posts)
             .HasForeignKey(p => p.AuthorId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.ParentPost)
             .WithMany(p => p.Replies)
             .HasForeignKey(p => p.ParentPostId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
