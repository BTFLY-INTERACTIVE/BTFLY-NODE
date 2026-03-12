using Btfly.API.Data;
using Btfly.API.DTOs;
using Btfly.API.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Btfly.API.Services;

public interface IAccountService
{
    Task<NodeAccountDto> GetProfileAsync(string username, Guid nodeServerId, Guid? viewerNodeAccountId = null);
    Task<NodeAccountDto> UpdateProfileAsync(Guid nodeAccountId, UpdateProfileRequest req);
    Task<FollowResponse> FollowAsync(Guid followerNodeAccountId, Guid targetNodeAccountId);
    Task<FollowResponse> UnfollowAsync(Guid followerNodeAccountId, Guid targetNodeAccountId);
    Task<IEnumerable<NodeAccountDto>> GetFollowersAsync(Guid nodeAccountId);
    Task<IEnumerable<NodeAccountDto>> GetFollowingAsync(Guid nodeAccountId);
    Task<SearchResponse> SearchAsync(string query, Guid nodeServerId, Guid? viewerNodeAccountId = null);
}

public class AccountService(BtflyDbContext db) : IAccountService
{
    public async Task<NodeAccountDto> GetProfileAsync(string username, Guid nodeServerId, Guid? viewerNodeAccountId = null)
    {
        var account = await db.NodeAccounts
            .Include(a => a.NodeServer)
            .Include(a => a.CloudlightAccount)
            .Include(a => a.Followers)
            .Include(a => a.Following)
            .FirstOrDefaultAsync(a =>
                a.NodeServerId == nodeServerId &&
                a.Username.ToLower() == username.ToLower())
            ?? throw new KeyNotFoundException($"User '{username}' not found.");

        var postCount = await db.Posts.CountAsync(p => p.AuthorId == account.Id && !p.IsDeleted);
        return MapAccount(account, postCount);
    }

    public async Task<NodeAccountDto> UpdateProfileAsync(Guid nodeAccountId, UpdateProfileRequest req)
    {
        var account = await db.NodeAccounts
            .Include(a => a.NodeServer)
            .Include(a => a.CloudlightAccount)
            .Include(a => a.Followers)
            .Include(a => a.Following)
            .FirstOrDefaultAsync(a => a.Id == nodeAccountId)
            ?? throw new KeyNotFoundException("Account not found.");

        if (req.Bio        != null) account.Bio        = req.Bio.Trim();
        if (req.AvatarUrl  != null) account.AvatarUrl  = req.AvatarUrl;
        if (req.HeaderUrl  != null) account.HeaderUrl  = req.HeaderUrl;
        if (req.WebsiteUrl != null) account.WebsiteUrl = req.WebsiteUrl;
        if (req.Location   != null) account.Location   = req.Location.Trim();

        await db.SaveChangesAsync();

        var postCount = await db.Posts.CountAsync(p => p.AuthorId == account.Id && !p.IsDeleted);
        return MapAccount(account, postCount);
    }

    public async Task<FollowResponse> FollowAsync(Guid followerNodeAccountId, Guid targetNodeAccountId)
    {
        if (followerNodeAccountId == targetNodeAccountId)
            throw new InvalidOperationException("Cannot follow yourself.");

        if (!await db.Follows.AnyAsync(f =>
            f.FollowerId == followerNodeAccountId && f.FollowingId == targetNodeAccountId))
        {
            db.Follows.Add(new Follow
            {
                FollowerId  = followerNodeAccountId,
                FollowingId = targetNodeAccountId,
            });

            // Notify target
            db.Notifications.Add(new Notification
            {
                RecipientId = targetNodeAccountId,
                ActorId     = followerNodeAccountId,
                Type        = Models.Enums.NotificationType.Follow,
            });

            await db.SaveChangesAsync();
        }

        var followerCount = await db.Follows.CountAsync(f => f.FollowingId == targetNodeAccountId);
        return new FollowResponse(true, followerCount);
    }

    public async Task<FollowResponse> UnfollowAsync(Guid followerNodeAccountId, Guid targetNodeAccountId)
    {
        var follow = await db.Follows.FirstOrDefaultAsync(f =>
            f.FollowerId == followerNodeAccountId && f.FollowingId == targetNodeAccountId);

        if (follow != null)
        {
            db.Follows.Remove(follow);
            await db.SaveChangesAsync();
        }

        var followerCount = await db.Follows.CountAsync(f => f.FollowingId == targetNodeAccountId);
        return new FollowResponse(false, followerCount);
    }

