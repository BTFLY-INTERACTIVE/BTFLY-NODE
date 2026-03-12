using Btfly.API.DTOs;
using Btfly.API.Models.Enums;
using Btfly.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Btfly.API.Controllers;

[ApiController]
[Route("api/nodes")]
public class NodeController(INodeService nodeService, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NodeServerDto>>> ListNodes(
        [FromQuery] ServerType? type = null)
    {
        var nodes = await nodeService.ListNodesAsync(type);
        return Ok(nodes);
    }

    [HttpGet("{domain}")]
    public async Task<ActionResult<NodeServerDto>> GetNode(string domain)
    {
        var node = await nodeService.GetNodeAsync(domain);
        return Ok(node);
    }

    /// <summary>
    /// Register a new node. If Btfly__AdminKey is set, the X-Admin-Key header must match.
    /// If Btfly__AdminKey is not configured, registration is open.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<NodeServerDto>> RegisterNode([FromBody] RegisterNodeRequest req)
    {
        var adminKey = config["Btfly:AdminKey"];
        if (!string.IsNullOrEmpty(adminKey))
        {
            var provided = Request.Headers["X-Admin-Key"].FirstOrDefault();
            if (provided != adminKey)
                return Unauthorized(new { error = "Invalid or missing X-Admin-Key header." });
        }

        var node = await nodeService.RegisterNodeAsync(req);
        return CreatedAtAction(nameof(GetNode), new { domain = node.Domain }, node);
    }

    [HttpDelete("{nodeId}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> DeactivateNode(Guid nodeId)
    {
        await nodeService.DeactivateNodeAsync(nodeId);
        return NoContent();
    }
}