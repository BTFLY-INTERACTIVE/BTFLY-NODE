using Btfly.API.DTOs;
using Btfly.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Btfly.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    private Guid NodeAccountId => Guid.Parse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? throw new UnauthorizedAccessException("Missing node account claim."));

    /// <summary>
    /// Step 1 — Generate a one-time login key and get the Cloudlight login URL.
    /// Pass { "returnUrl": "https://app.btfly.social/" } to redirect tokens back to the client.
    /// </summary>
    [HttpPost("node/{nodeServerId}/generate-key")]
    public async Task<ActionResult<GenerateKeyResponse>> GenerateNodeKey(
        Guid nodeServerId,
        [FromBody] GenerateKeyRequest? req = null)
    {
        var result = await authService.GenerateNodeKeyAsync(nodeServerId, req?.ReturnUrl);
        return Ok(result);
    }

    /// <summary>
    /// Step 2 — Complete the login handshake after Cloudlight auth.
    /// Returns RequiresUsernameSetup=true for new accounts — prompt user to choose a username.
    /// </summary>
    [HttpPost("node/complete-login")]
    public async Task<ActionResult<NodeLoginResponse>> CompleteNodeLogin(
        [FromBody] CompleteNodeLoginRequest req)
    {
        var result = await authService.CompleteNodeLoginAsync(req);
        return Ok(result);
    }

    /// <summary>
    /// Step 3 (new accounts only) — Set a username after first sign-up.
    /// Required when NodeLoginResponse.RequiresUsernameSetup is true.
    /// </summary>
    [HttpPost("setup-username")]
    [Authorize]
    public async Task<ActionResult<NodeAccountDto>> SetupUsername([FromBody] SetupUsernameRequest req)
    {
        var account = await authService.SetupUsernameAsync(NodeAccountId, req.Username);
        return Ok(account);
    }
}
