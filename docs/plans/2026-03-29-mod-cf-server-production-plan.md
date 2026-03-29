# ModCFServer Production Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 完成 `ModCFServer` 的首次正式版落地，把当前 demo 级 upload/query server 收敛成可部署的 `Workers + D1 + R2` 生产基线。

**Architecture:** 保持单 Worker API，`D1` 负责注册、状态、绑定和 battle 读模型，`R2` 负责 run/replay 原始对象存储。因为这是第一次部署，直接重写 `ModCFServer/migrations/0001_initial_schema.sql` 到正式版目标形状，不做兼容层、不做历史迁移代码。

**Tech Stack:** Cloudflare Workers, D1, R2, TypeScript, Wrangler, Node test runner (`tsx --test`)

---

### Task 1: Freeze The Identity Binding Decision

**Files:**
- Modify: `docs/plans/2026-03-29-mod-cf-server-production-design.md`
- Inspect: `Game/GameDataReader.cs`
- Inspect: `Game/PvpBattles/PvpBattleSnapshotCollector.cs`
- Inspect: `Game/HistoryPanel/GhostBattleSyncService.cs`
- Create or Modify: `ModCFServer/src/features/bindClient.ts`
- Create or Modify: `ModCFServer/test/bindClient.test.ts`

**Step 1: Confirm which stable identity the mod can actually send**

Read:

- `Game/GameDataReader.cs`
- `Game/PvpBattles/PvpBattleSnapshotCollector.cs`
- `Game/HistoryPanel/GhostBattleSyncService.cs`

Expected: determine whether the client can reliably send Bazaar `uid`, only `player_account_id`, or both.

**Step 2: Lock the production identity shape before editing server schema**

If the runtime exposes `uid`, keep the current design:

- `client_uid_bindings(client_id -> uid)`
- `uid_player_accounts(uid -> player_account_id)`

If the runtime only exposes `player_account_id`, update the production design doc before coding and simplify the binding model accordingly. Do not implement a half-working `uid` path.

Expected: the design doc reflects the final identity shape used by the codebase.

**Step 3: Write a failing binding API test**

Add `ModCFServer/test/bindClient.test.ts` for the chosen identity model. The test should assert:

- the signed client can call the binding route
- the server writes an active binding row
- the server can optionally persist an observed `player_account_id`

Run:

```bash
cd ModCFServer
npx tsx --test test/bindClient.test.ts
```

Expected: FAIL because the route and persistence logic do not exist yet.

**Step 4: Implement the binding endpoint**

Add a dedicated signed route in `ModCFServer/src/features/bindClient.ts`, wire it in `ModCFServer/src/index.ts`, and extend `ModCFServer/src/persistence/bindings.ts` with explicit upsert helpers.

Recommended route:

```text
POST /clients/bind
```

Recommended payload shape:

```json
{
  "uid": "uid-001",
  "player_account_id": "player-account-001"
}
```

If Task 1 resolves that `uid` is unavailable, replace `uid` with the chosen production identity and update the route contract consistently.

**Step 5: Run the binding tests**

Run:

```bash
cd ModCFServer
npx tsx --test test/bindClient.test.ts test/ghostBattles.test.ts
```

Expected: PASS. Ghost battle tests should no longer depend on hand-seeded bindings alone.

**Step 6: Commit**

```bash
git add docs/plans/2026-03-29-mod-cf-server-production-design.md ModCFServer/src/index.ts ModCFServer/src/features/bindClient.ts ModCFServer/src/persistence/bindings.ts ModCFServer/test/bindClient.test.ts ModCFServer/test/ghostBattles.test.ts
git commit -m "Add client binding flow for production server"
```

### Task 2: Rewrite The Initial D1 Schema To The Production Baseline

**Files:**
- Modify: `ModCFServer/migrations/0001_initial_schema.sql`
- Modify: `ModCFServer/src/types/db.ts`
- Modify: `ModCFServer/test/schema.test.ts`
- Modify: `ModCFServer/test/helpers/mockEnv.ts`

**Step 1: Write failing schema tests for the production columns**

Extend `ModCFServer/test/schema.test.ts` to assert that `0001_initial_schema.sql` contains:

