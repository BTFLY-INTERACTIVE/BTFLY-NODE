using Btfly.API.Models.Enums;

namespace Btfly.API.DTOs;

// ─── Auth ─────────────────────────────────────────────────────────────────────

public record GenerateKeyRequest(string? ReturnUrl);
public record GenerateKeyResponse(string Key, string CloudlightLoginUrl, DateTime ExpiresAt);

public record CompleteNodeLoginRequest(string BtflyToken, string NodeDomain, string Key);

/// <summary>
/// Returned after a successful node login.
/// When RequiresUsernameSetup is true, the client should prompt the user to
/// choose a username before continuing — call POST /api/auth/setup-username.
/// </summary>
public record NodeLoginResponse(
    string NodeToken,
    NodeAccountDto NodeAccount,
    bool RequiresUsernameSetup
);

public record SetupUsernameRequest(string Username);

// ─── Node Servers ─────────────────────────────────────────────────────────────

public record RegisterNodeRequest(
    string Domain,
    string DisplayName,
    string? Description,
    ServerType ServerType,
    bool IsReplicationOnly = false,
    bool AllowReadOnlyFederation = false
);

public record NodeServerDto(
    Guid Id,
    string Domain,
    string DisplayName,
    string? Description,
    string? BannerUrl,
    string? IconUrl,
    ServerType ServerType,
    bool IsReplicationOnly,
    bool AllowReadOnlyFederation,
    int MemberCount,
    int MaxPostLength,
    DateTime RegisteredAt
);

// ─── Accounts ─────────────────────────────────────────────────────────────────

public record NodeAccountDto(
    Guid Id,
    string Username,
    string? Bio,
    string? AvatarUrl,
    string? HeaderUrl,
    string? WebsiteUrl,
    string? Location,
    string NodeDomain,
    bool IsPro,
    bool IsNodeAdmin,
    int FollowerCount,
    int FollowingCount,
    int PostCount,
    bool RequiresUsernameSetup,
    DateTime JoinedAt
);

public record UpdateProfileRequest(
    string? Bio,
    string? AvatarUrl,
    string? HeaderUrl,
    string? WebsiteUrl,
    string? Location
);

// ─── Posts ────────────────────────────────────────────────────────────────────

public record CreatePostRequest(
    string Content,
    string? MediaUrl,
    string? ContentWarning,
    Guid? ParentPostId
);

public record EditPostRequest(string Content, string? ContentWarning);

public record PostDto(
    Guid Id,
    string Content,
    string? MediaUrl,
    string? ContentWarning,
    NodeAccountDto Author,
    Guid? ParentPostId,
    PostDto? ReflyOf,
    int LikeCount,
    int ReplyCount,
    int ReflyCount,
    bool IsLikedByMe,
    bool IsBookmarkedByMe,
    bool IsRefliedByMe,
    bool IsReplicated,
    DateTime CreatedAt,
    DateTime? EditedAt
);

// ─── Follows ──────────────────────────────────────────────────────────────────

public record FollowResponse(bool IsFollowing, int FollowerCount);

// ─── Bookmarks ────────────────────────────────────────────────────────────────

public record BookmarkResponse(bool IsBookmarked);

// ─── Notifications ────────────────────────────────────────────────────────────

public record NotificationDto(
    Guid Id,
    NotificationType Type,
    NodeAccountDto? Actor,
    PostDto? Post,
    bool IsRead,
    DateTime CreatedAt
);

public record NotificationsResponse(
    IEnumerable<NotificationDto> Notifications,
    int UnreadCount,
    int TotalCount,
    int Page,
    int PageSize
);

// ─── Search ───────────────────────────────────────────────────────────────────

public record SearchResponse(
    IEnumerable<PostDto> Posts,
    IEnumerable<NodeAccountDto> Accounts,
    int TotalPosts,
    int TotalAccounts
);

// ─── Moderation ───────────────────────────────────────────────────────────────

public record GlobalBanRequest(string Reason);
public record NodeBanRequest(string Reason, Guid NodeAccountId);

// ─── Feed ─────────────────────────────────────────────────────────────────────

public record FeedResponse(IEnumerable<PostDto> Posts, int TotalCount, int Page, int PageSize);
