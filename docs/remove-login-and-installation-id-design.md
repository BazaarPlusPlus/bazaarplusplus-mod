# Remove login and installation_id design

- Date: 2026-05-06
- Status: approved (brainstorm)
- Successor doc: implementation plan (TBD via writing-plans)

## 1. Context

ModCFServerV3 currently fronts the mod with a bearer-token auth model: `POST /activate` and `POST /login` mint tokens, `requireBearerAuth` gates `/ghost-battles` and `/logout`, and the schema carries `users` / `tokens` tables plus `installation_id` columns on `runs` / `battles` / `run_bundles`. The mod side mirrors this with `Game/Identity/AuthStore.cs`, `Game/Online/BearerState.cs`, and the `BearerState` plumbing inside `ModOnlineClient` / `GhostBattleApiClient`.

The product decision is to remove all login-required logic and the `installation_id` concept. `/run-bundles` is already unauthenticated; both replay endpoints have already been running with `ALLOW_UNAUTHENTICATED_REPLAY_*=true` in `wrangler.toml`. The change formalises that direction and cleans up the dead infrastructure.

## 2. Goals and non-goals

### Goals
- Remove every code path, endpoint, table, column, file, and test whose only purpose is gating access by login.
- Replace the `users`-backed opponent registry with a purpose-built `seen_player_accounts` table so ghost-battles still filters out NPCs / forged opponent IDs.
- Make `/ghost-battles` a public endpoint that accepts `player_account_id` as a query string parameter.
- Drop the `installation_id` concept end-to-end (server schema, server handler, client upload payload, models, docs).

### Non-goals
- No change to the `replay_tokens` table — that lifecycle is independent of login.
- No change to `BazaarPlusPlus.csproj`'s `BPPInstallerSourcePath` / `CopyToInstallerSource` targets — those are DLL copy targets to a sibling Installer repo, not auth.
- No new abuse-mitigation primitives (rate limit, CAPTCHA, signed tokens). Option B's tradeoff is accepted: anyone can query ghost battles for any account ID, and the `seen_player_accounts` registry is still self-bootstrapped from `/run-bundles` uploads.
- The Installer (`bazaarplusplus-installer/`) lives in a sibling repo and is out of scope for this change.

## 3. Server-side changes (ModCFServerV3)

### 3.1 Endpoints removed
- `POST /activate`
- `POST /login`
- `POST /logout`

Routes deleted from `src/index.ts`. No deprecation aliases — Installer in the sibling repo will get 404 once migrations land, which is the intended signal that it must stop calling these.

### 3.2 Modules deleted
- `src/features/v3/activate.ts`
- `src/features/v3/login.ts`
- `src/features/v3/logout.ts`
- `src/features/v3/requireBearerAuth.ts`
- `src/crypto/password.ts`
- `src/token/generate.ts`

### 3.3 Endpoints rewritten

**`GET /ghost-battles`** — `src/features/v3/queryGhostBattles.ts`:
- Drops `requireBearerAuth` import / call.
- Reads `player_account_id` from `URL.searchParams`. Empty / missing → `400 invalid_request`.
- Existing `limit` clamp logic and SQL stay; only the source of `playerAccountId` changes.
- The query continues to hit `idx_battles_opponent_recorded_covering`; index is unchanged.

**`POST /ghost-battles/:battleId/replay-link`** — `src/features/v3/createReplayLink.ts`:
- The `allowUnauthenticatedReplayLinks(env)` branch is the only behavior; the bearer branch and `requesterPlayerAccountId` variable are deleted.
- `tokenOwnerPlayerAccountId` becomes `battleRow.opponent_account_id` directly. If null → `403 replay_forbidden` (preserves current null-guard).

**`GET /replays/:token`** — `src/features/v3/downloadReplay.ts`:
- Same shape: drop the bearer branch, remove `requesterPlayerAccountId` and the 403 forbidden check that compares it.

**`POST /run-bundles`** — `src/features/v3/uploadRunBundle.ts`:
- Drop `LegacyInstallationId = "legacy"` constant.
- Remove every `installation_id` binding from the `run_bundles` / `runs` / `battles` INSERT statements and parameter lists.
- Replace `loadKnownOpponentAccountIds`'s `users` query with a `seen_player_accounts` query (same shape: `WHERE player_account_id IN (?, ?, …)`).
- After validating the request, upsert the uploader's `player_account_id` into `seen_player_accounts` (skip when it equals `AnonymousPlayerAccountId`).