- production `run_uploads` columns such as `payload_object_key`, `payload_bytes`, `projection_version`, `projected_battle_count`, `last_error_code`, `created_at_utc`, `updated_at_utc`
- production `replay_uploads` columns such as `payload_bytes`, `schema_version`, `content_type`, `created_at_utc`, `updated_at_utc`
- production `pvp_battles` columns such as `summary_json`, `replay_available`, `projection_version`

Run:

```bash
cd ModCFServer
npx tsx --test test/schema.test.ts
```

Expected: FAIL because the migration still contains the old schema.

**Step 2: Rewrite `0001_initial_schema.sql` in place**

Because this is the first deployment:

- edit `ModCFServer/migrations/0001_initial_schema.sql`
- do not add `0002_*.sql`
- do not write compatibility DDL
- do not keep old columns only for migration convenience

Implement the production-first tables and indexes described in `docs/plans/2026-03-29-mod-cf-server-production-design.md`.

**Step 3: Align TypeScript row types and the mock database**

Update:

- `ModCFServer/src/types/db.ts`
- `ModCFServer/test/helpers/mockEnv.ts`

so tests compile against the new columns and query shapes.

**Step 4: Re-run schema tests**

Run:

```bash
cd ModCFServer
npx tsx --test test/schema.test.ts test/index.test.ts
```

Expected: PASS.

**Step 5: Commit**

```bash
git add ModCFServer/migrations/0001_initial_schema.sql ModCFServer/src/types/db.ts ModCFServer/test/schema.test.ts ModCFServer/test/helpers/mockEnv.ts
git commit -m "Promote ModCFServer schema to production baseline"
```

### Task 3: Archive Run Uploads In R2 And Turn `run_uploads` Into An Ingestion Ledger

**Files:**
- Modify: `ModCFServer/src/features/uploadRun.ts`
- Modify: `ModCFServer/src/persistence/runUploads.ts`
- Modify: `ModCFServer/src/types/db.ts`
- Modify: `ModCFServer/test/uploadRun.test.ts`
- Modify: `ModCFServer/test/helpers/mockEnv.ts`

**Step 1: Write failing tests for run object storage and status fields**

Extend `ModCFServer/test/uploadRun.test.ts` to assert:

- successful run uploads write the raw payload to `R2`
- `run_uploads.payload_object_key` is set
- `run_uploads.payload_bytes` is populated
- `run_uploads.projection_version` is set
- `run_uploads.projected_battle_count` matches the number of projected battles
- failed projections record `last_error_code` and `last_error_detail`

Run:

```bash
cd ModCFServer
npx tsx --test test/uploadRun.test.ts
```

Expected: FAIL because runs are not yet archived to `R2` and the extra ledger fields do not exist.

**Step 2: Store raw run payloads in `R2` before projection**

Implement in `ModCFServer/src/features/uploadRun.ts`:

- build object key `runs/{client_id}/{run_id}/{payload_sha256}.json`
- write the original request body to `env.REPLAY_BUCKET` only if a dedicated run bucket is not introduced yet
- set metadata for `payload-sha256`, `client-id`, `run-id`, `uploaded-at-utc`

Note: if you prefer a dedicated bucket, first add the second bucket binding and update `Env`; otherwise keep a single bucket for first deploy and separate by key prefix only.

**Step 3: Expand `run_uploads` persistence**

Update `ModCFServer/src/persistence/runUploads.ts` so `upsertRunUpload` and `markRunProjectionStatus` persist the production ledger fields:

- `payload_object_key`
- `payload_bytes`
- `schema_version`
- `projection_version`
- `projected_battle_count`
- `last_error_code`
- `last_error_detail`
- `created_at_utc`
- `updated_at_utc`

Use the status model:

- `received`
- `stored`
- `projecting`
- `projected`
- `failed`

**Step 4: Re-run focused tests**

Run:

```bash
cd ModCFServer
npx tsx --test test/uploadRun.test.ts test/schema.test.ts
```

Expected: PASS.

**Step 5: Commit**

