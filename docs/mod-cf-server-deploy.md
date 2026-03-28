# ModCFServer Deployment

This document describes the current deployment flow for `ModCFServer` as configured in
`ModCFServer/wrangler.toml`.

## Current Production Targets

- Worker name: `bazaarplusplus-mod-api`
- Custom domain: `mod-api.bazaarplusplus.com`
- D1 database name: `bazaarplusplus-mod-api-db`
- R2 bucket name: `bazaarplusplus-replays`

The mod client currently defaults to:

- run upload: `https://mod-api.bazaarplusplus.com/runs/upload`
- client registration: `https://mod-api.bazaarplusplus.com/clients/register`

## Prerequisites

- `bazaarplusplus.com` is managed in the same Cloudflare account you will deploy from.
- `mod-api.bazaarplusplus.com` is available for Worker custom-domain binding.
- Node.js and npm are installed locally.
- Wrangler authentication is available through `npx wrangler login`.

## One-Time Setup

From the repository root:

```powershell
cd ModCFServer
npm install
npx wrangler login
```

Create the D1 database:

```powershell
npx wrangler d1 create bazaarplusplus-mod-api-db
```

Wrangler will print a `database_id`. Copy that value into `ModCFServer/wrangler.toml`:

```toml
[[d1_databases]]
binding = "DB"
database_name = "bazaarplusplus-mod-api-db"
database_id = "replace-me"
```

Create the R2 bucket:

```powershell
npx wrangler r2 bucket create bazaarplusplus-replays
```

Create the replay-download signing secret:

```powershell
npx wrangler secret put REPLAY_DOWNLOAD_SECRET
```

Use a long random secret value. The Worker expects this binding in `ModCFServer/src/env.ts`.

## Deploy

Deploy the Worker:

```powershell
npm run deploy
```

`package.json` maps this to `wrangler deploy`.

## Verify

After deploy, verify the Worker is reachable on the custom domain:

```powershell
curl -Method POST https://mod-api.bazaarplusplus.com/health
```

Expected response body:

```json
{"ok":true}
```

The current routes are implemented in `ModCFServer/src/index.ts`:

- `POST /health`
- `POST /clients/register`
- `POST /runs/upload`
- `POST /replays/upload`
- `GET /me/pvp-battles/against-me`
- `POST /me/pvp-battles/:battleId/replay-download-link`
- `GET /replays/download`

## Schema Initialization

No separate migration step is currently required.

On each request, the Worker calls `ensureSchema(env)` from `ModCFServer/src/persistence/schema.ts`.
That code creates the required D1 tables and indexes if they do not already exist.

## Redeploy Checklist

When updating an existing deployment:

1. Confirm `ModCFServer/wrangler.toml` still points at the correct `database_id`.
2. Confirm `REPLAY_DOWNLOAD_SECRET` is already present in the target Cloudflare environment.
3. Run `npm run deploy`.
4. Re-check `POST /health`.

## Common Issues

- `custom domain not available`
  - Check that `bazaarplusplus.com` is in the same Cloudflare account and that `mod-api.bazaarplusplus.com` is not already claimed elsewhere.
- `D1_ERROR: no such table`
  - Send one request to the Worker and re-check. The schema is created lazily by request handling.
- `authentication error on replay download`
  - Recreate `REPLAY_DOWNLOAD_SECRET` and redeploy.
- mod uploads still target the wrong host
  - Check the generated `BazaarPlusPlus.cfg` values under the `RunUpload` section and make sure they were not manually overridden.
