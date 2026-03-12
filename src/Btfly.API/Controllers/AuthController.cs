using Btfly.API.DTOs;
using Btfly.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Btfly.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>
    /// Step 1 — Node generates a one-time key and gets the Cloudlight login URL.
    ///
    /// The node redirects the user to the returned CloudlightLoginUrl.
    /// After Auth0 login, the user is sent back to the returnUrl (defaults to
    /// https://{nodeDomain}/auth/complete) with ?btfly_token=...&amp;node_key=...
    ///
    /// Pass { "returnUrl": "https://app.btfly.social/" } in the body to redirect
    /// back to the client app instead of the node domain.
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
    /// Step 2 — Complete the node login handshake.
    ///
    /// Called by the node after the user returns from Cloudlight auth.
    /// Validates the BTFLY global token, upserts the local CloudlightAccount mirror,
    /// and issues a node-scoped JWT for this session.
    /// </summary>
    [HttpPost("node/complete-login")]
    public async Task<ActionResult<NodeLoginResponse>> CompleteNodeLogin(
        [FromBody] CompleteNodeLoginRequest req)
    {
        var result = await authService.CompleteNodeLoginAsync(req);
        return Ok(result);
    }
}