```bash
git add ModCFServer/src/features/uploadRun.ts ModCFServer/src/persistence/runUploads.ts ModCFServer/src/types/db.ts ModCFServer/test/uploadRun.test.ts ModCFServer/test/helpers/mockEnv.ts
git commit -m "Archive run uploads in R2 and track ledger status"
```

### Task 4: Shrink `pvp_battles` Into A Query Model And Add Explicit Replay Availability

**Files:**
- Modify: `ModCFServer/src/features/uploadRun.ts`
- Modify: `ModCFServer/src/features/uploadRunPayload.ts`
- Modify: `ModCFServer/src/persistence/battleProjections.ts`
- Modify: `ModCFServer/src/features/ghostBattles.ts`
- Modify: `ModCFServer/test/uploadRun.test.ts`
- Modify: `ModCFServer/test/ghostBattles.test.ts`

**Step 1: Write failing tests for `summary_json` and `replay_available`**

Extend:

- `ModCFServer/test/uploadRun.test.ts`
- `ModCFServer/test/ghostBattles.test.ts`

to assert:

- projected rows store a trimmed `summary_json`
- `summary_json` still contains every field `GhostBattleApiClient` needs
- ghost battle queries can read `replay_available` directly from `pvp_battles`

Run:

```bash
cd ModCFServer
npx tsx --test test/uploadRun.test.ts test/ghostBattles.test.ts
```

Expected: FAIL because the code still stores `payload_json` and derives replay presence by join.

**Step 2: Build a projection summary instead of storing the full raw battle object**

In `ModCFServer/src/features/uploadRun.ts` and `ModCFServer/src/features/uploadRunPayload.ts`:

- keep parsing the raw battle input
- build a smaller summary object with only fields needed by ghost battle import and UI preview
- store that summary in `summary_json`

Do not re-store the whole run payload or the whole replay payload inside `pvp_battles`.

**Step 3: Add explicit replay availability updates**

In `ModCFServer/src/persistence/battleProjections.ts`:

- persist `replay_available`
- default it to `0` on run projection
- expose query helpers that no longer need to left join `replay_uploads` just to answer availability

**Step 4: Re-run focused tests**

Run:

```bash
cd ModCFServer
npx tsx --test test/uploadRun.test.ts test/ghostBattles.test.ts
```

Expected: PASS.

**Step 5: Commit**

```bash
git add ModCFServer/src/features/uploadRun.ts ModCFServer/src/features/uploadRunPayload.ts ModCFServer/src/persistence/battleProjections.ts ModCFServer/src/features/ghostBattles.ts ModCFServer/test/uploadRun.test.ts ModCFServer/test/ghostBattles.test.ts
git commit -m "Turn pvp battles into a production query model"
```

### Task 5: Expand Replay Upload Metadata And Sync Replay Availability

**Files:**
- Modify: `ModCFServer/src/features/uploadReplay.ts`
- Modify: `ModCFServer/src/features/uploadReplayPayload.ts`
- Modify: `ModCFServer/src/persistence/replayUploads.ts`
- Modify: `ModCFServer/src/persistence/battleProjections.ts`
- Modify: `ModCFServer/test/uploadReplay.test.ts`
- Modify: `ModCFServer/test/ghostBattles.test.ts`

**Step 1: Write failing replay metadata tests**

Extend `ModCFServer/test/uploadReplay.test.ts` to assert:

- `replay_uploads.payload_bytes` is stored
- `replay_uploads.schema_version` is stored
- `replay_uploads.content_type` is stored
- `replay_uploads.created_at_utc` and `updated_at_utc` are tracked separately
- uploading a replay marks `pvp_battles.replay_available = 1`

Run:

```bash
cd ModCFServer
npx tsx --test test/uploadReplay.test.ts
```

Expected: FAIL because replay metadata is still minimal and projection rows are not explicitly updated.

**Step 2: Parse and persist replay metadata**

Update `ModCFServer/src/features/uploadReplayPayload.ts` to extract the replay payload version for storage. Update `ModCFServer/src/features/uploadReplay.ts` and `ModCFServer/src/persistence/replayUploads.ts` to persist:

