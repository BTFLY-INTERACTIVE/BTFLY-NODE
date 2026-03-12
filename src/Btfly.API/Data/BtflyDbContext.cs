using Btfly.API.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Btfly.API.Data;

public class BtflyDbContext(DbContextOptions<BtflyDbContext> options) : DbContext(options)
{
    public DbSet<CloudlightAccount> CloudlightAccounts => Set<CloudlightAccount>();
    public DbSet<NodeServer>        NodeServers         => Set<NodeServer>();
    public DbSet<NodeAccount>       NodeAccounts        => Set<NodeAccount>();
    public DbSet<PendingLoginKey>   PendingLoginKeys    => Set<PendingLoginKey>();
    public DbSet<Post>              Posts               => Set<Post>();
    public DbSet<PostLike>          PostLikes           => Set<PostLike>();
    public DbSet<Bookmark>          Bookmarks           => Set<Bookmark>();
    public DbSet<Follow>            Follows             => Set<Follow>();
    public DbSet<Notification>      Notifications       => Set<Notification>();
    public DbSet<NodeBan>           NodeBans            => Set<NodeBan>();
    public DbSet<PostBackup>        PostBackups         => Set<PostBackup>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        // ── CloudlightAccount ──────────────────────────────────────────────────
        m.Entity<CloudlightAccount>()
            .HasIndex(a => a.Auth0Sub).IsUnique();
        m.Entity<CloudlightAccount>()
            .HasIndex(a => a.Email);

        // ── NodeAccount ────────────────────────────────────────────────────────
        m.Entity<NodeAccount>()
            .HasIndex(a => new { a.NodeServerId, a.Username }).IsUnique();
        m.Entity<NodeAccount>()
            .HasIndex(a => new { a.CloudlightAccountId, a.NodeServerId }).IsUnique();
        m.Entity<NodeAccount>()
            .HasOne(a => a.CloudlightAccount)
            .WithMany(g => g.NodeAccounts)
            .HasForeignKey(a => a.CloudlightAccountId)
            .OnDelete(DeleteBehavior.Cascade);
        m.Entity<NodeAccount>()
            .HasOne(a => a.NodeServer)
            .WithMany(n => n.Accounts)
            .HasForeignKey(a => a.NodeServerId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Post ──────────────────────────────────────────────────────────────
        m.Entity<Post>()
            .HasOne(p => p.Author)
            .WithMany(a => a.Posts)
            .HasForeignKey(p => p.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);
        m.Entity<Post>()
            .HasOne(p => p.ParentPost)
            .WithMany(p => p.Replies)
            .HasForeignKey(p => p.ParentPostId)
            .OnDelete(DeleteBehavior.Restrict);
        m.Entity<Post>()
            .HasOne(p => p.ReflyOfPost)
            .WithMany(p => p.Reflys)
            .HasForeignKey(p => p.ReflyOfPostId)
            .OnDelete(DeleteBehavior.Restrict);
        m.Entity<Post>()
            .HasIndex(p => new { p.NodeServerId, p.CreatedAt });
        m.Entity<Post>()
            .HasIndex(p => p.AuthorId);

        // ── PostLike ──────────────────────────────────────────────────────────
        m.Entity<PostLike>()
            .HasIndex(l => new { l.PostId, l.NodeAccountId }).IsUnique();
        m.Entity<PostLike>()
            .HasOne(l => l.Post).WithMany(p => p.Likes)
            .HasForeignKey(l => l.PostId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<PostLike>()
            .HasOne(l => l.NodeAccount).WithMany(a => a.Likes)
            .HasForeignKey(l => l.NodeAccountId).OnDelete(DeleteBehavior.Cascade);

        // ── Bookmark ──────────────────────────────────────────────────────────
        m.Entity<Bookmark>()
            .HasIndex(b => new { b.PostId, b.NodeAccountId }).IsUnique();
        m.Entity<Bookmark>()
            .HasOne(b => b.Post).WithMany(p => p.Bookmarks)
            .HasForeignKey(b => b.PostId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<Bookmark>()
            .HasOne(b => b.NodeAccount).WithMany(a => a.Bookmarks)
            .HasForeignKey(b => b.NodeAccountId).OnDelete(DeleteBehavior.Cascade);

        // ── Follow ────────────────────────────────────────────────────────────
        m.Entity<Follow>()
            .HasIndex(f => new { f.FollowerId, f.FollowingId }).IsUnique();
        m.Entity<Follow>()
            .HasOne(f => f.Follower)
            .WithMany(a => a.Following)
            .HasForeignKey(f => f.FollowerId)
            .OnDelete(DeleteBehavior.Cascade);
        m.Entity<Follow>()
            .HasOne(f => f.Following)
            .WithMany(a => a.Followers)
            .HasForeignKey(f => f.FollowingId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Notification ──────────────────────────────────────────────────────
        m.Entity<Notification>()
            .HasOne(n => n.Recipient).WithMany(a => a.Notifications)
            .HasForeignKey(n => n.RecipientId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<Notification>()
            .HasOne(n => n.Actor).WithMany()
            .HasForeignKey(n => n.ActorId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<Notification>()
            .HasOne(n => n.Post).WithMany()
            .HasForeignKey(n => n.PostId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<Notification>()
            .HasIndex(n => new { n.RecipientId, n.IsRead, n.CreatedAt });

        // ── NodeBan ───────────────────────────────────────────────────────────
        m.Entity<NodeBan>()
            .HasOne(b => b.NodeServer).WithMany(n => n.NodeBans)
            .HasForeignKey(b => b.NodeServerId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<NodeBan>()
            .HasOne(b => b.NodeAccount).WithMany()
            .HasForeignKey(b => b.NodeAccountId).OnDelete(DeleteBehavior.Cascade);

        // ── PostBackup ────────────────────────────────────────────────────────
        m.Entity<PostBackup>()
            .HasOne(b => b.Post).WithMany(p => p.Backups)
            .HasForeignKey(b => b.PostId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<PostBackup>()
            .HasOne(b => b.CloudlightAccount).WithMany(a => a.PostBackups)
            .HasForeignKey(b => b.CloudlightAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
