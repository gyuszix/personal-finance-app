# Deployment

Everything needed to host the API is in the repo, and **nothing is
deployed** until you run the commands below (#123). Two targets:

| | What you get | Cost |
|---|---|---|
| **Any Docker host** (a VPS, a home server) | `docker-compose.prod.yml`: Caddy with automatic HTTPS in front of the API, Postgres and the dashboard on the internal network | the host |
| **Azure** | `infra/main.bicep`: Container Apps (API + migrate job) + PostgreSQL Flexible Server + Log Analytics, HTTPS at the ingress | see [Cost](#cost) |

Both run the same image (`Dockerfile`) and the same `migrate` command.

## What "production-shaped" means here

- **HTTPS only.** TLS ends at the proxy (Caddy, or the Container Apps
  ingress). Plain HTTP is redirected there, and the API redirects too
  (`UseHttpsRedirection`). Every HTTPS response carries
  `Strict-Transport-Security: max-age=31536000; includeSubDomains` (one
  year; not preloaded, since that's a one-way commitment for the domain).
- **Forwarded headers trusted from one hop only**, and only with
  `ReverseProxy:Enabled=true`, which is safe because the API's own port
  isn't published. Caddy strips anything a client sends in
  `X-Forwarded-For`, so audit IPs and rate limits see the real client.
- **Security headers** on every response: `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, a
  `Content-Security-Policy` of `default-src 'none'` (the API serves JSON,
  never pages), and a restrictive `Permissions-Policy`.
- **Least-privilege database role.** The API is `pf_app` under row-level
  security (#119), and it refuses to start in Production as a role that
  bypasses RLS. Only the migrate job uses the owner.
- **Secrets from the platform**, never the image or the repo. Postgres
  connections use `SSL Mode=VerifyFull` on Azure.

## Option A: Docker host with Caddy

```bash
cp .env.example .env   # fill everything in; add DOMAIN=api.example.com
                       # and PLAID_WEBHOOK_URL=https://api.example.com/api/v1/plaid/webhook
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
curl https://api.example.com/health/ready
```

DNS for `DOMAIN` must point at the host, with ports 80 and 443 open. Caddy
gets and renews a Let's Encrypt certificate itself. With no `DOMAIN` it
serves `https://localhost` from its own local CA (`curl -k`, or
`docker compose ... exec caddy caddy trust`).

What was checked on a local run: `http://` gets a `308` to `https://`, the
API and Postgres ports aren't published, `/health/ready` is `200` over
HTTP/2, the security headers are present, and a spoofed
`X-Forwarded-For` doesn't reach the audit log. (ASP.NET doesn't send HSTS
for `localhost`. `HttpsHardeningTests` covers it with a real host name.)

## Option B: Azure Container Apps

### Prerequisites

- An Azure subscription (Azure for Students includes credit), the
  [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), `az login`
- The image in a registry Container Apps can pull from. GitHub Container
  Registry is free for public images:
  ```bash
  echo "$(gh auth token)" | docker login ghcr.io -u <github-user> --password-stdin   # token needs write:packages
  docker build -t ghcr.io/<github-user>/personalfinance-api:0.3.0 .
  docker push ghcr.io/<github-user>/personalfinance-api:0.3.0
  # then make the package public in GitHub → Packages → Settings
  ```

### Deploy

```bash
az group create -n personalfinance -l eastus

export PF_IMAGE=ghcr.io/<github-user>/personalfinance-api:0.3.0
export PF_POSTGRES_ADMIN_PASSWORD="$(openssl rand -base64 24)"
export PF_APP_ROLE_PASSWORD="$(openssl rand -base64 24)"
export PF_JWT_KEY="$(openssl rand -base64 48)"
export PF_ENCRYPTION_KEY="$(scripts/new-kek.sh)"
export PF_PLAID_CLIENT_ID=... PF_PLAID_SECRET=...
# Keep these somewhere safe (a password manager): the KEK above is the only
# way to decrypt stored Plaid tokens.

az deployment group create -g personalfinance -f infra/main.bicep -p infra/main.bicepparam
az containerapp job start -g personalfinance -n pfapp-migrate     # migrations + pf_app's password
az containerapp job execution list -g personalfinance -n pfapp-migrate -o table   # wait for Succeeded
curl "$(az deployment group show -g personalfinance -n main --query properties.outputs.apiUrl.value -o tsv)/health/ready"
```

The API's first revision may start before the migrate job finishes. It
fails readiness until then and recovers on its own. Point the MAUI app at
it with `PERSONALFINANCE_API_URL=<apiUrl>`.

### Releasing a new version

```bash
docker build -t ghcr.io/<github-user>/personalfinance-api:0.3.1 . && docker push ...
az containerapp job update -g personalfinance -n pfapp-migrate --image ghcr.io/<github-user>/personalfinance-api:0.3.1
az containerapp job start  -g personalfinance -n pfapp-migrate
az containerapp update     -g personalfinance -n pfapp-api     --image ghcr.io/<github-user>/personalfinance-api:0.3.1
```

Migrations go first. They only ever add (columns, tables, indexes), so
the old revision keeps working against the new schema while the new one
rolls out.

### Rolling back

Each `az containerapp update` creates a new **revision**, and the previous
one is kept:

```bash
az containerapp revision list -g personalfinance -n pfapp-api -o table
az containerapp ingress traffic set -g personalfinance -n pfapp-api --revision-weight <previous-revision>=100
```

Migrations aren't rolled back automatically. Restore the database to a
point in time instead (`az postgres flexible-server restore`, 7 days of
backups) if a migration has to be undone.

### Rotating secrets

- **KEK** ([`KEY_MANAGEMENT.md`](KEY_MANAGEMENT.md)): add `k2` as a new
  secret and env var `Encryption__Keys__k2`, set `Encryption__CurrentKeyId=k2`,
  then run `rewrap-secrets` as a one-off job (same image, `args: ["rewrap-secrets"]`,
  with the API's environment), and finally remove `k1`.
- **pf_app password**: change `PF_APP_ROLE_PASSWORD`, redeploy, and run the
  migrate job (it sets the password again).
- **JWT key**: redeploy with a new `PF_JWT_KEY`. Every access token stops
  validating within 10 minutes, and clients refresh with their refresh
  tokens, which are unaffected.

### Teardown

```bash
az group delete -n personalfinance   # everything, including the database and its backups
```

## Cost

Rough monthly figures, pay-as-you-go in US regions as of late 2026. Check
the [pricing calculator](https://azure.microsoft.com/pricing/calculator/):

| Resource | Size | ≈ / month |
|---|---|---|
| PostgreSQL Flexible Server | B1ms, 32 GB | ~$15 |
| Container Apps (API) | 0.5 vCPU / 1 GiB, always 1 replica | ~$15-20 (part is covered by the monthly free grant) |
| Container Apps job (migrate) | seconds per release | ~$0 |
| Log Analytics | a demo's logs | ~$0-3 (5 GB/month free) |

Stopping the database when it's not in use
(`az postgres flexible-server stop`) saves most of its cost. Azure starts
it again after 7 days.

## Upgrades not done (yet)

- **Private networking**: VNet-integrate the Container Apps environment
  and use Postgres private access, instead of the "allow Azure services"
  firewall rule.
- **Managed identity + Key Vault** for the KEK
  ([`KEY_MANAGEMENT.md`](KEY_MANAGEMENT.md#production-upgrade-a-managed-kms))
  and for Postgres (Entra ID auth instead of passwords).
- **A custom domain** with a managed certificate on the Container App.