### 3.4 Configuration removed
- `wrangler.toml`: delete the two `ALLOW_UNAUTHENTICATED_REPLAY_*` vars and their preceding "Early rollout default…" comment block (currently lines 9–12). The `[vars]` section keeps only `GHOST_QUERY_LOOKBACK_DAYS` and `RUN_BUNDLE_RETENTION_DAYS`.
- `src/config/v3.ts`: drop `allowUnauthenticatedReplayLinks` and `allowUnauthenticatedReplayDownloads` accessors.

### 3.5 New table

```sql
CREATE TABLE seen_player_accounts (
  player_account_id TEXT PRIMARY KEY,
  first_seen_at_utc TEXT NOT NULL,
  last_seen_at_utc  TEXT NOT NULL
);
```

Upsert during `/run-bundles`:

```sql
INSERT INTO seen_player_accounts (player_account_id, first_seen_at_utc, last_seen_at_utc)
VALUES (?, ?1, ?1)
ON CONFLICT(player_account_id) DO UPDATE SET last_seen_at_utc = excluded.last_seen_at_utc;
```

Only the uploader is registered — opponent IDs from the payload are not auto-registered. This matches the current `users`-backed semantics: an opponent only counts as "real" once they themselves run the mod and upload a run.

### 3.6 D1 migrations (new files)

D1 generation rules (SQLite 3.45+) prevent `DROP COLUMN` for columns participating in PK / UNIQUE / foreign-key constraints. `tokens.player_account_id` REFERENCES `users(player_account_id)`, so drop `tokens` before `users`. `run_bundles.installation_id` is part of `UNIQUE (installation_id, run_id, payload_hash)` — that table needs the SQLite 12-step rebuild dance.

**`0009_drop_auth_tables.sql`**:
```sql
DROP INDEX IF EXISTS tokens_by_user;
DROP TABLE IF EXISTS tokens;
DROP TABLE IF EXISTS users;
```
(`DROP TABLE tokens` removes its index automatically; the explicit `DROP INDEX` is defensive.)

**`0010_drop_installation_id.sql`** — staged in two parts:

```sql
-- runs and battles: installation_id is a plain column, simple drop works.
ALTER TABLE runs    DROP COLUMN installation_id;
ALTER TABLE battles DROP COLUMN installation_id;

-- run_bundles: installation_id is part of UNIQUE(installation_id, run_id, payload_hash).
-- Rebuild the table without that column and without the now-redundant UNIQUE
-- (bundle_id is already the PK, current handler uses INSERT OR REPLACE on PK).
PRAGMA foreign_keys = OFF;

CREATE TABLE run_bundles_new (
  bundle_id TEXT PRIMARY KEY,
  player_account_id TEXT NOT NULL,
  run_id TEXT NOT NULL,
  payload_hash TEXT NOT NULL,
  schema_version INTEGER NOT NULL,
  object_key TEXT NOT NULL,
  codec TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  submitted_at_utc TEXT NOT NULL,
  created_at_utc TEXT NOT NULL
);

INSERT INTO run_bundles_new (
  bundle_id, player_account_id, run_id, payload_hash, schema_version,
  object_key, codec, size_bytes, submitted_at_utc, created_at_utc
)
SELECT
  bundle_id, player_account_id, run_id, payload_hash, schema_version,
  object_key, codec, size_bytes, submitted_at_utc, created_at_utc
FROM run_bundles;

DROP TABLE run_bundles;
ALTER TABLE run_bundles_new RENAME TO run_bundles;

-- Recreate indexes that previously existed on run_bundles.
CREATE INDEX IF NOT EXISTS idx_run_bundles_created_at
  ON run_bundles (created_at_utc, bundle_id);
CREATE INDEX IF NOT EXISTS idx_run_bundles_submitted_at
  ON run_bundles (submitted_at_utc, bundle_id);

PRAGMA foreign_keys = ON;
```

