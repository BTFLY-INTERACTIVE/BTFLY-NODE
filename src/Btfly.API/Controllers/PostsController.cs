using Btfly.API.DTOs;
using Btfly.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Btfly.API.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class PostsController(IPostService postService) : ControllerBase
{
    private Guid NodeAccountId => Guid.Parse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? throw new UnauthorizedAccessException("Missing node account claim."));

    private Guid GlobalAccountId => Guid.Parse(
        User.FindFirst("global_id")?.Value
        ?? throw new UnauthorizedAccessException("Missing global_id claim."));

    // ─── Posts ────────────────────────────────────────────────────────────────

    /// <summary>Create a post. Supports replies (set ParentPostId) and content warnings.</summary>
    [HttpPost("posts")]
    public async Task<ActionResult<PostDto>> CreatePost([FromBody] CreatePostRequest req)
    {
        var post = await postService.CreatePostAsync(NodeAccountId, req);
        return CreatedAtAction(nameof(GetPost), new { postId = post.Id }, post);
    }

    /// <summary>Get a post by ID.</summary>
    [HttpGet("posts/{postId}")]
    [AllowAnonymous]
    public async Task<ActionResult<PostDto>> GetPost(Guid postId)
    {
        Guid? viewerId = User.Identity?.IsAuthenticated == true ? NodeAccountId : null;
        var post = await postService.GetPostAsync(postId, viewerId);
        return Ok(post);
    }

    /// <summary>Edit a post (author only). Records an EditedAt timestamp.</summary>
    [HttpPatch("posts/{postId}")]
    public async Task<ActionResult<PostDto>> EditPost(Guid postId, [FromBody] EditPostRequest req)
    {
        var post = await postService.EditPostAsync(postId, NodeAccountId, req);
        return Ok(post);
    }

    /// <summary>Delete a post (author or node admin).</summary>
    [HttpDelete("posts/{postId}")]
    public async Task<IActionResult> DeletePost(Guid postId)
    {
        await postService.DeletePostAsync(postId, NodeAccountId);
        return NoContent();
    }

    /// <summary>Get replies to a post.</summary>
    [HttpGet("posts/{postId}/replies")]
    [AllowAnonymous]
    public async Task<ActionResult<FeedResponse>> GetReplies(
        Guid postId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        Guid? viewerId = User.Identity?.IsAuthenticated == true ? NodeAccountId : null;
        var replies = await postService.GetRepliesAsync(postId, page, pageSize, viewerId);
        return Ok(replies);
    }

    // ─── Likes ────────────────────────────────────────────────────────────────

    /// <summary>Like a post.</summary>
    [HttpPost("posts/{postId}/like")]
    public async Task<ActionResult<PostDto>> LikePost(Guid postId)
    {
        var post = await postService.LikePostAsync(postId, NodeAccountId);
        return Ok(post);
    }

    /// <summary>Unlike a post.</summary>
    [HttpDelete("posts/{postId}/like")]
    public async Task<ActionResult<PostDto>> UnlikePost(Guid postId)
    {
        var post = await postService.UnlikePostAsync(postId, NodeAccountId);
        return Ok(post);
    }

    // ─── Reflys (Reposts) ─────────────────────────────────────────────────────

    /// <summary>Refly (repost) a post.</summary>
    [HttpPost("posts/{postId}/refly")]
    public async Task<ActionResult<PostDto>> ReflyPost(Guid postId)
    {
        var post = await postService.ReflyPostAsync(postId, NodeAccountId);
        return Ok(post);
    }

    /// <summary>Un-refly a post.</summary>
    [HttpDelete("posts/{postId}/refly")]
    public async Task<ActionResult<PostDto>> UnreflyPost(Guid postId)
    {
        var post = await postService.UnreflyPostAsync(postId, NodeAccountId);
        return Ok(post);
    }

    // ─── Bookmarks ────────────────────────────────────────────────────────────

    /// <summary>Bookmark a post.</summary>
    [HttpPost("posts/{postId}/bookmark")]
    public async Task<ActionResult<BookmarkResponse>> BookmarkPost(Guid postId)
    {
        var result = await postService.BookmarkPostAsync(postId, NodeAccountId);
        return Ok(result);
    }

    /// <summary>Remove bookmark.</summary>
    [HttpDelete("posts/{postId}/bookmark")]
    public async Task<ActionResult<BookmarkResponse>> UnbookmarkPost(Guid postId)
    {
        var result = await postService.UnbookmarkPostAsync(postId, NodeAccountId);
        return Ok(result);
    }

    /// <summary>Get bookmarked posts.</summary>
    [HttpGet("bookmarks")]
    public async Task<ActionResult<FeedResponse>> GetBookmarks(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var feed = await postService.GetBookmarksAsync(NodeAccountId, page, pageSize);
        return Ok(feed);
    }

    // ─── Pin ──────────────────────────────────────────────────────────────────

    /// <summary>Pin a post to your profile.</summary>
    [HttpPost("posts/{postId}/pin")]
    public async Task<IActionResult> PinPost(Guid postId)
    {
        await postService.PinPostAsync(postId, NodeAccountId);
        return NoContent();
    }

    /// <summary>Unpin your pinned post.</summary>
    [HttpDelete("posts/pin")]
    public async Task<IActionResult> UnpinPost()
    {
        await postService.UnpinPostAsync(NodeAccountId);
        return NoContent();
    }

    // ─── Pro ──────────────────────────────────────────────────────────────────

    /// <summary>Backup a post to Cloudlight storage (Pro users only).</summary>
    [HttpPost("posts/{postId}/backup")]
    public async Task<IActionResult> BackupPost(Guid postId)
    {
        await postService.BackupPostForProUserAsync(postId, GlobalAccountId);
        return NoContent();
    }

    // ─── Feeds ────────────────────────────────────────────────────────────────

    /// <summary>Node-wide chronological feed.</summary>
    [HttpGet("nodes/{nodeServerId}/feed")]
    [AllowAnonymous]
    public async Task<ActionResult<FeedResponse>> GetNodeFeed(
        Guid nodeServerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        Guid? viewerId = User.Identity?.IsAuthenticated == true ? NodeAccountId : null;
        var feed = await postService.GetNodeFeedAsync(nodeServerId, page, pageSize, viewerId);
        return Ok(feed);
    }

    /// <summary>Personal feed: posts from accounts you follow.</summary>
    [HttpGet("feed")]
    public async Task<ActionResult<FeedResponse>> GetUserFeed(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var feed = await postService.GetUserFeedAsync(NodeAccountId, page, pageSize);
        return Ok(feed);
    }

    /// <summary>Trending posts on this node (last 48h, sorted by engagement).</summary>
    [HttpGet("nodes/{nodeServerId}/trending")]
    [AllowAnonymous]
    public async Task<ActionResult<FeedResponse>> GetTrending(
        Guid nodeServerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        Guid? viewerId = User.Identity?.IsAuthenticated == true ? NodeAccountId : null;
        var feed = await postService.GetTrendingAsync(nodeServerId, page, pageSize, viewerId);
        return Ok(feed);
    }
}
