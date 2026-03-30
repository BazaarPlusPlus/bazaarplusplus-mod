# ModCFServer

`ModCFServer` is the Cloudflare Worker backend for BazaarPlusPlus.

It accepts signed client uploads from the mod, stores payloads in Cloudflare R2, keeps queryable metadata in Cloudflare D1, and serves replay discovery and replay download links for the in-game history panel.

## Responsibilities

- Register upload clients and persist their public keys.
- Verify signed requests for run uploads, replay uploads, and account binding requests.
- Store uploaded run payloads in R2 and record ingestion state in D1.
- Store uploaded replay payloads in R2 and project battle metadata into D1.
- Expose read APIs for "battles against me" and replay download links.
- Periodically purge consumed request nonces.

## Runtime

- Cloudflare Workers
- Cloudflare D1 for relational metadata
- Cloudflare R2 for uploaded JSON payloads

Bindings defined in `wrangler.toml`:

- `DB`: D1 database
- `PVP_BATTLE_BUCKET`: R2 bucket for uploaded run and replay payloads
- `REPLAY_DOWNLOAD_SECRET`: HMAC secret used to mint replay download links

## HTTP Routes

Routes are defined in `src/index.ts`.

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Health check |
| `POST` | `/clients/register` | Register a client public key for `runs` or `replays` |
| `POST` | `/clients/bind` | Bind a signed `runs` client to a player account |
| `POST` | `/runs/upload` | Upload a signed run payload |
| `POST` | `/battles/upload` | Upload a signed replay payload |
| `GET` | `/me/pvp-battles/against-me` | Query recent PvP battles against the bound player account |
| `POST` | `/me/pvp-battles/:battleId/replay-download-link` | Mint a short-lived replay download URL |
| `GET` | `/replays/download` | Download a replay payload through a signed URL |

## Signed Request Model

All authenticated endpoints use the same header-based signature scheme implemented in `src/features/verifiedClient.ts`.

Required headers:

- `x-bpp-client-id`
- `x-bpp-install-id`
- `x-bpp-timestamp`
- `x-bpp-nonce`
- `x-bpp-content-sha256`
- `x-bpp-signature`

Optional header:

- `x-bpp-signature-alg`
  Expected value: `rsa-pkcs1-sha256`

Validation rules:

- The client must already be registered with the requested purpose.
- The request timestamp must be within a 10 minute skew window.
- The advertised body hash must match the request body.
- The RSA signature must match the canonical request string.
- Endpoints that mutate state consume a nonce and reject reuse.

## Data Flow

### Client registration

1. The mod posts `install_id`, `purpose`, and an RSA public key to `/clients/register`.
2. The server derives a stable `client_id` from that payload and stores the registration in D1.

### Account binding

1. A signed `runs` client posts to `/clients/bind`.
2. The server records the active `client_id -> player_account_id` binding.
3. An optional observed opponent account can also be recorded.

### Run uploads

1. The mod sends a signed JSON payload to `/runs/upload`.
2. The server validates the request and determines the `run_id`.
3. The payload is written to R2 under `runs/<client_id>/<run_id>/<sha256>.json`.
4. D1 records the ingestion row and projection status transitions.

Current note:
The worker stores the run payload and marks projection lifecycle states, but it does not currently project run-internal `pvp_battles` data into the battle query model.

### Replay uploads

1. The mod sends a signed replay bundle to `/battles/upload`.
2. The server validates the request and parses the replay manifest.
3. The replay payload is written to R2 under `replays/<client_id>/<battle_id>/<sha256>.json`.
4. D1 upserts a `pvp_battles` row used by ghost battle queries and replay lookup.

### Ghost battle queries

1. The client calls `/me/pvp-battles/against-me`.
2. The worker resolves the currently bound player account for the caller.
3. D1 returns recent `PVPCombat` rows where `opponent_account_id` matches that account.

### Replay downloads

1. The client requests `/me/pvp-battles/:battleId/replay-download-link`.
2. The worker checks that the requested battle belongs to the bound account.
3. It returns a short-lived signed URL for `/replays/download`.
4. `/replays/download` validates the HMAC token and streams the JSON replay payload from R2.

## Local Development

Install dependencies:

```bash
npm install
```

Useful commands:

```bash
npm run dev
npm run check
npm test
npm run deploy
```

## Scheduled Work

The worker has a cron trigger configured in `wrangler.toml`.

- Every 6 hours it deletes expired request nonces.
- The nonce retention window is currently 15 minutes.

## File Map

- `src/index.ts`: route table and scheduled entrypoint
- `src/features/`: HTTP handlers
- `src/persistence/`: D1 persistence helpers
- `src/crypto/`: request signing and verification helpers
- `src/http/`: request and JSON helpers
- `migrations/`: D1 schema
- `test/`: Worker-level tests

## Verification

For this server package, the normal validation commands are:

```bash
npm test -- --runInBand
npx tsc --noEmit --noUnusedLocals --noUnusedParameters
```