**`0011_create_seen_player_accounts.sql`**:
```sql
CREATE TABLE seen_player_accounts (
  player_account_id TEXT PRIMARY KEY,
  first_seen_at_utc TEXT NOT NULL,
  last_seen_at_utc  TEXT NOT NULL
);

-- Backfill from existing run_bundles uploaders so the first deploy of the new
-- worker doesn't lose ghost-battle visibility for already-seen opponents.
INSERT OR IGNORE INTO seen_player_accounts (player_account_id, first_seen_at_utc, last_seen_at_utc)
SELECT player_account_id, MIN(created_at_utc), MAX(created_at_utc)
FROM run_bundles
WHERE player_account_id IS NOT NULL
  AND player_account_id != ''
  AND player_account_id != 'anonymous-player'
GROUP BY player_account_id;
```

The backfill query uses `run_bundles` (which is already keyed by `player_account_id`) so that the moment 0011 runs, every previously-registered uploader becomes a known opponent. This avoids a "post-migration ghost-battle blackout" while users gradually re-upload runs.

### 3.7 Type and helper cleanup
- `src/types/db.ts`: drop `users` / `tokens` row types; drop `installation_id` from `runs` / `battles` / `run_bundles` row types.
- `RunBundleRequest` in `uploadRunBundle.ts`: drop `installation_id` field.
- `src/config/v3.ts`: drop the two replay-allow accessors.

### 3.8 Tests
**Delete:**
- `test/v3.activate.test.ts`
- `test/v3.login.test.ts`
- `test/v3.logout.test.ts`
- `test/v3.requireBearerAuth.test.ts`
- `test/token.generate.test.ts`

**Update:**
- `test/v3.ghostBattles.test.ts`: replace bearer-token setup with `?player_account_id=…` query string; cover the new `400 invalid_request` case.
- `test/v3.replays.test.ts`: drop bearer setup; remove the 401 / 403 cases; keep token-expiry / token-not-found / artifact-expired.
- `test/v3.runBundles.test.ts`: drop `installation_id` from request bodies; add coverage for `seen_player_accounts` upsert and the `loadKnownOpponentAccountIds` filter swap.
- `test/schema.test.ts`: update expected table set (drop `users` / `tokens`, add `seen_player_accounts`, `installation_id` gone from three tables).
- `test/helpers/seed.ts`: remove `insertV3User` / `insertToken`; add `insertSeenPlayerAccount`; drop `installation_id` from any seed builders.
- `test/index.test.ts`: drop assertions about removed routes.

## 4. Client-side changes (BazaarPlusPlus mod)

### 4.1 Files deleted
- `Game/Identity/AuthStore.cs`
- `Game/Identity/AuthRecord.cs`
- `Game/Online/BearerState.cs`
- `tests/IdentityDatabase.Tests/` (entire test project; the `.csproj` is standalone — no `.sln` references it, so directory removal is the whole story)

### 4.2 Files simplified

**`Game/Identity/IdentityJsonFileStore.cs`** — keep, but:
- Remove `AuthPath(...)` and any `auth.v1.json`-specific helpers.
- Remove the legacy-database cleanup that was scoped to auth files. `observation.v1.json` writes stay.

**`Game/Online/ModOnlineClient.cs`**:
- Drop `_bearer` field, `Bearer` property, `LoadBearerFrom`, `HandleUnauthorized`.
- Class becomes a thin holder of `HttpClient` + `V3Routes`.

**`Game/Identity/PlayerObservationController.cs`**:
- `Configure(...)` drops the `AuthStore` and `ModOnlineClient` parameters; only the observation store remains.
- Update logic stops calling `LoadBearerFrom`. Identity caching by account/username stays since it gates `observation.v1.json` writes.
- Bootstrap site (likely `Plugin.cs` or a dedicated bootstrap class) updated to match the new `Configure` signature.

**`Game/HistoryPanel/Ghost/GhostBattleApiClient.cs`**:
- Every method drops the `string bearerToken` parameter.
- `CreateBearerRequest` renamed to `CreateRequest` and stops setting `Authorization`.
- `QueryAgainstMeAsync` already builds the URI with `player_account_id=…`; no behavior change there.
- `V3HttpFailureClassifier`: drop the `ShouldReRegister` flag entirely (no caller acts on it after auth removal). Result structs `GhostBattleApiResult` / `GhostBattleReplayDownloadLinkResult` lose `ShouldReRegister`.

