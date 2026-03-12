using Btfly.API.Services;
using Btfly.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Btfly.API.Controllers;

[ApiController]
[Route("api/moderation")]
[Authorize]
public class ModerationController(IModerationService moderationService) : ControllerBase
{
    // The node account ID is the JWT sub
    private Guid NodeAccountId => Guid.Parse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? throw new UnauthorizedAccessException());

    // ─── Global Bans (Platform Admin only) ───────────────────────────────────

    /// <summary>
    /// Issue a global ban on a Cloudlight account.
    /// Propagates across all nodes — enforced at the identity layer on next login.
    /// </summary>
    [HttpPost("global-ban/{cloudlightAccountId}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> GlobalBan(
        Guid cloudlightAccountId,
        [FromBody] GlobalBanRequest req)
    {
        await moderationService.GlobalBanAsync(cloudlightAccountId, req.Reason, NodeAccountId);
        return NoContent();
    }

    /// <summary>Lift a global ban.</summary>
    [HttpDelete("global-ban/{cloudlightAccountId}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> LiftGlobalBan(Guid cloudlightAccountId)
    {
        await moderationService.LiftGlobalBanAsync(cloudlightAccountId, NodeAccountId);
        return NoContent();
    }

    // ─── Node Bans (Node Admin) ───────────────────────────────────────────────

    /// <summary>
    /// Issue a node-level ban. Only affects this node — Cloudlight account and
    /// other node accounts remain unaffected.
    /// </summary>
    [HttpPost("nodes/{nodeServerId}/ban")]
    public async Task<IActionResult> NodeBan(
        Guid nodeServerId,
        [FromBody] NodeBanRequest req)
    {
        await moderationService.NodeBanAsync(
            nodeServerId, req.NodeAccountId, req.Reason, NodeAccountId);
        return NoContent();
    }

    /// <summary>Lift a node-level ban.</summary>
    [HttpDelete("nodes/{nodeServerId}/ban/{targetNodeAccountId}")]
    public async Task<IActionResult> LiftNodeBan(
        Guid nodeServerId,
        Guid targetNodeAccountId)
    {
        await moderationService.LiftNodeBanAsync(nodeServerId, targetNodeAccountId, NodeAccountId);
        return NoContent();
    }
}
