using Btfly.API.DTOs;
using Btfly.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Btfly.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class AccountsController(IAccountService accountService, IPostService postService, INotificationService notificationService) : ControllerBase
{
    private Guid NodeAccountId => Guid.Parse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? throw new UnauthorizedAccessException("Missing node account claim."));

    // ─── Profile ──────────────────────────────────────────────────────────────

    /// <summary>Get a user profile by username on a given node.</summary>
    [HttpGet("nodes/{nodeServerId}/accounts/{username}")]
    [AllowAnonymous]
    public async Task<ActionResult<NodeAccountDto>> GetProfile(Guid nodeServerId, string username)
    {
        Guid? viewerId = User.Identity?.IsAuthenticated == true ? NodeAccountId : null;
        var profile = await accountService.GetProfileAsync(username, nodeServerId, viewerId);
        return Ok(profile);
    }

    /// <summary>Get posts by a specific user.</summary>
    [HttpGet("nodes/{nodeServerId}/accounts/{username}/posts")]
    [AllowAnonymous]
    public async Task<ActionResult<FeedResponse>> GetAccountPosts(
        Guid nodeServerId, string username,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        Guid? viewerId = User.Identity?.IsAuthenticated == true ? NodeAccountId : null;
        var profile = await accountService.GetProfileAsync(username, nodeServerId);
        var feed = await postService.GetAccountPostsAsync(profile.Id, page, pageSize, viewerId);
        return Ok(feed);
    }

    /// <summary>Update your profile (bio, avatar, header, website, location).</summary>
    [HttpPatch("accounts/me")]
    public async Task<ActionResult<NodeAccountDto>> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        var account = await accountService.UpdateProfileAsync(NodeAccountId, req);
        return Ok(account);
    }

    // ─── Follow / Unfollow ────────────────────────────────────────────────────

    /// <summary>Follow a user.</summary>
    [HttpPost("accounts/{targetNodeAccountId}/follow")]
    public async Task<ActionResult<FollowResponse>> Follow(Guid targetNodeAccountId)
    {
        var result = await accountService.FollowAsync(NodeAccountId, targetNodeAccountId);
        return Ok(result);
    }

    /// <summary>Unfollow a user.</summary>
    [HttpDelete("accounts/{targetNodeAccountId}/follow")]
    public async Task<ActionResult<FollowResponse>> Unfollow(Guid targetNodeAccountId)
    {
        var result = await accountService.UnfollowAsync(NodeAccountId, targetNodeAccountId);
        return Ok(result);
    }

    /// <summary>Get followers of a user.</summary>
    [HttpGet("accounts/{nodeAccountId}/followers")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<NodeAccountDto>>> GetFollowers(Guid nodeAccountId)
    {
        var followers = await accountService.GetFollowersAsync(nodeAccountId);
        return Ok(followers);
    }

    /// <summary>Get accounts a user is following.</summary>
    [HttpGet("accounts/{nodeAccountId}/following")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<NodeAccountDto>>> GetFollowing(Guid nodeAccountId)
    {
        var following = await accountService.GetFollowingAsync(nodeAccountId);
        return Ok(following);
    }

    // ─── Search ───────────────────────────────────────────────────────────────

    /// <summary>Search posts and users on a node by keyword.</summary>
    [HttpGet("nodes/{nodeServerId}/search")]
    [AllowAnonymous]
    public async Task<ActionResult<SearchResponse>> Search(
        Guid nodeServerId, [FromQuery] string q)
    {
        Guid? viewerId = User.Identity?.IsAuthenticated == true ? NodeAccountId : null;
        var results = await accountService.SearchAsync(q, nodeServerId, viewerId);
        return Ok(results);
    }

    // ─── Notifications ────────────────────────────────────────────────────────

    /// <summary>Get your notifications.</summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<NotificationsResponse>> GetNotifications(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var notifs = await notificationService.GetNotificationsAsync(NodeAccountId, page, pageSize);
        return Ok(notifs);
    }

    /// <summary>Get unread notification count.</summary>
    [HttpGet("notifications/unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount()
    {
        var count = await notificationService.GetUnreadCountAsync(NodeAccountId);
        return Ok(count);
    }

    /// <summary>Mark all notifications as read.</summary>
    [HttpPost("notifications/mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        await notificationService.MarkAllReadAsync(NodeAccountId);
        return NoContent();
    }

    /// <summary>Mark a specific notification as read.</summary>
    [HttpPost("notifications/{notificationId}/mark-read")]
    public async Task<IActionResult> MarkRead(Guid notificationId)
    {
        await notificationService.MarkReadAsync(notificationId, NodeAccountId);
        return NoContent();
    }
}