**`Game/HistoryPanel/Ghost/GhostBattleSyncService.cs`** and any other callers: remove `bearerToken` arguments at call sites; remove handling of `ShouldReRegister` paths.

**`Game/Online/Models/RunBundleUploadRequestV3.cs`**:
- Delete the `InstallationId` property and its serialization key.

**`Game/RunLogging/Upload/RunBundleUploadStore.cs`** and `RunBundleUploadService.cs`:
- `TryBuildRunBundleSnapshot(string runId, string installationId, string playerAccountId)` → `TryBuildRunBundleSnapshot(string runId, string playerAccountId)`. Update all callers.
- Any local code holding an `installationId` constant or wiring is deleted.

### 4.3 Startup-side cleanup

In `Plugin.cs` (mod bootstrap), perform a one-shot best-effort `File.Delete` of `auth.v1.json` under the identity directory if it exists. The call is idempotent — missing file is fine. Any other failure is logged at info level and ignored so a locked file never blocks startup. `observation.v1.json` is untouched.

## 5. Documentation updates

Edit (delete login + installation_id sections, refresh schema/contract examples):
- `ModCFServerV3/README.md`
- `ModCFServerV3/docs/api-reference.md`
- `docs/run-upload.md`
- `docs/reference/sqlite-schema-reference.md`
- `docs/reference/ghost-battle-data-flow.md`
- `docs/mod-features-overview.md`
- `docs/mod-cf-server-deploy.md`
- `docs/reference/settings-and-debug-surfaces.md` (audit; update if any auth-status surface is referenced)

Add a short note to the API reference explaining the new `/ghost-battles?player_account_id=…` contract and the `seen_player_accounts` registry.

## 6. Trade-offs (Option B accepted)

- **Open ghost-battle reads.** Anyone holding any `player_account_id` (these flow through public ghost-battle responses already) can call `/ghost-battles` for that ID. This was already true via stolen bearer tokens; the difference is one-step access.
- **Self-bootstrapped registry.** `seen_player_accounts` is filled by `/run-bundles` uploads, which are unauthenticated. A malicious uploader can register arbitrary `player_account_id` values for themselves, but doing so only helps them be filtered *in*, not *out*; opponent IDs in the payload are never auto-registered.
- **Permanent loss of production auth state.** Migration 0009 destroys all `users` / `tokens` rows. Acceptable per scope.
- **Installer breakage.** The sibling Installer repo's call to `/activate` and `/login` will start returning 404. Coordinated cleanup over there is the Installer team's follow-up.

## 7. Rollout

The change cannot be rolled out behind a flag because the schema migration is destructive. The deploy sequence is:

1. Land code change behind a single PR; CI runs the updated test suite end-to-end.
2. Pick a deploy window.
3. `wrangler d1 migrations apply` against the production D1 database (applies 0009 → 0010 → 0011).
4. Immediately `wrangler deploy` the new worker.
5. Smoke check: `GET /health`, `POST /run-bundles` with a known fixture, `GET /ghost-battles?player_account_id=<seeded id>`.

The mod release is independent — old mod builds still in the wild will simply hit 401-free endpoints; the bearer header they currently set is silently ignored by the new server.

## 8. Verification checklist

Before merging the implementation PR:

- [x] `npm test` green inside `ModCFServerV3/` — 43/43 passing across 7 test files.
- [x] `dotnet build BazaarPlusPlus.csproj` green — 0 errors, 0 warnings.
- [x] No remaining references to `users` / `tokens` / `installation_id` / `Authorization: Bearer` / `AuthStore` / `BearerState` in tracked files. Surviving hits are confined to migration files (the SQL that drops them), `test/schema.test.ts` deletion-canaries, `test/index.test.ts` deletion-canaries, and the spec/plan docs.
- [x] All 35 tasks committed in order from the implementation plan.
- [ ] `wrangler d1 migrations list` shows 0009 / 0010 / 0011 in order — pending deploy window.
- [ ] Manual smoke against staging worker: ghost-battles round-trip with a freshly uploaded run bundle — pending deploy window.
