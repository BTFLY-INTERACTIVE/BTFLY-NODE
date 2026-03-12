using Btfly.API.DTOs;
using Btfly.API.Models.Enums;
using Btfly.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Btfly.API.Controllers;

[ApiController]
[Route("api/nodes")]
public class NodeController(INodeService nodeService) : ControllerBase
{
    /// <summary>
    /// List all discoverable nodes (Grey and Light only).
    /// Dark nodes are never publicly listed.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NodeServerDto>>> ListNodes(
        [FromQuery] ServerType? type = null)
    {
        var nodes = await nodeService.ListNodesAsync(type);
        return Ok(nodes);
    }

    /// <summary>Get a specific node by domain.</summary>
    [HttpGet("{domain}")]
    public async Task<ActionResult<NodeServerDto>> GetNode(string domain)
    {
        var node = await nodeService.GetNodeAsync(domain);
        return Ok(node);
    }

    /// <summary>Register a new node server. Requires a valid Cloudlight session.</summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<NodeServerDto>> RegisterNode([FromBody] RegisterNodeRequest req)
    {
        var node = await nodeService.RegisterNodeAsync(req);
        return CreatedAtAction(nameof(GetNode), new { domain = node.Domain }, node);
    }

    /// <summary>Deactivate a node (platform admin only).</summary>
    [HttpDelete("{nodeId}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> DeactivateNode(Guid nodeId)
    {
        await nodeService.DeactivateNodeAsync(nodeId);
        return NoContent();
    }
}
