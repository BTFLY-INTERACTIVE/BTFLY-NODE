using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Btfly.API.Models.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Btfly.API.Services;

public interface ITokenService
{
    /// <summary>Issue a node-scoped JWT for a verified node account session.</summary>
    string GenerateNodeToken(NodeAccount nodeAccount, CloudlightAccount globalAccount);
}

/// <summary>
/// Issues node-session JWTs signed with an RSA private key.
/// The matching public key is published at api.login.btfly.social/.well-known/jwks.json
/// so any node can validate tokens without secrets.
///
/// The private key is stored base64-encoded in the BTFLY__NODEPRIVATEKEYB64
/// environment variable — never on disk, never in the repo.
/// </summary>
public class TokenService : ITokenService
{
    private readonly RsaSecurityKey _signingKey;
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config, ILogger<TokenService> log)
    {
        _config = config;

        var b64 = config["Btfly:NodePrivateKeyB64"]
            ?? throw new InvalidOperationException(
                "Btfly:NodePrivateKeyB64 is not set. " +
                "Generate a keypair and set BTFLY__NODEPRIVATEKEYB64 in your environment.");

        var pem = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        var rsa  = RSA.Create();
        rsa.ImportFromPem(pem);
        _signingKey = new RsaSecurityKey(rsa) { KeyId = "btfly-node-key-1" };

        log.LogInformation("Node RSA signing key loaded.");
    }

    public string GenerateNodeToken(NodeAccount nodeAccount, CloudlightAccount globalAccount)
    {
        var issuer   = _config["Btfly:JwtIssuer"]   ?? "https://node.btfly.social";
        var audience = _config["Btfly:JwtAudience"] ?? "btfly-node-session";
        var expDays  = int.Parse(_config["Btfly:TokenExpiryDays"] ?? "7");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, nodeAccount.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("global_sub",      globalAccount.Auth0Sub),
            new Claim("global_id",       globalAccount.Id.ToString()),
            new Claim("node_id",         nodeAccount.NodeServerId.ToString()),
            new Claim("username",        nodeAccount.Username),
            new Claim("display_name",    globalAccount.DisplayName),
            new Claim("picture",         globalAccount.PictureUrl ?? string.Empty),
            new Claim("is_pro",          globalAccount.IsPro.ToString().ToLower()),
            new Claim("is_node_admin",   nodeAccount.IsNodeAdmin.ToString().ToLower()),
            new Claim("token_type",      "node_session"),
        };

        var jwt = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddDays(expDays),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
