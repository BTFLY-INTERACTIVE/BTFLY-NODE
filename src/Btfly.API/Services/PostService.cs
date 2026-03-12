using Btfly.API.Data;
using Btfly.API.DTOs;
using Btfly.API.Models.Entities;
using Btfly.API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Btfly.API.Services;

public interface IPostService
{
    Task<PostDto> CreatePostAsync(Guid nodeAccountId, CreatePostRequest req);
    Task<PostDto> GetPostAsync(Guid postId);
    Task<FeedResponse> GetNodeFeedAsync(Guid nodeServerId, int page, int pageSize);
    Task<FeedResponse> GetUserFeedAsync(Guid nodeAccountId, int page, int pageSize);
    Task<FeedResponse> GetRepliesAsync(Guid postId, int page, int pageSize);
    Task DeletePostAsync(Guid postId, Guid requestingNodeAccountId);
    Task<PostDto> LikePostAsync(Guid postId, Guid nodeAccountId);
    Task<PostDto> UnlikePostAsync(Guid postId, Guid nodeAccountId);
    Task BackupPostForProUserAsync(Guid postId, Guid cloudlightAccountId);
}

public class PostService(BtflyDbContext db) : IPostService
{
    public async Task<PostDto> CreatePostAsync(Guid nodeAccountId, CreatePostRequest req)
    {
        var author = await db.NodeAccounts
            .Include(a => a.NodeServer)
            .Include(a => a.CloudlightAccount)
            .FirstOrDefaultAsync(a => a.Id == nodeAccountId)
            ?? throw new KeyNotFoundException("Node account not found.");

        if (author.IsNodeBanned || author.CloudlightAccount.IsGloballyBanned)
            throw new UnauthorizedAccessException("Account is banned and cannot post.");

        // Dark servers only allow posts to their own members
        // (federation rules are enforced at the federation layer; creation is always local)

        var post = new Post
        {
            AuthorId = nodeAccountId,
            NodeServerId = author.NodeServerId,
            Content = req.Content.Trim(),
            MediaUrl = req.MediaUrl,
            ParentPostId = req.ParentPostId
        };

        db.Posts.Add(post);

        // Auto-backup for Pro users on any server type
        if (author.CloudlightAccount.IsPro)
        {
            db.PostBackups.Add(new PostBackup
            {
                PostId = post.Id,
                CloudlightAccountId = author.CloudlightAccountId,
                ContentSnapshot = post.Content
            });
        }

        await db.SaveChangesAsync();
        return await GetPostAsync(post.Id);
    }

    public async Task<PostDto> GetPostAsync(Guid postId)
    {
        var post = await db.Posts
            .Include(p => p.Author).ThenInclude(a => a.NodeServer)
            .Include(p => p.Author).ThenInclude(a => a.CloudlightAccount)
            .Include(p => p.Author).ThenInclude(a => a.Followers)
            .Include(p => p.Author).ThenInclude(a => a.Following)
            .Include(p => p.Likes)
            .Include(p => p.Replies)
            .FirstOrDefaultAsync(p => p.Id == postId && !p.IsDeleted)
            ?? throw new KeyNotFoundException("Post not found.");

        return MapPost(post);
    }

    public async Task<FeedResponse> GetNodeFeedAsync(Guid nodeServerId, int page, int pageSize)
    {
        var node = await db.NodeServers.FindAsync(nodeServerId)
            ?? throw new KeyNotFoundException("Node not found.");

        var query = db.Posts
            .Include(p => p.Author).ThenInclude(a => a.NodeServer)
            .Include(p => p.Author).ThenInclude(a => a.CloudlightAccount)
            .Include(p => p.Author).ThenInclude(a => a.Followers)
            .Include(p => p.Author).ThenInclude(a => a.Following)
            .Include(p => p.Likes)
            .Include(p => p.Replies)
            .Where(p => !p.IsDeleted && p.ParentPostId == null);

        // Light servers show replicated content from other nodes too
        if (node.ServerType == ServerType.Light)
            query = query.Where(p => p.NodeServerId == nodeServerId || p.IsReplicated);
        else
            query = query.Where(p => p.NodeServerId == nodeServerId);

        var total = await query.CountAsync();
        var posts = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new FeedResponse(posts.Select(MapPost), total, page, pageSize);
    }

