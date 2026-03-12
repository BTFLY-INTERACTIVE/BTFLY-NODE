using Btfly.API.Data;
using Btfly.API.DTOs;
using Btfly.API.Models.Entities;
using Btfly.API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Btfly.API.Services;

public interface INodeService
{
    Task<NodeServerDto> RegisterNodeAsync(RegisterNodeRequest req);
    Task<NodeServerDto> GetNodeAsync(string domain);
    Task<IEnumerable<NodeServerDto>> ListNodesAsync(ServerType? filterType = null);
    Task DeactivateNodeAsync(Guid nodeId);
}

public class NodeService(BtflyDbContext db) : INodeService
{
    public async Task<NodeServerDto> RegisterNodeAsync(RegisterNodeRequest req)
    {
        if (await db.NodeServers.AnyAsync(n => n.Domain == req.Domain))
            throw new InvalidOperationException($"A node with domain '{req.Domain}' is already registered.");

        // Replication-only mode is only valid for Light servers
        if (req.IsReplicationOnly && req.ServerType != ServerType.Light)
            throw new InvalidOperationException("Replication-only mode is only available for Light servers.");

        // Read-only federation toggle is only for Dark servers
        if (req.AllowReadOnlyFederation && req.ServerType != ServerType.Dark)
            throw new InvalidOperationException("Read-only federation toggle only applies to Dark servers.");

        var node = new NodeServer
        {
            Domain = req.Domain.Trim().ToLowerInvariant(),
            DisplayName = req.DisplayName.Trim(),
            Description = req.Description,
            ServerType = req.ServerType,
            IsReplicationOnly = req.IsReplicationOnly,
            AllowReadOnlyFederation = req.AllowReadOnlyFederation
        };

        db.NodeServers.Add(node);
        await db.SaveChangesAsync();

        return MapNode(node, 0);
    }

    public async Task<NodeServerDto> GetNodeAsync(string domain)
    {
        var node = await db.NodeServers
            .Include(n => n.Accounts)
            .FirstOrDefaultAsync(n => n.Domain == domain.ToLowerInvariant())
            ?? throw new KeyNotFoundException($"Node '{domain}' not found.");

        return MapNode(node, node.Accounts.Count);
    }

    public async Task<IEnumerable<NodeServerDto>> ListNodesAsync(ServerType? filterType = null)
    {
        // Dark servers are not globally discoverable — exclude them from public listing
        var query = db.NodeServers
            .Include(n => n.Accounts)
            .Where(n => n.IsActive && n.ServerType != ServerType.Dark);

        if (filterType.HasValue)
            query = query.Where(n => n.ServerType == filterType.Value);

        var nodes = await query.ToListAsync();
        return nodes.Select(n => MapNode(n, n.Accounts.Count));
    }

    public async Task DeactivateNodeAsync(Guid nodeId)
    {
        var node = await db.NodeServers.FindAsync(nodeId)
            ?? throw new KeyNotFoundException("Node not found.");
        node.IsActive = false;
        await db.SaveChangesAsync();
    }

    private static NodeServerDto MapNode(NodeServer n, int memberCount) => new(
        n.Id, n.Domain, n.DisplayName, n.Description,
        n.ServerType, n.IsReplicationOnly, n.AllowReadOnlyFederation,
        memberCount, n.RegisteredAt);
}
