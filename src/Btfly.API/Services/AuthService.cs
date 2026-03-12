using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;
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
    Task<GenerateKeyResponse> GenerateNodeKeyAsync(Guid nodeServerId, string? returnUrl = null);
    Task<NodeLoginResponse> CompleteNodeLoginAsync(CompleteNodeLoginRequest req);
    Task<NodeAccountDto> SetupUsernameAsync(Guid nodeAccountId, string username);
}

public class AuthService(
    BtflyDbContext db,
    ITokenService tokens,
    IConfiguration config,
    ILogger<AuthService> log) : IAuthService
{
    private string CloudlightIssuer   => config["Cloudlight:Issuer"]   ?? "https://api.login.btfly.social";
    private string CloudlightAudience => config["Cloudlight:Audience"] ?? "btfly-node";
    private string CloudlightJwksUrl  => config["Cloudlight:JwksUrl"]  ?? "https://api.login.btfly.social/.well-known/jwks.json";

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
        // 1. Validate BTFLY global token
        ClaimsPrincipal principal;
        try { principal = await ValidateBtflyTokenAsync(req.BtflyToken); }
        catch (Exception ex)
        {
            log.LogWarning("BTFLY token validation failed: {Message}", ex.Message);
            throw new UnauthorizedAccessException("Invalid or expired Cloudlight token.");
        }

        // 2. Extract claims
        var auth0Sub      = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new UnauthorizedAccessException("Token missing sub claim.");
        var email         = principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty;
        var displayName   = principal.FindFirstValue("display_name") ?? email;
        var pictureUrl    = principal.FindFirstValue("picture");
        var emailVerified = principal.FindFirstValue("email_verified") == "true";

        if (!emailVerified)
            throw new UnauthorizedAccessException("Email address is not verified.");

        // 3. Verify one-time node key
        var node = await db.NodeServers
            .FirstOrDefaultAsync(n => n.Domain == req.NodeDomain && n.IsActive)
            ?? throw new KeyNotFoundException($"Node '{req.NodeDomain}' not found or inactive.");

        var pendingKey = await db.PendingLoginKeys
            .FirstOrDefaultAsync(k =>
                k.Key          == req.Key &&
                k.NodeServerId == node.Id &&
                !k.IsUsed      &&
                k.ExpiresAt    > DateTime.UtcNow)
            ?? throw new UnauthorizedAccessException("Invalid or expired login key. Please start the login process again.");

        pendingKey.IsUsed = true;

        // 4. Upsert CloudlightAccount mirror
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
            globalAccount.Email       = email;
            globalAccount.DisplayName = displayName;
            globalAccount.PictureUrl  = pictureUrl;
            globalAccount.LastSeenAt  = DateTime.UtcNow;
        }

        if (globalAccount.IsGloballyBanned)
            throw new UnauthorizedAccessException(
                $"This account has been globally suspended. Reason: {globalAccount.GlobalBanReason}");

        // 5. Find or create NodeAccount
        bool isNewAccount = false;
        var nodeAccount = await db.NodeAccounts
            .Include(a => a.NodeServer)
            .Include(a => a.Followers)
            .Include(a => a.Following)
            .FirstOrDefaultAsync(a =>
                a.CloudlightAccountId == globalAccount.Id &&
                a.NodeServerId        == node.Id);

        if (nodeAccount == null)
        {
            isNewAccount = true;
            // Generate a temporary username from email — user will be prompted to change it
            var tempUsername = await GenerateTempUsernameAsync(email, node.Id);
            nodeAccount = new NodeAccount
            {
                CloudlightAccountId  = globalAccount.Id,
                NodeServerId         = node.Id,
                Username             = tempUsername,
                AvatarUrl            = pictureUrl,
                RequiresUsernameSetup = true,
            };
            db.NodeAccounts.Add(nodeAccount);
            log.LogInformation("New NodeAccount created for {Sub} on {Domain}", auth0Sub, node.Domain);
        }

        await db.SaveChangesAsync();

        var nodeToken = tokens.GenerateNodeToken(nodeAccount, globalAccount);
        var dto       = MapNodeAccount(nodeAccount, globalAccount);

        return new NodeLoginResponse(nodeToken, dto, nodeAccount.RequiresUsernameSetup);
    }

    public async Task<NodeAccountDto> SetupUsernameAsync(Guid nodeAccountId, string username)
    {
        // Validate username format
        if (string.IsNullOrWhiteSpace(username) || username.Length < 2 || username.Length > 30)
            throw new ArgumentException("Username must be between 2 and 30 characters.");

        if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
            throw new ArgumentException("Username can only contain letters, numbers, and underscores.");

        var nodeAccount = await db.NodeAccounts
            .Include(a => a.NodeServer)
            .Include(a => a.CloudlightAccount)
            .Include(a => a.Followers)
            .Include(a => a.Following)
            .FirstOrDefaultAsync(a => a.Id == nodeAccountId)
            ?? throw new KeyNotFoundException("Node account not found.");

        // Check uniqueness on this node
        var taken = await db.NodeAccounts.AnyAsync(a =>
            a.NodeServerId == nodeAccount.NodeServerId &&
            a.Username.ToLower() == username.ToLower() &&
            a.Id != nodeAccountId);

        if (taken)
            throw new InvalidOperationException($"Username '{username}' is already taken on this node.");

        nodeAccount.Username             = username;
        nodeAccount.RequiresUsernameSetup = false;
        await db.SaveChangesAsync();

        return MapNodeAccount(nodeAccount, nodeAccount.CloudlightAccount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> GenerateTempUsernameAsync(string email, Guid nodeServerId)
    {
        // e.g. "alice@example.com" → "alice" then "alice_1" if taken
        var base_ = Regex.Replace(email.Split('@')[0], @"[^a-zA-Z0-9_]", "_");
        base_ = base_[..Math.Min(base_.Length, 20)];

        var candidate = base_;
        var suffix = 1;
        while (await db.NodeAccounts.AnyAsync(a =>
            a.NodeServerId == nodeServerId &&
            a.Username.ToLower() == candidate.ToLower()))
        {
            candidate = $"{base_}_{suffix++}";
        }
        return candidate;
    }

    private async Task<ClaimsPrincipal> ValidateBtflyTokenAsync(string token)
    {
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{CloudlightIssuer}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());

        OpenIdConnectConfiguration oidcConfig;
        try { oidcConfig = await configManager.GetConfigurationAsync(); }
        catch
        {
            var fallback = new ConfigurationManager<OpenIdConnectConfiguration>(
                CloudlightJwksUrl, new OpenIdConnectConfigurationRetriever());
            oidcConfig = await fallback.GetConfigurationAsync();
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
        handler.InboundClaimTypeMap.Clear();
        return handler.ValidateToken(token, validationParams, out _);
    }

    internal static NodeAccountDto MapNodeAccount(NodeAccount a, CloudlightAccount g) => new(
        a.Id, a.Username, a.Bio, a.AvatarUrl, a.HeaderUrl, a.WebsiteUrl, a.Location,
        a.NodeServer?.Domain ?? string.Empty,
        g.IsPro, a.IsNodeAdmin,
        a.Followers?.Count ?? 0,
        a.Following?.Count ?? 0,
        0, // PostCount fetched separately when needed
        a.RequiresUsernameSetup,
        a.JoinedAt);
}
