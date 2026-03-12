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
    // JWT claim "sub" = node account ID (set in TokenService.GenerateNodeToken)
    private Guid NodeAccountId => Guid.Parse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? throw new UnauthorizedAccessException("Missing node account claim."));

    // JWT claim "global_id" = CloudlightAccount.Id
    private Guid GlobalAccountId => Guid.Parse(
        User.FindFirst("global_id")?.Value
        ?? throw new UnauthorizedAccessException("Missing global_id claim."));

    /// <summary>Create a new post on the current node.</summary>
    [HttpPost("posts")]
    public async Task<ActionResult<PostDto>> CreatePost([FromBody] CreatePostRequest req)
    {
        var post = await postService.CreatePostAsync(NodeAccountId, req);
        return CreatedAtAction(nameof(GetPost), new { postId = post.Id }, post);
    }

    /// <summary>Get a specific post by ID.</summary>
    [HttpGet("posts/{postId}")]
    [AllowAnonymous]
    public async Task<ActionResult<PostDto>> GetPost(Guid postId)
    {
        var post = await postService.GetPostAsync(postId);
        return Ok(post);
    }

    /// <summary>Get replies to a post.</summary>
    [HttpGet("posts/{postId}/replies")]
    [AllowAnonymous]
    public async Task<ActionResult<FeedResponse>> GetReplies(
        Guid postId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var replies = await postService.GetRepliesAsync(postId, page, pageSize);
        return Ok(replies);
    }

    /// <summary>Delete a post (author or admin only).</summary>
    [HttpDelete("posts/{postId}")]
    public async Task<IActionResult> DeletePost(Guid postId)
    {
        await postService.DeletePostAsync(postId, NodeAccountId);
        return NoContent();
    }

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

    /// <summary>Backup a post to cloud storage (Pro users only).</summary>
    [HttpPost("posts/{postId}/backup")]
    public async Task<IActionResult> BackupPost(Guid postId)
    {
        await postService.BackupPostForProUserAsync(postId, GlobalAccountId);
        return NoContent();
    }

    // ─── Feeds ────────────────────────────────────────────────────────────────

    /// <summary>Node-wide feed. Light nodes include replicated posts from other nodes.</summary>
    [HttpGet("nodes/{nodeServerId}/feed")]
    [AllowAnonymous]
    public async Task<ActionResult<FeedResponse>> GetNodeFeed(
        Guid nodeServerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var feed = await postService.GetNodeFeedAsync(nodeServerId, page, pageSize);
        return Ok(feed);
    }

    /// <summary>Personal feed: posts from accounts the current user follows.</summary>
    [HttpGet("feed")]
    public async Task<ActionResult<FeedResponse>> GetUserFeed(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var feed = await postService.GetUserFeedAsync(NodeAccountId, page, pageSize);
        return Ok(feed);
    }
}
