using System.Text.RegularExpressions;
using Btfly.API.Data;
using Btfly.API.DTOs;
using Btfly.API.Models.Entities;
using Btfly.API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Btfly.API.Services;

public interface IPostService
{
    Task<PostDto> CreatePostAsync(Guid nodeAccountId, CreatePostRequest req);
    Task<PostDto> EditPostAsync(Guid postId, Guid nodeAccountId, EditPostRequest req);
    Task<PostDto> GetPostAsync(Guid postId, Guid? viewerNodeAccountId = null);
    Task<FeedResponse> GetNodeFeedAsync(Guid nodeServerId, int page, int pageSize, Guid? viewerNodeAccountId = null);
    Task<FeedResponse> GetUserFeedAsync(Guid nodeAccountId, int page, int pageSize);
    Task<FeedResponse> GetRepliesAsync(Guid postId, int page, int pageSize, Guid? viewerNodeAccountId = null);
    Task<FeedResponse> GetTrendingAsync(Guid nodeServerId, int page, int pageSize, Guid? viewerNodeAccountId = null);
    Task<FeedResponse> GetBookmarksAsync(Guid nodeAccountId, int page, int pageSize);
    Task<FeedResponse> GetAccountPostsAsync(Guid profileNodeAccountId, int page, int pageSize, Guid? viewerNodeAccountId = null);
    Task DeletePostAsync(Guid postId, Guid requestingNodeAccountId);
    Task<PostDto> LikePostAsync(Guid postId, Guid nodeAccountId);
    Task<PostDto> UnlikePostAsync(Guid postId, Guid nodeAccountId);
    Task<PostDto> ReflyPostAsync(Guid postId, Guid nodeAccountId);
    Task<PostDto> UnreflyPostAsync(Guid postId, Guid nodeAccountId);
    Task<BookmarkResponse> BookmarkPostAsync(Guid postId, Guid nodeAccountId);
    Task<BookmarkResponse> UnbookmarkPostAsync(Guid postId, Guid nodeAccountId);
    Task<bool> PinPostAsync(Guid postId, Guid nodeAccountId);
    Task<bool> UnpinPostAsync(Guid nodeAccountId);
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

        if (req.Content.Length > (author.NodeServer?.MaxPostLength ?? 500))
            throw new ArgumentException($"Post exceeds the maximum length of {author.NodeServer?.MaxPostLength ?? 500} characters.");

        var post = new Post
        {
            AuthorId     = nodeAccountId,
            NodeServerId = author.NodeServerId,
            Content      = req.Content.Trim(),
            MediaUrl     = req.MediaUrl,
            ContentWarning = req.ContentWarning?.Trim(),
            ParentPostId = req.ParentPostId,
        };

        db.Posts.Add(post);

        // Auto-backup for Pro users
        if (author.CloudlightAccount.IsPro)
            db.PostBackups.Add(new PostBackup
            {
                PostId = post.Id,
                CloudlightAccountId = author.CloudlightAccountId,
                ContentSnapshot = post.Content
            });

        await db.SaveChangesAsync();

        // Fire notifications
        await FirePostNotificationsAsync(post, author);

