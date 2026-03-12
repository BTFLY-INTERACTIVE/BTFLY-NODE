using Btfly.API.Data;
using Btfly.API.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Btfly.API.Services;

public interface INotificationService
{
    Task<NotificationsResponse> GetNotificationsAsync(Guid nodeAccountId, int page, int pageSize);
    Task MarkAllReadAsync(Guid nodeAccountId);
    Task MarkReadAsync(Guid notificationId, Guid nodeAccountId);
    Task<int> GetUnreadCountAsync(Guid nodeAccountId);
}

public class NotificationService(BtflyDbContext db) : INotificationService
{
    public async Task<NotificationsResponse> GetNotificationsAsync(Guid nodeAccountId, int page, int pageSize)
    {
        var query = db.Notifications
            .Include(n => n.Actor).ThenInclude(a => a!.NodeServer)
            .Include(n => n.Actor).ThenInclude(a => a!.CloudlightAccount)
            .Include(n => n.Actor).ThenInclude(a => a!.Followers)
            .Include(n => n.Actor).ThenInclude(a => a!.Following)
            .Include(n => n.Post).ThenInclude(p => p!.Author).ThenInclude(a => a.NodeServer)
            .Include(n => n.Post).ThenInclude(p => p!.Author).ThenInclude(a => a.CloudlightAccount)
            .Include(n => n.Post).ThenInclude(p => p!.Likes)
            .Include(n => n.Post).ThenInclude(p => p!.Replies)
            .Include(n => n.Post).ThenInclude(p => p!.Reflys)
            .Where(n => n.RecipientId == nodeAccountId);

        var total   = await query.CountAsync();
        var unread  = await query.CountAsync(n => !n.IsRead);
        var items   = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(n => new NotificationDto(
            n.Id, n.Type,
            n.Actor == null ? null : new NodeAccountDto(
                n.Actor.Id, n.Actor.Username, n.Actor.Bio, n.Actor.AvatarUrl,
                n.Actor.HeaderUrl, n.Actor.WebsiteUrl, n.Actor.Location,
                n.Actor.NodeServer?.Domain ?? string.Empty,
                n.Actor.CloudlightAccount?.IsPro ?? false,
                n.Actor.IsNodeAdmin,
                n.Actor.Followers?.Count ?? 0,
                n.Actor.Following?.Count ?? 0,
                0, false, n.Actor.JoinedAt),
            n.Post == null ? null : new PostDto(
                n.Post.Id, n.Post.Content, n.Post.MediaUrl, n.Post.ContentWarning,
                new NodeAccountDto(
                    n.Post.Author.Id, n.Post.Author.Username, null, n.Post.Author.AvatarUrl,
                    null, null, null,
                    n.Post.Author.NodeServer?.Domain ?? string.Empty,
                    n.Post.Author.CloudlightAccount?.IsPro ?? false,
                    n.Post.Author.IsNodeAdmin,
                    0, 0, 0, false, n.Post.Author.JoinedAt),
                n.Post.ParentPostId, null,
                n.Post.Likes?.Count ?? 0, n.Post.Replies?.Count ?? 0, n.Post.Reflys?.Count ?? 0,
                false, false, false, n.Post.IsReplicated, n.Post.CreatedAt, n.Post.EditedAt),
            n.IsRead, n.CreatedAt));

        return new NotificationsResponse(dtos, unread, total, page, pageSize);
    }

    public async Task MarkAllReadAsync(Guid nodeAccountId)
    {
        await db.Notifications
            .Where(n => n.RecipientId == nodeAccountId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
    }

    public async Task MarkReadAsync(Guid notificationId, Guid nodeAccountId)
    {
        var n = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientId == nodeAccountId)
            ?? throw new KeyNotFoundException("Notification not found.");
        n.IsRead = true;
        await db.SaveChangesAsync();
    }

    public async Task<int> GetUnreadCountAsync(Guid nodeAccountId) =>
        await db.Notifications.CountAsync(n => n.RecipientId == nodeAccountId && !n.IsRead);
}
