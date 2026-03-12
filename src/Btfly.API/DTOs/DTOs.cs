using Btfly.API.Models.Enums;

namespace Btfly.API.DTOs;

// ─── Auth ─────────────────────────────────────────────────────────────────────

/// <summary>Step 1: Node generates a one-time key, redirects user to Cloudlight auth.</summary>
/// <param name="ReturnUrl">
/// The URL Cloudlight should redirect back to after auth.
/// Pass the client app URL (e.g. https://app.btfly.social/) so tokens
/// land on the right page. Defaults to https://{nodeDomain}/auth/complete.
/// </param>
public record GenerateKeyRequest(string? ReturnUrl);

public record GenerateKeyResponse(string Key, string CloudlightLoginUrl, DateTime ExpiresAt);

/// <summary>
/// Step 2: After Auth0 login at api.login.btfly.social, the user is redirected
/// back to the node with a BTFLY global token and the original node key.
/// The node POSTs both here to complete its own session.
/// </summary>
public record CompleteNodeLoginRequest(
    string BtflyToken,   // JWT from api.login.btfly.social
    string NodeDomain,
    string Key
);

public record NodeLoginResponse(string NodeToken, NodeAccountDto NodeAccount);

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
    ServerType ServerType,
    bool IsReplicationOnly,
    bool AllowReadOnlyFederation,
    int MemberCount,
    DateTime RegisteredAt
);

// ─── Accounts ─────────────────────────────────────────────────────────────────

public record NodeAccountDto(
    Guid Id,
    string Username,
    string? Bio,
    string? AvatarUrl,
    string NodeDomain,
    bool IsPro,
    int FollowerCount,
    int FollowingCount,
    DateTime JoinedAt
);

public record UpdateProfileRequest(string? Bio, string? AvatarUrl);

// ─── Posts ────────────────────────────────────────────────────────────────────

public record CreatePostRequest(string Content, string? MediaUrl, Guid? ParentPostId);

public record PostDto(
    Guid Id,
    string Content,
    string? MediaUrl,
    NodeAccountDto Author,
    Guid? ParentPostId,
    int LikeCount,
    int ReplyCount,
    bool IsReplicated,
    DateTime CreatedAt,
    DateTime? EditedAt
);

// ─── Moderation ───────────────────────────────────────────────────────────────

public record GlobalBanRequest(string Reason);

public record NodeBanRequest(string Reason, Guid NodeAccountId);

// ─── Feed ─────────────────────────────────────────────────────────────────────

public record FeedResponse(IEnumerable<PostDto> Posts, int TotalCount, int Page, int PageSize);