        return await GetPostAsync(post.Id, nodeAccountId);
    }

    public async Task<PostDto> EditPostAsync(Guid postId, Guid nodeAccountId, EditPostRequest req)
    {
        var post = await db.Posts.FindAsync(postId)
            ?? throw new KeyNotFoundException("Post not found.");

        if (post.AuthorId != nodeAccountId)
            throw new UnauthorizedAccessException("You can only edit your own posts.");

        if (post.IsDeleted)
            throw new InvalidOperationException("Cannot edit a deleted post.");

        post.Content        = req.Content.Trim();
        post.ContentWarning = req.ContentWarning?.Trim();
        post.EditedAt       = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return await GetPostAsync(postId, nodeAccountId);
    }

    public async Task<PostDto> GetPostAsync(Guid postId, Guid? viewerNodeAccountId = null)
    {
        var post = await LoadPostQuery(db.Posts)
            .FirstOrDefaultAsync(p => p.Id == postId && !p.IsDeleted)
            ?? throw new KeyNotFoundException("Post not found.");

        return await MapPostAsync(post, viewerNodeAccountId);
    }

    public async Task<FeedResponse> GetNodeFeedAsync(Guid nodeServerId, int page, int pageSize, Guid? viewerNodeAccountId = null)
    {
        var node = await db.NodeServers.FindAsync(nodeServerId)
            ?? throw new KeyNotFoundException("Node not found.");

        var query = LoadPostQuery(db.Posts)
            .Where(p => !p.IsDeleted && p.ParentPostId == null);

        query = node.ServerType == ServerType.Light
            ? query.Where(p => p.NodeServerId == nodeServerId || p.IsReplicated)
            : query.Where(p => p.NodeServerId == nodeServerId);

        var total = await query.CountAsync();
        var posts = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = await Task.WhenAll(posts.Select(p => MapPostAsync(p, viewerNodeAccountId)));
        return new FeedResponse(dtos, total, page, pageSize);
    }

    public async Task<FeedResponse> GetUserFeedAsync(Guid nodeAccountId, int page, int pageSize)
    {
        var followingIds = await db.Follows
            .Where(f => f.FollowerId == nodeAccountId)
            .Select(f => f.FollowingId)
            .ToListAsync();

        followingIds.Add(nodeAccountId);

        var query = LoadPostQuery(db.Posts)
            .Where(p => followingIds.Contains(p.AuthorId) && !p.IsDeleted && p.ParentPostId == null);

        var total = await query.CountAsync();
        var posts = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = await Task.WhenAll(posts.Select(p => MapPostAsync(p, nodeAccountId)));
        return new FeedResponse(dtos, total, page, pageSize);
    }

    public async Task<FeedResponse> GetRepliesAsync(Guid postId, int page, int pageSize, Guid? viewerNodeAccountId = null)
    {
        var query = LoadPostQuery(db.Posts)
            .Where(p => p.ParentPostId == postId && !p.IsDeleted);

        var total = await query.CountAsync();
        var posts = await query
            .OrderBy(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = await Task.WhenAll(posts.Select(p => MapPostAsync(p, viewerNodeAccountId)));
        return new FeedResponse(dtos, total, page, pageSize);
    }

    public async Task<FeedResponse> GetTrendingAsync(Guid nodeServerId, int page, int pageSize, Guid? viewerNodeAccountId = null)
    {
        // Trending = most likes + reflys + replies in last 48h
        var since = DateTime.UtcNow.AddHours(-48);

        var query = LoadPostQuery(db.Posts)
            .Where(p => p.NodeServerId == nodeServerId && !p.IsDeleted &&
                        p.ParentPostId == null && p.CreatedAt >= since);

        var total = await query.CountAsync();
        var posts = await query
            .OrderByDescending(p => p.Likes.Count + p.Reflys.Count + p.Replies.Count)
            .ThenByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = await Task.WhenAll(posts.Select(p => MapPostAsync(p, viewerNodeAccountId)));
        return new FeedResponse(dtos, total, page, pageSize);
    }

    public async Task<FeedResponse> GetBookmarksAsync(Guid nodeAccountId, int page, int pageSize)
    {
        var query = LoadPostQuery(db.Posts)
            .Where(p => p.Bookmarks.Any(b => b.NodeAccountId == nodeAccountId) && !p.IsDeleted);

        var total = await query.CountAsync();
        var posts = await query
            .OrderByDescending(p => p.Bookmarks.First(b => b.NodeAccountId == nodeAccountId).CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = await Task.WhenAll(posts.Select(p => MapPostAsync(p, nodeAccountId)));
        return new FeedResponse(dtos, total, page, pageSize);
    }

    public async Task<FeedResponse> GetAccountPostsAsync(Guid profileNodeAccountId, int page, int pageSize, Guid? viewerNodeAccountId = null)
    {
        var query = LoadPostQuery(db.Posts)
            .Where(p => p.AuthorId == profileNodeAccountId && !p.IsDeleted && p.ParentPostId == null);

        var total = await query.CountAsync();
        var posts = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = await Task.WhenAll(posts.Select(p => MapPostAsync(p, viewerNodeAccountId)));
        return new FeedResponse(dtos, total, page, pageSize);
    }

    public async Task DeletePostAsync(Guid postId, Guid requestingNodeAccountId)
    {
        var post = await db.Posts.FindAsync(postId)
            ?? throw new KeyNotFoundException("Post not found.");

        var requester = await db.NodeAccounts
            .Include(a => a.CloudlightAccount)
            .FirstAsync(a => a.Id == requestingNodeAccountId);

        var isAuthor = post.AuthorId == requestingNodeAccountId;
        var isAdmin  = requester.IsNodeAdmin || requester.CloudlightAccount.Role == AccountRole.PlatformAdmin;

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

        // Notify post author
        var post = await db.Posts.Include(p => p.Author).FirstAsync(p => p.Id == postId);
        if (post.AuthorId != nodeAccountId)
            await AddNotificationAsync(post.AuthorId, nodeAccountId, NotificationType.Like, postId);

        await db.SaveChangesAsync();
        return await GetPostAsync(postId, nodeAccountId);
    }

    public async Task<PostDto> UnlikePostAsync(Guid postId, Guid nodeAccountId)
    {
        var like = await db.PostLikes.FirstOrDefaultAsync(l => l.PostId == postId && l.NodeAccountId == nodeAccountId)
            ?? throw new InvalidOperationException("Not liked.");
        db.PostLikes.Remove(like);
        await db.SaveChangesAsync();
        return await GetPostAsync(postId, nodeAccountId);
    }

    public async Task<PostDto> ReflyPostAsync(Guid postId, Guid nodeAccountId)
    {
        var original = await db.Posts
            .Include(p => p.Author).ThenInclude(a => a.NodeServer)
            .FirstOrDefaultAsync(p => p.Id == postId && !p.IsDeleted)
            ?? throw new KeyNotFoundException("Post not found.");

        if (await db.Posts.AnyAsync(p => p.ReflyOfPostId == postId && p.AuthorId == nodeAccountId && !p.IsDeleted))
            throw new InvalidOperationException("Already reflyed.");

        var author = await db.NodeAccounts.Include(a => a.NodeServer).FirstAsync(a => a.Id == nodeAccountId);

        var refly = new Post
        {
            AuthorId      = nodeAccountId,
            NodeServerId  = author.NodeServerId,
            Content       = string.Empty,
            ReflyOfPostId = postId,
        };
        db.Posts.Add(refly);

        if (original.AuthorId != nodeAccountId)
            await AddNotificationAsync(original.AuthorId, nodeAccountId, NotificationType.Refly, postId);

        await db.SaveChangesAsync();
        return await GetPostAsync(original.Id, nodeAccountId);
    }

    public async Task<PostDto> UnreflyPostAsync(Guid postId, Guid nodeAccountId)
    {
        var refly = await db.Posts
            .FirstOrDefaultAsync(p => p.ReflyOfPostId == postId && p.AuthorId == nodeAccountId && !p.IsDeleted)
            ?? throw new KeyNotFoundException("Refly not found.");
        refly.IsDeleted = true;
        await db.SaveChangesAsync();
        return await GetPostAsync(postId, nodeAccountId);
    }

    public async Task<BookmarkResponse> BookmarkPostAsync(Guid postId, Guid nodeAccountId)
    {
        if (!await db.Bookmarks.AnyAsync(b => b.PostId == postId && b.NodeAccountId == nodeAccountId))
            db.Bookmarks.Add(new Bookmark { PostId = postId, NodeAccountId = nodeAccountId });
        await db.SaveChangesAsync();
        return new BookmarkResponse(true);
    }

    public async Task<BookmarkResponse> UnbookmarkPostAsync(Guid postId, Guid nodeAccountId)
    {
        var bm = await db.Bookmarks.FirstOrDefaultAsync(b => b.PostId == postId && b.NodeAccountId == nodeAccountId);
        if (bm != null) db.Bookmarks.Remove(bm);
        await db.SaveChangesAsync();
        return new BookmarkResponse(false);
    }

    public async Task<bool> PinPostAsync(Guid postId, Guid nodeAccountId)
    {
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId && p.AuthorId == nodeAccountId && !p.IsDeleted)
            ?? throw new KeyNotFoundException("Post not found or not yours.");
        var account = await db.NodeAccounts.FindAsync(nodeAccountId)!;
        account!.PinnedPostId = postId;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnpinPostAsync(Guid nodeAccountId)
    {
        var account = await db.NodeAccounts.FindAsync(nodeAccountId)!;
        account!.PinnedPostId = null;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task BackupPostForProUserAsync(Guid postId, Guid cloudlightAccountId)
    {
        var account = await db.CloudlightAccounts.FindAsync(cloudlightAccountId)
            ?? throw new KeyNotFoundException("Account not found.");
        if (!account.IsPro)
            throw new UnauthorizedAccessException("Post backup is a Pro feature.");
        var post = await db.Posts.FindAsync(postId)
            ?? throw new KeyNotFoundException("Post not found.");
        if (!await db.PostBackups.AnyAsync(b => b.PostId == postId && b.CloudlightAccountId == cloudlightAccountId))
            db.PostBackups.Add(new PostBackup
            {
                PostId = postId,
                CloudlightAccountId = cloudlightAccountId,
                ContentSnapshot = post.Content
            });
        await db.SaveChangesAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task FirePostNotificationsAsync(Post post, NodeAccount author)
    {
        // Notify parent post author of reply
        if (post.ParentPostId.HasValue)
        {
            var parent = await db.Posts.FindAsync(post.ParentPostId.Value);
            if (parent != null && parent.AuthorId != author.Id)
                await AddNotificationAsync(parent.AuthorId, author.Id, NotificationType.Reply, post.Id);
        }

        // Notify @mentioned users
        var mentions = Regex.Matches(post.Content, @"@([a-zA-Z0-9_]+)")
            .Select(m => m.Groups[1].Value.ToLower())
            .Distinct();

        foreach (var username in mentions)
        {
            var mentioned = await db.NodeAccounts
                .FirstOrDefaultAsync(a => a.NodeServerId == author.NodeServerId &&
                                          a.Username.ToLower() == username);
            if (mentioned != null && mentioned.Id != author.Id)
                await AddNotificationAsync(mentioned.Id, author.Id, NotificationType.Mention, post.Id);
        }
    }

    private async Task AddNotificationAsync(Guid recipientId, Guid actorId, NotificationType type, Guid? postId)
    {
        // Avoid duplicate notifications of same type from same actor on same post
        var exists = await db.Notifications.AnyAsync(n =>
            n.RecipientId == recipientId &&
            n.ActorId     == actorId &&
            n.Type        == type &&
            n.PostId      == postId &&
            n.CreatedAt   > DateTime.UtcNow.AddHours(-1));

        if (!exists)
            db.Notifications.Add(new Notification
            {
                RecipientId = recipientId,
                ActorId     = actorId,
                Type        = type,
                PostId      = postId,
            });
    }

    private static IQueryable<Post> LoadPostQuery(IQueryable<Post> q) => q
        .Include(p => p.Author).ThenInclude(a => a.NodeServer)
        .Include(p => p.Author).ThenInclude(a => a.CloudlightAccount)
        .Include(p => p.Author).ThenInclude(a => a.Followers)
        .Include(p => p.Author).ThenInclude(a => a.Following)
        .Include(p => p.Likes)
        .Include(p => p.Replies)
        .Include(p => p.Reflys)
        .Include(p => p.Bookmarks)
        .Include(p => p.ReflyOfPost).ThenInclude(r => r!.Author).ThenInclude(a => a.NodeServer)
        .Include(p => p.ReflyOfPost).ThenInclude(r => r!.Author).ThenInclude(a => a.CloudlightAccount)
        .Include(p => p.ReflyOfPost).ThenInclude(r => r!.Likes)
        .Include(p => p.ReflyOfPost).ThenInclude(r => r!.Replies)
        .Include(p => p.ReflyOfPost).ThenInclude(r => r!.Reflys);

    private async Task<PostDto> MapPostAsync(Post p, Guid? viewerNodeAccountId)
    {
        int postCount = 0;
        if (p.Author != null)
            postCount = await db.Posts.CountAsync(x => x.AuthorId == p.AuthorId && !x.IsDeleted);

        var authorDto = new NodeAccountDto(
            p.Author.Id, p.Author.Username, p.Author.Bio, p.Author.AvatarUrl,
            p.Author.HeaderUrl, p.Author.WebsiteUrl, p.Author.Location,
            p.Author.NodeServer?.Domain ?? string.Empty,
            p.Author.CloudlightAccount?.IsPro ?? false,
            p.Author.IsNodeAdmin,
            p.Author.Followers?.Count ?? 0,
            p.Author.Following?.Count ?? 0,
            postCount,
            p.Author.RequiresUsernameSetup,
            p.Author.JoinedAt);

        PostDto? reflyOfDto = null;
        if (p.ReflyOfPost != null && !p.ReflyOfPost.IsDeleted)
        {
            var reflyAuthorDto = new NodeAccountDto(
                p.ReflyOfPost.Author.Id, p.ReflyOfPost.Author.Username,
                p.ReflyOfPost.Author.Bio, p.ReflyOfPost.Author.AvatarUrl,
                null, null, null,
                p.ReflyOfPost.Author.NodeServer?.Domain ?? string.Empty,
                p.ReflyOfPost.Author.CloudlightAccount?.IsPro ?? false,
                p.ReflyOfPost.Author.IsNodeAdmin,
                0, 0, 0, false, p.ReflyOfPost.Author.JoinedAt);

            reflyOfDto = new PostDto(
                p.ReflyOfPost.Id, p.ReflyOfPost.Content, p.ReflyOfPost.MediaUrl,
                p.ReflyOfPost.ContentWarning, reflyAuthorDto, p.ReflyOfPost.ParentPostId,
                null,
                p.ReflyOfPost.Likes?.Count ?? 0,
                p.ReflyOfPost.Replies?.Count ?? 0,
                p.ReflyOfPost.Reflys?.Count ?? 0,
                viewerNodeAccountId.HasValue && (p.ReflyOfPost.Likes?.Any(l => l.NodeAccountId == viewerNodeAccountId) ?? false),
                false, false,
                p.ReflyOfPost.IsReplicated,
                p.ReflyOfPost.CreatedAt, p.ReflyOfPost.EditedAt);
        }

        return new PostDto(
            p.Id, p.Content, p.MediaUrl, p.ContentWarning, authorDto, p.ParentPostId,
            reflyOfDto,
            p.Likes?.Count ?? 0,
            p.Replies?.Count ?? 0,
            p.Reflys?.Count ?? 0,
            viewerNodeAccountId.HasValue && (p.Likes?.Any(l => l.NodeAccountId == viewerNodeAccountId) ?? false),
            viewerNodeAccountId.HasValue && (p.Bookmarks?.Any(b => b.NodeAccountId == viewerNodeAccountId) ?? false),
            viewerNodeAccountId.HasValue && await db.Posts.AnyAsync(r =>
                r.ReflyOfPostId == p.Id && r.AuthorId == viewerNodeAccountId && !r.IsDeleted),
            p.IsReplicated,
            p.CreatedAt, p.EditedAt);
    }
}