    public async Task<FeedResponse> GetUserFeedAsync(Guid nodeAccountId, int page, int pageSize)
    {
        // Personal feed: posts from accounts this user follows
        var followingIds = await db.Follows
            .Where(f => f.FollowerId == nodeAccountId)
            .Select(f => f.FollowingId)
            .ToListAsync();

        followingIds.Add(nodeAccountId); // Include own posts

        var query = db.Posts
            .Include(p => p.Author).ThenInclude(a => a.NodeServer)
            .Include(p => p.Author).ThenInclude(a => a.CloudlightAccount)
            .Include(p => p.Author).ThenInclude(a => a.Followers)
            .Include(p => p.Author).ThenInclude(a => a.Following)
            .Include(p => p.Likes)
            .Include(p => p.Replies)
            .Where(p => followingIds.Contains(p.AuthorId) && !p.IsDeleted && p.ParentPostId == null);

        var total = await query.CountAsync();
        var posts = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new FeedResponse(posts.Select(MapPost), total, page, pageSize);
    }

    public async Task<FeedResponse> GetRepliesAsync(Guid postId, int page, int pageSize)
    {
        var query = db.Posts
            .Include(p => p.Author).ThenInclude(a => a.NodeServer)
            .Include(p => p.Author).ThenInclude(a => a.CloudlightAccount)
            .Include(p => p.Author).ThenInclude(a => a.Followers)
            .Include(p => p.Author).ThenInclude(a => a.Following)
            .Include(p => p.Likes)
            .Include(p => p.Replies)
            .Where(p => p.ParentPostId == postId && !p.IsDeleted);

        var total = await query.CountAsync();
        var posts = await query
            .OrderBy(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new FeedResponse(posts.Select(MapPost), total, page, pageSize);
    }

    public async Task DeletePostAsync(Guid postId, Guid requestingNodeAccountId)
    {
        var post = await db.Posts.FindAsync(postId)
            ?? throw new KeyNotFoundException("Post not found.");

        var requester = await db.NodeAccounts
            .Include(a => a.CloudlightAccount)
            .FirstAsync(a => a.Id == requestingNodeAccountId);

        var isAuthor = post.AuthorId == requestingNodeAccountId;
        var isAdmin = requester.IsNodeAdmin || requester.CloudlightAccount.Role == Models.Enums.AccountRole.PlatformAdmin;

        if (!isAuthor && !isAdmin)
            throw new UnauthorizedAccessException("You do not have permission to delete this post.");

        post.IsDeleted = true;
        await db.SaveChangesAsync();
    }

    public async Task<PostDto> LikePostAsync(Guid postId, Guid nodeAccountId)
    {
        if (await db.PostLikes.AnyAsync(l => l.PostId == postId && l.NodeAccountId == nodeAccountId))
            throw new InvalidOperationException("Already liked.");

        db.PostLikes.Add(new PostLike { PostId = postId, NodeAccountId = nodeAccountId });
        await db.SaveChangesAsync();
        return await GetPostAsync(postId);
    }

    public async Task<PostDto> UnlikePostAsync(Guid postId, Guid nodeAccountId)
    {
        var like = await db.PostLikes.FirstOrDefaultAsync(l => l.PostId == postId && l.NodeAccountId == nodeAccountId)
            ?? throw new InvalidOperationException("Not liked.");

        db.PostLikes.Remove(like);
        await db.SaveChangesAsync();
        return await GetPostAsync(postId);
    }

    public async Task BackupPostForProUserAsync(Guid postId, Guid cloudlightAccountId)
    {
        var account = await db.CloudlightAccounts.FindAsync(cloudlightAccountId)
            ?? throw new KeyNotFoundException("Account not found.");

        if (!account.IsPro)
            throw new UnauthorizedAccessException("Post backup is a Pro feature.");

        var post = await db.Posts.FindAsync(postId)
            ?? throw new KeyNotFoundException("Post not found.");

        if (await db.PostBackups.AnyAsync(b => b.PostId == postId && b.CloudlightAccountId == cloudlightAccountId))
            return; // Already backed up

        db.PostBackups.Add(new PostBackup
        {
            PostId = postId,
            CloudlightAccountId = cloudlightAccountId,
            ContentSnapshot = post.Content
        });

        await db.SaveChangesAsync();
    }

    private static PostDto MapPost(Post p) => new(
        p.Id,
        p.Content,
        p.MediaUrl,
        new NodeAccountDto(
            p.Author.Id,
            p.Author.Username,
            p.Author.Bio,
            p.Author.AvatarUrl,
            p.Author.NodeServer?.Domain ?? string.Empty,
            p.Author.CloudlightAccount?.IsPro ?? false,
            p.Author.Followers?.Count ?? 0,
            p.Author.Following?.Count ?? 0,
            p.Author.JoinedAt),
        p.ParentPostId,
        p.Likes?.Count ?? 0,
        p.Replies?.Count ?? 0,
        p.IsReplicated,
        p.CreatedAt,
        p.EditedAt);
}