- `payload_bytes`
- `schema_version`
- `content_type`
- `created_at_utc`
- `updated_at_utc`

Use object key:

```text
replays/{client_id}/{battle_id}/{payload_sha256}.json
```

**Step 3: Mark the battle projection as replayable**

Add a helper in `ModCFServer/src/persistence/battleProjections.ts` to set `replay_available = 1` for a battle after replay upload succeeds.

**Step 4: Re-run focused tests**

Run:

```bash
cd ModCFServer
npx tsx --test test/uploadReplay.test.ts test/ghostBattles.test.ts
```

Expected: PASS.

**Step 5: Commit**

```bash
git add ModCFServer/src/features/uploadReplay.ts ModCFServer/src/features/uploadReplayPayload.ts ModCFServer/src/persistence/replayUploads.ts ModCFServer/src/persistence/battleProjections.ts ModCFServer/test/uploadReplay.test.ts ModCFServer/test/ghostBattles.test.ts
git commit -m "Persist replay metadata and sync replay availability"
```

### Task 6: Wire Production Config And Deployment Validation

**Files:**
- Modify: `ModCFServer/wrangler.toml`
- Modify: `docs/mod-cf-server-deploy.md`
- Modify: `docs/plans/2026-03-29-mod-cf-server-production-design.md`

**Step 1: Add any final Worker bindings needed by the chosen schema**

If Task 3 kept a single bucket, ensure the plan and deploy doc explicitly say that one bucket stores both:

- `runs/...`
- `replays/...`

If Task 3 introduced a dedicated run bucket, update:

- `ModCFServer/wrangler.toml`
- `ModCFServer/src/env.ts`
- deploy docs

accordingly.

**Step 2: Update the deploy doc to the first-deploy workflow**

Edit `docs/mod-cf-server-deploy.md` so it matches the production implementation:

- create D1
- write the real `database_id`
- apply `0001_initial_schema.sql`
- create `R2` bucket(s)
- set `REPLAY_DOWNLOAD_SECRET`
- deploy Worker
- verify `/health`
- run a signed register/upload smoke test

**Step 3: Run the full server verification set**

Run:

```bash
cd ModCFServer
npm install
npm run check
npm test
```

Expected:

- `npm run check` PASS
- `npm test` PASS

If `npm run check` still fails on missing local type packages, fix the workspace dependency state before claiming production readiness.

**Step 4: Commit**

```bash
git add ModCFServer/wrangler.toml docs/mod-cf-server-deploy.md docs/plans/2026-03-29-mod-cf-server-production-design.md
git commit -m "Finalize production deployment configuration"
```

### Task 7: Dry-Run The First Deployment

**Files:**
- No repo code changes required unless issues are found

**Step 1: Create the Cloudflare resources**

Run:

```bash
cd ModCFServer
npx wrangler d1 create bazaarplusplus-mod-api-db
npx wrangler r2 bucket create bazaarplusplus-replays
```

If a dedicated run bucket was added, also run:

```bash
npx wrangler r2 bucket create bazaarplusplus-runs
```

Expected: resource ids and names returned by Wrangler.

**Step 2: Apply the schema**

Run:

```bash
cd ModCFServer
npx wrangler d1 migrations apply bazaarplusplus-mod-api-db
```

Expected: `0001_initial_schema.sql` applied successfully.

**Step 3: Set secrets and deploy**

Run:

```bash
cd ModCFServer
npx wrangler secret put REPLAY_DOWNLOAD_SECRET
npm run deploy
```

Expected: Worker deploy succeeds and the route is active.

**Step 4: Smoke-test production**

Run:

```bash
curl https://mod-api.bazaarplusplus.com/health
```

Expected:

```json
{"ok":true}
```

Then manually verify:

- `POST /clients/register`
- `POST /runs/upload`
- `POST /replays/upload`
- `GET /me/pvp-battles/against-me`

using a signed test client or the mod itself.

**Step 5: Commit deployment notes if needed**

If the dry run reveals doc mismatches, update only the minimal deploy documentation and commit:

```bash
git add docs/mod-cf-server-deploy.md
git commit -m "Tighten first-deploy notes for ModCFServer"
```
