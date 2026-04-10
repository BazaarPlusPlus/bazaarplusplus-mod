# ModCFServerV3

`ModCFServerV3` is the Cloudflare Worker backend for the V3 BazaarPlusPlus online flow.

It accepts installation-authenticated uploads from the mod, stores compressed run bundles in Cloudflare R2, projects queryable metadata into Cloudflare D1, and serves ghost battle discovery plus replay download links for the in-game history panel.

## Responsibilities

- Activate a player account and issue installer sessions.
- Register signed installations for a player account.
- Accept observation uploads tied to an installation.
- Store uploaded run bundles in R2 and project runs and battles into D1.
- Expose ghost battle queries and short-lived replay download links.

## Runtime

- Cloudflare Workers
- Cloudflare D1 for relational metadata
- Cloudflare R2 for uploaded run bundle artifacts

Bindings defined in `wrangler.toml`:

- `DB`: D1 database
- `RUN_BUNDLE_BUCKET`: R2 bucket for uploaded V3 run bundle artifacts

## HTTP Routes

Routes are defined in `src/index.ts`.

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Health check |
| `POST` | `/activate` | Activate a player account for V3 auth |
| `POST` | `/login` | Create an installer session |
| `POST` | `/installations` | Register a signed installation |
| `POST` | `/installations/observations` | Upload an installation observation |
| `POST` | `/run-bundles` | Upload a compressed run bundle and project its metadata |
| `GET` | `/ghost-battles` | Query recent ghost battles against the signed player account |
| `POST` | `/ghost-battles/:battleId/replay-link` | Mint a short-lived replay download URL |
| `GET` | `/replays/:token` | Download a replay payload through a signed token |

## Auth Model

V3 uses installation-based authentication.

- `/activate` creates or claims the V3 user identity.
- `/login` creates an installer session.
- `/installations` binds an installation public key to the player account.
- Installation-authenticated routes verify the installation signature and player/account match before accepting writes.
- Replay link minting and replay downloads can be temporarily opened for unauthenticated clients through `ALLOW_UNAUTHENTICATED_REPLAY_LINKS` and `ALLOW_UNAUTHENTICATED_REPLAY_DOWNLOADS` during early rollout. These two flags should normally be switched together.

## Data Flow

### Activation and installation

1. The client activates a player account through `/activate`.
2. The installer creates a session through `/login`.
3. The installer posts the installation public key to `/installations`.

### Run bundle uploads

1. The mod sends a signed run bundle to `/run-bundles`.
2. The worker validates installation identity, bundle shape, and battle projections.
3. The artifact is written to R2 under `run-bundles/<player_account_id>/<installation_id>/<run_id>/<sha256>.mpack.gz`.
4. D1 inserts the `run_bundles` row and projects `runs` plus `battles`.

### Ghost battle queries

1. The client calls `/ghost-battles`.
2. The worker resolves the signed installation/player context.
3. D1 returns recent battle projections where `opponent_account_id` matches that player.

### Replay downloads

1. The client requests `/ghost-battles/:battleId/replay-link`.
2. The worker checks that the battle belongs to the signed player account, unless anonymous replay-link minting is temporarily enabled.
3. It returns a short-lived tokenized URL for `/replays/:token`.
4. `/replays/:token` loads the owning run bundle from R2 and returns the replay payload for that battle, with installation auth required unless anonymous replay downloads are temporarily enabled.

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

## File Map

- `src/index.ts`: route table entrypoint
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
