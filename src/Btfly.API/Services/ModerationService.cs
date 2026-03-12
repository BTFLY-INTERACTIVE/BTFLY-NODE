using Btfly.API.Data;
using Btfly.API.DTOs;
using Btfly.API.Models.Entities;
using Btfly.API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Btfly.API.Services;

public interface IModerationService
{
    /// <summary>Platform-level: ban a Cloudlight account globally (propagates across all nodes).</summary>
    Task GlobalBanAsync(Guid cloudlightAccountId, string reason, Guid issuedByAdminId);

    /// <summary>Platform-level: lift a global ban.</summary>
    Task LiftGlobalBanAsync(Guid cloudlightAccountId, Guid issuedByAdminId);

    /// <summary>Node-level: ban a NodeAccount from a specific node.</summary>
    Task NodeBanAsync(Guid nodeServerId, Guid targetNodeAccountId, string reason, Guid issuedByNodeAccountId);

    /// <summary>Node-level: lift a node ban.</summary>
    Task LiftNodeBanAsync(Guid nodeServerId, Guid targetNodeAccountId, Guid issuedByNodeAccountId);
}

public class ModerationService(BtflyDbContext db) : IModerationService
{
    public async Task GlobalBanAsync(Guid cloudlightAccountId, string reason, Guid issuedByAdminId)
    {
        var admin = await db.CloudlightAccounts.FindAsync(issuedByAdminId)
            ?? throw new KeyNotFoundException("Admin account not found.");

        if (admin.Role != AccountRole.PlatformAdmin)
            throw new UnauthorizedAccessException("Only platform admins can issue global bans.");

        var target = await db.CloudlightAccounts.FindAsync(cloudlightAccountId)
            ?? throw new KeyNotFoundException("Target account not found.");

        target.IsGloballyBanned = true;
        target.GlobalBanReason = reason;
        target.GlobalBannedAt = DateTime.UtcNow;

        // Also mark all of their node accounts as banned for quick local checks
        var nodeAccounts = await db.NodeAccounts
            .Where(a => a.CloudlightAccountId == cloudlightAccountId)
            .ToListAsync();

        foreach (var na in nodeAccounts)
            na.IsNodeBanned = true;

        await db.SaveChangesAsync();
    }

    public async Task LiftGlobalBanAsync(Guid cloudlightAccountId, Guid issuedByAdminId)
    {
        var admin = await db.CloudlightAccounts.FindAsync(issuedByAdminId)
            ?? throw new KeyNotFoundException("Admin account not found.");

        if (admin.Role != AccountRole.PlatformAdmin)
            throw new UnauthorizedAccessException("Only platform admins can lift global bans.");

        var target = await db.CloudlightAccounts.FindAsync(cloudlightAccountId)
            ?? throw new KeyNotFoundException("Target account not found.");

        target.IsGloballyBanned = false;
        target.GlobalBanReason = null;
        target.GlobalBannedAt = null;

        // Restore node accounts (they'll still have individual node bans if any)
        var nodeAccounts = await db.NodeAccounts
            .Where(a => a.CloudlightAccountId == cloudlightAccountId)
            .ToListAsync();

        // Only restore accounts that were banned solely due to global ban
        // (those with explicit NodeBan records keep their node-level bans)
        var explicitlyBannedNodeIds = await db.NodeBans
            .Where(b => nodeAccounts.Select(na => na.Id).Contains(b.NodeAccountId))
            .Select(b => b.NodeAccountId)
            .ToListAsync();

        foreach (var na in nodeAccounts.Where(a => !explicitlyBannedNodeIds.Contains(a.Id)))
            na.IsNodeBanned = false;

        await db.SaveChangesAsync();
    }

    public async Task NodeBanAsync(Guid nodeServerId, Guid targetNodeAccountId, string reason, Guid issuedByNodeAccountId)
    {
        var issuer = await db.NodeAccounts
            .Include(a => a.CloudlightAccount)
            .FirstOrDefaultAsync(a => a.Id == issuedByNodeAccountId && a.NodeServerId == nodeServerId)
            ?? throw new KeyNotFoundException("Issuing admin account not found on this node.");

        if (!issuer.IsNodeAdmin && issuer.CloudlightAccount.Role != AccountRole.PlatformAdmin)
            throw new UnauthorizedAccessException("Only node admins can issue node bans.");

        var target = await db.NodeAccounts
            .FirstOrDefaultAsync(a => a.Id == targetNodeAccountId && a.NodeServerId == nodeServerId)
            ?? throw new KeyNotFoundException("Target account not found on this node.");

        target.IsNodeBanned = true;

        if (!await db.NodeBans.AnyAsync(b => b.NodeAccountId == targetNodeAccountId && b.NodeServerId == nodeServerId))
        {
            db.NodeBans.Add(new NodeBan
            {
                NodeServerId = nodeServerId,
                NodeAccountId = targetNodeAccountId,
                Reason = reason,
                IssuedByNodeAccountId = issuedByNodeAccountId
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task LiftNodeBanAsync(Guid nodeServerId, Guid targetNodeAccountId, Guid issuedByNodeAccountId)
    {
        var issuer = await db.NodeAccounts
            .Include(a => a.CloudlightAccount)
            .FirstOrDefaultAsync(a => a.Id == issuedByNodeAccountId && a.NodeServerId == nodeServerId)
            ?? throw new KeyNotFoundException("Issuing admin account not found on this node.");

        if (!issuer.IsNodeAdmin && issuer.CloudlightAccount.Role != AccountRole.PlatformAdmin)
            throw new UnauthorizedAccessException("Only node admins can lift node bans.");

        var target = await db.NodeAccounts
            .FirstOrDefaultAsync(a => a.Id == targetNodeAccountId && a.NodeServerId == nodeServerId)
            ?? throw new KeyNotFoundException("Target account not found.");

        // Check they don't have an active global ban before restoring
        var globalAccount = await db.CloudlightAccounts.FindAsync(target.CloudlightAccountId);
        if (globalAccount?.IsGloballyBanned != true)
            target.IsNodeBanned = false;

        var ban = await db.NodeBans.FirstOrDefaultAsync(b =>
            b.NodeAccountId == targetNodeAccountId &&
            b.NodeServerId == nodeServerId);

        if (ban != null)
            db.NodeBans.Remove(ban);

        await db.SaveChangesAsync();
    }
}