    public async Task<IEnumerable<NodeAccountDto>> GetFollowersAsync(Guid nodeAccountId)
    {
        var followers = await db.Follows
            .Include(f => f.Follower).ThenInclude(a => a.NodeServer)
            .Include(f => f.Follower).ThenInclude(a => a.CloudlightAccount)
            .Include(f => f.Follower).ThenInclude(a => a.Followers)
            .Include(f => f.Follower).ThenInclude(a => a.Following)
            .Where(f => f.FollowingId == nodeAccountId)
            .Select(f => f.Follower)
            .ToListAsync();

        return followers.Select(a => MapAccount(a, 0));
    }

    public async Task<IEnumerable<NodeAccountDto>> GetFollowingAsync(Guid nodeAccountId)
    {
        var following = await db.Follows
            .Include(f => f.Following).ThenInclude(a => a.NodeServer)
            .Include(f => f.Following).ThenInclude(a => a.CloudlightAccount)
            .Include(f => f.Following).ThenInclude(a => a.Followers)
            .Include(f => f.Following).ThenInclude(a => a.Following)
            .Where(f => f.FollowerId == nodeAccountId)
            .Select(f => f.Following)
            .ToListAsync();

        return following.Select(a => MapAccount(a, 0));
    }

    public async Task<SearchResponse> SearchAsync(string query, Guid nodeServerId, Guid? viewerNodeAccountId = null)
    {
        var q = query.Trim().ToLower();
        if (string.IsNullOrWhiteSpace(q))
            return new SearchResponse([], [], 0, 0);

        // Search accounts by username or bio
        var accounts = await db.NodeAccounts
            .Include(a => a.NodeServer)
            .Include(a => a.CloudlightAccount)
            .Include(a => a.Followers)
            .Include(a => a.Following)
            .Where(a =>
                a.NodeServerId == nodeServerId &&
                (a.Username.ToLower().Contains(q) || (a.Bio != null && a.Bio.ToLower().Contains(q))))
            .Take(20)
            .ToListAsync();

        // Search posts by content
        var posts = await db.Posts
            .Include(p => p.Author).ThenInclude(a => a.NodeServer)
            .Include(p => p.Author).ThenInclude(a => a.CloudlightAccount)
            .Include(p => p.Author).ThenInclude(a => a.Followers)
            .Include(p => p.Author).ThenInclude(a => a.Following)
            .Include(p => p.Likes)
            .Include(p => p.Replies)
            .Include(p => p.Reflys)
            .Include(p => p.Bookmarks)
            .Where(p =>
                p.NodeServerId == nodeServerId &&
                !p.IsDeleted &&
                p.Content.ToLower().Contains(q))
            .OrderByDescending(p => p.CreatedAt)
            .Take(20)
            .ToListAsync();

        var accountDtos = accounts.Select(a => MapAccount(a, 0));
        var postDtos = posts.Select(p => new PostDto(
            p.Id, p.Content, p.MediaUrl, p.ContentWarning,
            MapAccount(p.Author, 0),
            p.ParentPostId, null,
            p.Likes?.Count ?? 0, p.Replies?.Count ?? 0, p.Reflys?.Count ?? 0,
            viewerNodeAccountId.HasValue && (p.Likes?.Any(l => l.NodeAccountId == viewerNodeAccountId) ?? false),
            viewerNodeAccountId.HasValue && (p.Bookmarks?.Any(b => b.NodeAccountId == viewerNodeAccountId) ?? false),
            false,
            p.IsReplicated, p.CreatedAt, p.EditedAt));

        return new SearchResponse(postDtos, accountDtos, posts.Count, accounts.Count);
    }

    internal static NodeAccountDto MapAccount(NodeAccount a, int postCount) => new(
        a.Id, a.Username, a.Bio, a.AvatarUrl, a.HeaderUrl, a.WebsiteUrl, a.Location,
        a.NodeServer?.Domain ?? string.Empty,
        a.CloudlightAccount?.IsPro ?? false,
        a.IsNodeAdmin,
        a.Followers?.Count ?? 0,
        a.Following?.Count ?? 0,
        postCount,
        a.RequiresUsernameSetup,
        a.JoinedAt);
}
