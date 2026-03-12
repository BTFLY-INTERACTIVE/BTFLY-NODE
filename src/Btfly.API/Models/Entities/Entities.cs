using Btfly.API.Models.Enums;

namespace Btfly.API.Models.Entities;

// ─── Global Identity (Cloudlight) ────────────────────────────────────────────

public class CloudlightAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Auth0Sub { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PictureUrl { get; set; }
    public bool IsPro { get; set; } = false;
    public bool IsGloballyBanned { get; set; } = false;
    public string? GlobalBanReason { get; set; }
    public DateTime? GlobalBannedAt { get; set; }
    public AccountRole Role { get; set; } = AccountRole.User;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public ICollection<NodeAccount> NodeAccounts { get; set; } = [];
    public ICollection<PostBackup> PostBackups { get; set; } = [];
}

// ─── Node Servers ─────────────────────────────────────────────────────────────

public class NodeServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Domain { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BannerUrl { get; set; }
    public string? IconUrl { get; set; }
    public ServerType ServerType { get; set; } = ServerType.Grey;
    public bool IsReplicationOnly { get; set; } = false;
    public bool AllowReadOnlyFederation { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public int MaxPostLength { get; set; } = 500;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public ICollection<NodeAccount> Accounts { get; set; } = [];
    public ICollection<Post> Posts { get; set; } = [];
    public ICollection<PendingLoginKey> PendingLoginKeys { get; set; } = [];
    public ICollection<NodeBan> NodeBans { get; set; } = [];
}

// ─── Authentication ───────────────────────────────────────────────────────────

public class PendingLoginKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public Guid NodeServerId { get; set; }
    public NodeServer NodeServer { get; set; } = null!;
    public bool IsUsed { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(10);
}

// ─── Node Accounts ────────────────────────────────────────────────────────────

public class NodeAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CloudlightAccountId { get; set; }
    public CloudlightAccount CloudlightAccount { get; set; } = null!;
    public Guid NodeServerId { get; set; }
    public NodeServer NodeServer { get; set; } = null!;
    public string Username { get; set; } = string.Empty;

    /// <summary>True until user explicitly sets their username after first login.</summary>
    public bool RequiresUsernameSetup { get; set; } = true;

    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? HeaderUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? Location { get; set; }
    public Guid? PinnedPostId { get; set; }
    public bool IsNodeAdmin { get; set; } = false;
    public bool IsNodeBanned { get; set; } = false;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Post> Posts { get; set; } = [];
    public ICollection<Follow> Following { get; set; } = [];
    public ICollection<Follow> Followers { get; set; } = [];
    public ICollection<PostLike> Likes { get; set; } = [];
    public ICollection<Bookmark> Bookmarks { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}

// ─── Posts ────────────────────────────────────────────────────────────────────

public class Post
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuthorId { get; set; }
    public NodeAccount Author { get; set; } = null!;
    public Guid NodeServerId { get; set; }
    public NodeServer NodeServer { get; set; } = null!;
    public string Content { get; set; } = string.Empty;
    public string? MediaUrl { get; set; }
    public string? ContentWarning { get; set; }
    public Guid? ParentPostId { get; set; }
    public Post? ParentPost { get; set; }

    /// <summary>Set when this is a refly (repost) of another post.</summary>
    public Guid? ReflyOfPostId { get; set; }
    public Post? ReflyOfPost { get; set; }

    public bool IsDeleted { get; set; } = false;
    public bool IsReplicated { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }

    public ICollection<Post> Replies { get; set; } = [];
    public ICollection<Post> Reflys { get; set; } = [];
    public ICollection<PostLike> Likes { get; set; } = [];
    public ICollection<PostBackup> Backups { get; set; } = [];
    public ICollection<Bookmark> Bookmarks { get; set; } = [];
}

public class PostLike
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Guid NodeAccountId { get; set; }
    public NodeAccount NodeAccount { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Bookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Guid NodeAccountId { get; set; }
    public NodeAccount NodeAccount { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─── Federation ───────────────────────────────────────────────────────────────

public class Follow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FollowerId { get; set; }
    public NodeAccount Follower { get; set; } = null!;
    public Guid FollowingId { get; set; }
    public NodeAccount Following { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─── Notifications ────────────────────────────────────────────────────────────

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipientId { get; set; }
    public NodeAccount Recipient { get; set; } = null!;
    public Guid? ActorId { get; set; }
    public NodeAccount? Actor { get; set; }
    public NotificationType Type { get; set; }
    public Guid? PostId { get; set; }
    public Post? Post { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─── Moderation ───────────────────────────────────────────────────────────────

public class NodeBan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NodeServerId { get; set; }
    public NodeServer NodeServer { get; set; } = null!;
    public Guid NodeAccountId { get; set; }
    public NodeAccount NodeAccount { get; set; } = null!;
    public string? Reason { get; set; }
    public DateTime BannedAt { get; set; } = DateTime.UtcNow;
    public Guid IssuedByNodeAccountId { get; set; }
}

// ─── Pro Features ─────────────────────────────────────────────────────────────

public class PostBackup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Guid CloudlightAccountId { get; set; }
    public CloudlightAccount CloudlightAccount { get; set; } = null!;
    public string ContentSnapshot { get; set; } = string.Empty;
    public DateTime BackedUpAt { get; set; } = DateTime.UtcNow;
}
