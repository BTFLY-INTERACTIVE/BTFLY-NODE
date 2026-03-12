using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Btfly.API.Data;
using Btfly.API.DTOs;
using Btfly.API.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Btfly.API.Services;

public interface IAuthService
{
    /// <summary>Generate a one-time node key and return the Cloudlight login URL.</summary>
    /// <param name="returnUrl">
    /// Where Cloudlight should redirect after auth. Defaults to https://{nodeDomain}/auth/complete.
    /// Pass the client app URL to redirect tokens back to the frontend.
    /// </param>
    Task<GenerateKeyResponse> GenerateNodeKeyAsync(Guid nodeServerId, string? returnUrl = null);

    /// <summary>
    /// Complete the node login handshake.
    /// Validates the BTFLY global token from api.login.btfly.social,
    /// upserts the CloudlightAccount mirror, creates/finds the NodeAccount,
    /// and issues a node-scoped JWT.
    /// </summary>
    Task<NodeLoginResponse> CompleteNodeLoginAsync(CompleteNodeLoginRequest req);
}

public class AuthService(
    BtflyDbContext db,
    ITokenService tokens,
    IConfiguration config,
    ILogger<AuthService> log) : IAuthService
{
    // ── JWKS config from the central auth service ─────────────────────────────
    private string CloudlightIssuer =>
        config["Cloudlight:Issuer"] ?? "https://api.login.btfly.social";

    private string CloudlightAudience =>
        config["Cloudlight:Audience"] ?? "btfly-node";

    private string CloudlightJwksUrl =>
        config["Cloudlight:JwksUrl"] ?? "https://api.login.btfly.social/.well-known/jwks.json";

    // ─────────────────────────────────────────────────────────────────────────

    public async Task<GenerateKeyResponse> GenerateNodeKeyAsync(Guid nodeServerId, string? returnUrl = null)
    {
        var node = await db.NodeServers.FindAsync(nodeServerId)
            ?? throw new KeyNotFoundException("Node server not found.");

        var key = new PendingLoginKey
        {
            Key          = Guid.NewGuid().ToString("N")[..16].ToUpper(),
            NodeServerId = nodeServerId,
            ExpiresAt    = DateTime.UtcNow.AddMinutes(10)
        };

        db.PendingLoginKeys.Add(key);
        await db.SaveChangesAsync();

        var baseLoginUrl = config["Cloudlight:BaseUrl"] ?? "https://api.login.btfly.social";

        // Use caller-supplied returnUrl (e.g. the client app) or fall back to node's /auth/complete.
        // This ensures tokens land on the right page after Cloudlight auth.
        var effectiveReturnUrl = !string.IsNullOrWhiteSpace(returnUrl)
            ? returnUrl
            : $"https://{node.Domain}/auth/complete";

        var loginUrl =
            $"{baseLoginUrl}/api/auth/login" +
            $"?nodeKey={Uri.EscapeDataString(key.Key)}" +
            $"&nodeDomain={Uri.EscapeDataString(node.Domain)}" +
            $"&returnUrl={Uri.EscapeDataString(effectiveReturnUrl)}";

        return new GenerateKeyResponse(key.Key, loginUrl, key.ExpiresAt);
    }

    public async Task<NodeLoginResponse> CompleteNodeLoginAsync(CompleteNodeLoginRequest req)
    {
        // ── 1. Validate the BTFLY global token against the Cloudlight JWKS ────
        ClaimsPrincipal principal;
        try
        {
            principal = await ValidateBtflyTokenAsync(req.BtflyToken);
        }
        catch (Exception ex)
        {
            log.LogWarning("BTFLY token validation failed: {Message}", ex.Message);
            throw new UnauthorizedAccessException("Invalid or expired Cloudlight token.");
        }

        // ── 2. Extract claims from the validated token ─────────────────────────
        var auth0Sub     = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new UnauthorizedAccessException("Token missing sub claim.");
        var email        = principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty;
        var displayName  = principal.FindFirstValue("display_name") ?? email;
        var pictureUrl   = principal.FindFirstValue("picture");
        var emailVerified = principal.FindFirstValue("email_verified") == "true";

        if (!emailVerified)
            throw new UnauthorizedAccessException("Email address is not verified.");

        // ── 3. Verify the one-time node key ────────────────────────────────────
        var node = await db.NodeServers
            .FirstOrDefaultAsync(n => n.Domain == req.NodeDomain && n.IsActive)
            ?? throw new KeyNotFoundException($"Node '{req.NodeDomain}' not found or inactive.");

        var pendingKey = await db.PendingLoginKeys
            .FirstOrDefaultAsync(k =>
                k.Key          == req.Key &&
                k.NodeServerId == node.Id &&
                !k.IsUsed      &&
                k.ExpiresAt    > DateTime.UtcNow)
            ?? throw new UnauthorizedAccessException(
                "Invalid or expired login key. Please start the login process again.");

        pendingKey.IsUsed = true;

        // ── 4. Upsert CloudlightAccount (mirror of Auth0 identity) ────────────
        var globalAccount = await db.CloudlightAccounts
            .FirstOrDefaultAsync(a => a.Auth0Sub == auth0Sub);

        if (globalAccount == null)
        {
            globalAccount = new CloudlightAccount
            {
                Auth0Sub    = auth0Sub,
                Email       = email,
                DisplayName = displayName,
                PictureUrl  = pictureUrl,
            };
            db.CloudlightAccounts.Add(globalAccount);
            log.LogInformation("New CloudlightAccount created for {Sub}", auth0Sub);
        }
        else
        {
            // Keep the mirror fresh
            globalAccount.Email       = email;
            globalAccount.DisplayName = displayName;
            globalAccount.PictureUrl  = pictureUrl;
            globalAccount.LastSeenAt  = DateTime.UtcNow;
        }

        if (globalAccount.IsGloballyBanned)
            throw new UnauthorizedAccessException(
                $"This account has been globally suspended. Reason: {globalAccount.GlobalBanReason}");

        // ── 5. Find or create NodeAccount ──────────────────────────────────────
        var nodeAccount = await db.NodeAccounts
            .Include(a => a.NodeServer)
            .FirstOrDefaultAsync(a =>
                a.CloudlightAccountId == globalAccount.Id &&
                a.NodeServerId        == node.Id);

        if (nodeAccount == null)
        {
            nodeAccount = new NodeAccount
            {
                CloudlightAccountId = globalAccount.Id,
                NodeServerId        = node.Id,
                Username            = displayName,
                AvatarUrl           = pictureUrl,
            };
            db.NodeAccounts.Add(nodeAccount);
            log.LogInformation("New NodeAccount created for {Sub} on {Domain}",
                auth0Sub, node.Domain);
        }

        await db.SaveChangesAsync();

        // ── 6. Issue node-scoped JWT ───────────────────────────────────────────
        var nodeToken = tokens.GenerateNodeToken(nodeAccount, globalAccount);
        var dto       = MapNodeAccount(nodeAccount, globalAccount);

        return new NodeLoginResponse(nodeToken, dto);
    }

    // ── JWT validation against Cloudlight JWKS ────────────────────────────────

    private async Task<ClaimsPrincipal> ValidateBtflyTokenAsync(string token)
    {
        // Fetch the JWKS from the central auth service (cached by ConfigurationManager)
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{CloudlightIssuer}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());

        OpenIdConnectConfiguration oidcConfig;
        try
        {
            oidcConfig = await configManager.GetConfigurationAsync();
        }
        catch
        {
            // Fall back to fetching JWKS directly if discovery doc isn't available
            var jwksRetriever = new ConfigurationManager<OpenIdConnectConfiguration>(
                CloudlightJwksUrl,
                new OpenIdConnectConfigurationRetriever());
            oidcConfig = await jwksRetriever.GetConfigurationAsync();
        }

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = CloudlightIssuer,
            ValidateAudience         = true,
            ValidAudience            = CloudlightAudience,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys        = oidcConfig.SigningKeys,
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateToken(token, validationParams, out _);
    }

    private static NodeAccountDto MapNodeAccount(NodeAccount a, CloudlightAccount g) => new(
        a.Id,
        a.Username,
        a.Bio,
        a.AvatarUrl,
        a.NodeServer?.Domain ?? string.Empty,
        g.IsPro,
        a.Followers?.Count ?? 0,
        a.Following?.Count ?? 0,
        a.JoinedAt);
}
