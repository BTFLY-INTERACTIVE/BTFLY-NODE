# 🦋 BTFLY Node — Railway Deployment

## Required Environment Variables

Set these in Railway → your service → Variables:

| Variable | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | From Railway Postgres addon |
| `Cloudlight__BaseUrl` | `https://api.login.btfly.social` |
| `Cloudlight__Issuer` | `https://api.login.btfly.social` |
| `Cloudlight__Audience` | `btfly-node` |
| `Cloudlight__JwksUrl` | `https://api.login.btfly.social/.well-known/jwks.json` |
| `Btfly__NodePrivateKeyB64` | Generate below |
| `Btfly__NodePublicKeyB64` | Generate below |
| `Btfly__JwtIssuer` | `https://your-node.up.railway.app` |
| `Btfly__JwtAudience` | `btfly-node-session` |
| `Btfly__TokenExpiryDays` | `7` |

## Generating RSA Keys (run locally)

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out /tmp/node.pem
# Private key b64:
base64 -w0 /tmp/node.pem
# Public key b64:
openssl pkey -in /tmp/node.pem -pubout | base64 -w0
```

Or reuse the keys from your local .env if you ran it before.

## Deploy Steps

1. Create Railway project → add PostgreSQL addon
2. Connect this repo as a GitHub service
3. Set all env vars above
4. Railway deploys automatically

## After First Deploy

Register your node (do this once via Swagger at /swagger):
```json
POST /api/nodes
{
  "domain": "your-node.up.railway.app",
  "displayName": "My Node",
  "serverType": 1
}
```
Then set the client app's node URL to your Railway domain.
