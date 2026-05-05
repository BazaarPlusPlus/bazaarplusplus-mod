# Remove login and installation_id implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Strip every login-required code path and the `installation_id` concept from the ModCFServerV3 worker, the BazaarPlusPlus mod, and supporting tests/docs, replacing the `users`-backed opponent filter with a purpose-built `seen_player_accounts` table.

**Architecture:** Phased deletion + targeted rewrite. Server schema migrations land first (0009 drops auth, 0010 drops `installation_id`, 0011 adds `seen_player_accounts`), then handler/test rewrites, then client-side simplification, then docs. The mod and worker can be developed in parallel against the new schema; deployment is a single destructive window (`wrangler d1 migrations apply` followed immediately by `wrangler deploy`).

**Tech Stack:** Cloudflare Worker (TypeScript) backed by D1 + R2; vitest with `@cloudflare/vitest-pool-workers`. C# mod targets `netstandard2.1` (BepInEx 5), built with `dotnet build`. C# test projects are standalone .NET 10 console apps run via `dotnet run --project tests/<Name>/<Name>.Tests.csproj`.

**Spec:** `docs/remove-login-and-installation-id-design.md` (commit `07ffda6`). All trade-offs in §6 of that doc are accepted.

**Note on transient test failures.** The deletion-heavy nature of this refactor means the vitest suite is *temporarily red* between the Task 4 commit (helpers/seed.ts purge) and the end of Phase 3 (each endpoint + its test rewritten). Each task's commit is a logical unit, not a green-suite checkpoint. Phase 3 should be completed in one sitting; Phase 7 is the green-suite gate.

---

## Setup (no commit)

- Confirm baseline is green before starting any task:

```bash
cd ModCFServerV3 && npm install
npm run check
npm test
cd ..
dotnet build BazaarPlusPlus.csproj
```

If any of those fail on master, stop and reconcile before proceeding. `dotnet build` requires `ManagedPath` resolvable per `.rules`; pass it explicitly if auto-detect fails.

Work directly on `master` per the user's previous workflow (no worktree). Each task below ends with one commit; do not batch tasks into a single commit.

---

## Phase 1 — Server schema migrations

### Task 1: Add migration 0009 dropping auth tables

**Files:**
- Create: `ModCFServerV3/migrations/0009_drop_auth_tables.sql`

- [ ] **Step 1: Write the migration**

```sql
-- Migration 0009: drop V3 auth (users + tokens).
--
-- Login is being removed end-to-end. tokens.player_account_id REFERENCES
-- users(player_account_id), so tokens drops first. tokens_by_user is dropped
-- defensively before the table; SQLite would also drop it implicitly.

DROP INDEX IF EXISTS tokens_by_user;
DROP TABLE IF EXISTS tokens;
DROP TABLE IF EXISTS users;
```

- [ ] **Step 2: Sanity check the migration applies in isolation**

Run: `cd ModCFServerV3 && npx wrangler d1 migrations list --local 2>&1 | head -20`

Expected: `0009_drop_auth_tables` is listed. (We don't apply it locally yet — vitest will refuse if tests still reference `users`/`tokens`. The next task will fix that.)

- [ ] **Step 3: Commit**

```bash
git add ModCFServerV3/migrations/0009_drop_auth_tables.sql
git commit -m "Add migration dropping V3 auth tables"
```

### Task 2: Add migration 0010 dropping installation_id

**Files:**
- Create: `ModCFServerV3/migrations/0010_drop_installation_id.sql`

`runs.installation_id` and `battles.installation_id` are plain `TEXT NOT NULL` columns with no PK/UNIQUE/index participation, so simple `DROP COLUMN` works. `run_bundles.installation_id` is part of `UNIQUE(installation_id, run_id, payload_hash)`, which SQLite cannot drop in place — rebuild via the documented copy-and-rename pattern.

- [ ] **Step 1: Write the migration**

```sql
-- Migration 0010: remove installation_id from runs/battles/run_bundles.
--
-- The mod no longer carries an installation identity; the column has been a
-- "legacy" placeholder since auth simplification. runs/battles can drop the
-- column directly. run_bundles needs a table rebuild because installation_id
-- participates in UNIQUE(installation_id, run_id, payload_hash). The UNIQUE
-- itself is now redundant: bundle_id is the primary key and the upload handler
-- uses INSERT OR REPLACE on it.

ALTER TABLE runs    DROP COLUMN installation_id;
ALTER TABLE battles DROP COLUMN installation_id;

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

CREATE INDEX IF NOT EXISTS idx_run_bundles_created_at
  ON run_bundles (created_at_utc, bundle_id);
CREATE INDEX IF NOT EXISTS idx_run_bundles_submitted_at
  ON run_bundles (submitted_at_utc, bundle_id);

PRAGMA foreign_keys = ON;
```

- [ ] **Step 2: Commit**

```bash
git add ModCFServerV3/migrations/0010_drop_installation_id.sql
git commit -m "Add migration dropping installation_id columns"
```

### Task 3: Add migration 0011 creating seen_player_accounts

**Files:**
- Create: `ModCFServerV3/migrations/0011_create_seen_player_accounts.sql`

- [ ] **Step 1: Write the migration**

```sql
-- Migration 0011: lightweight registry of player accounts that have uploaded
-- a run bundle. Replaces the auth-era users table for the ghost-battle
-- opponent filter. Backfill from existing run_bundles uploaders so the first
-- post-deploy ghost-battles query does not lose visibility for opponents we
-- already knew about.

CREATE TABLE seen_player_accounts (
  player_account_id TEXT PRIMARY KEY,
  first_seen_at_utc TEXT NOT NULL,
  last_seen_at_utc  TEXT NOT NULL
);

INSERT OR IGNORE INTO seen_player_accounts (
  player_account_id,
  first_seen_at_utc,
  last_seen_at_utc
)
SELECT
  player_account_id,
  MIN(created_at_utc),
  MAX(created_at_utc)
FROM run_bundles
WHERE player_account_id IS NOT NULL
  AND player_account_id != ''
  AND player_account_id != 'anonymous-player'
GROUP BY player_account_id;
```

- [ ] **Step 2: Commit**

```bash
git add ModCFServerV3/migrations/0011_create_seen_player_accounts.sql
git commit -m "Add migration creating seen_player_accounts registry"
```

---

## Phase 2 — Server test fixtures

### Task 4: Rewrite test/helpers/seed.ts

The vitest pool applies all migrations in the directory before each test, so once Tasks 1–3 are committed, the test environment will have **no** `users` / `tokens` tables and **no** `installation_id` column. The helpers must stop binding to those, must not reset already-removed env vars, and must offer a `seen_player_accounts` inserter.

**Files:**
- Modify: `ModCFServerV3/test/helpers/seed.ts`

- [ ] **Step 1: Replace seed.ts contents**

Remove `InsertTokenArgs`, `InsertV3UserArgs`, `insertToken`, `insertV3User`. Drop `installationId` from `InsertV3RunBundleArgs` / `InsertV3BattleArgs` and their inserters. Update `resetTestState` to no longer DELETE from `users` / `tokens` and no longer assign the removed env vars. Add `InsertSeenPlayerAccountArgs` + `insertSeenPlayerAccount`.

```typescript
type SqlValue = string | number | null;

export type InsertV3RunBundleArgs = {
  bundleId: string;
  playerAccountId: string;
  runId: string;
  payloadHash: string;
  schemaVersion: number;
  objectKey: string;
  codec: string;
  sizeBytes: number;
  submittedAtUtc: string;
  createdAtUtc: string;
};

export type InsertV3BattleArgs = {
  battleId: string;
  runId: string;
  playerAccountId: string;
  bundleId: string;
  recordedAtUtc: string;
  day?: number | null;
  playerName?: string | null;
  playerAccountIdInPayload?: string | null;
  playerHero?: string | null;
  playerRank?: string | null;
  playerRating?: number | null;
  playerLevel?: number | null;
  opponentName?: string | null;
  opponentAccountId?: string | null;
  opponentHero?: string | null;
  opponentRank?: string | null;
  opponentRating?: number | null;
  opponentLevel?: number | null;
  result?: string | null;
  replayAvailable: number;
  isBundleFinalBattle?: number;
  updatedAtUtc: string;
};

export type InsertReplayTokenArgs = {
  token: string;
  battleId: string;
  requestedByPlayerAccountId: string;
  expiresAtUtc: string;
  createdAtUtc: string;
  usedAtUtc?: string | null;
  revokedAtUtc?: string | null;
};

export type InsertSeenPlayerAccountArgs = {
  playerAccountId: string;
  firstSeenAtUtc: string;
  lastSeenAtUtc: string;
};

function statement<T extends SqlValue[]>(
  db: D1Database,
  sql: string,
  values: T,
): D1PreparedStatement {
  return db.prepare(sql).bind(...values);
}

export async function insertRunBundle(
  db: D1Database,
  args: InsertV3RunBundleArgs,
): Promise<void> {
  await statement(
    db,
    `
      INSERT INTO run_bundles (
        bundle_id,
        player_account_id,
        run_id,
        payload_hash,
        schema_version,
        object_key,
        codec,
        size_bytes,
        submitted_at_utc,
        created_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `,
    [
      args.bundleId,
      args.playerAccountId,
      args.runId,
      args.payloadHash,
      args.schemaVersion,
      args.objectKey,
      args.codec,
      args.sizeBytes,
      args.submittedAtUtc,
      args.createdAtUtc,
    ],
  ).run();
}

export async function insertV3Battle(
  db: D1Database,
  args: InsertV3BattleArgs,
): Promise<void> {
  await statement(
    db,
    `
      INSERT INTO battles (
        battle_id,
        run_id,
        player_account_id,
        bundle_id,
        recorded_at_utc,
        day,
        player_name,
        player_account_id_in_payload,
        player_hero,
        player_rank,
        player_rating,
        player_level,
        opponent_name,
        opponent_account_id,
        opponent_hero,
        opponent_rank,
        opponent_rating,
        opponent_level,
        result,
        replay_available,
        is_bundle_final_battle,
        updated_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `,
    [
      args.battleId,
      args.runId,
      args.playerAccountId,
      args.bundleId,
      args.recordedAtUtc,
      args.day ?? null,
      args.playerName ?? null,
      args.playerAccountIdInPayload ?? null,
      args.playerHero ?? null,
      args.playerRank ?? null,
      args.playerRating ?? null,
      args.playerLevel ?? null,
      args.opponentName ?? null,
      args.opponentAccountId ?? null,
      args.opponentHero ?? null,
      args.opponentRank ?? null,
      args.opponentRating ?? null,
      args.opponentLevel ?? null,
      args.result ?? null,
      args.replayAvailable,
      args.isBundleFinalBattle ?? 0,
      args.updatedAtUtc,
    ],
  ).run();
}

export async function insertReplayToken(
  db: D1Database,
  args: InsertReplayTokenArgs,
): Promise<void> {
  await statement(
    db,
    `
      INSERT INTO replay_tokens (
        token,
        battle_id,
        requested_by_player_account_id,
        expires_at_utc,
        created_at_utc,
        used_at_utc,
        revoked_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?)
    `,
    [
      args.token,
      args.battleId,
      args.requestedByPlayerAccountId,
      args.expiresAtUtc,
      args.createdAtUtc,
      args.usedAtUtc ?? null,
      args.revokedAtUtc ?? null,
    ],
  ).run();
}

export async function insertSeenPlayerAccount(
  db: D1Database,
  args: InsertSeenPlayerAccountArgs,
): Promise<void> {
  await statement(
    db,
    `
      INSERT INTO seen_player_accounts (
        player_account_id,
        first_seen_at_utc,
        last_seen_at_utc
      ) VALUES (?, ?, ?)
    `,
    [args.playerAccountId, args.firstSeenAtUtc, args.lastSeenAtUtc],
  ).run();
}

export async function countRows(
  db: D1Database,
  tableName: string,
): Promise<number> {
  const result = await db
    .prepare(`SELECT COUNT(*) AS count FROM ${tableName}`)
    .first<{ count: number }>();
  return result?.count ?? 0;
}

export async function selectFirst<T>(
  db: D1Database,
  sql: string,
  ...values: SqlValue[]
): Promise<T | null> {
  return db.prepare(sql).bind(...values).first<T>();
}

async function deleteAllR2(bucket: R2Bucket): Promise<void> {
  let cursor: string | undefined;

  do {
    const page = await bucket.list({ cursor });
    if (page.objects.length > 0) {
      await bucket.delete(page.objects.map((object) => object.key));
    }
    cursor = page.truncated ? page.cursor : undefined;
  } while (cursor != null);
}

export async function resetTestState(env: Cloudflare.Env): Promise<void> {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM replay_tokens"),
    env.DB.prepare("DELETE FROM battles"),
    env.DB.prepare("DELETE FROM runs"),
    env.DB.prepare("DELETE FROM run_bundles"),
    env.DB.prepare("DELETE FROM seen_player_accounts"),
  ]);
  await deleteAllR2(env.RUN_BUNDLE_BUCKET);
  env.GHOST_QUERY_LOOKBACK_DAYS = "3";
  env.RUN_BUNDLE_RETENTION_DAYS = "5";
}
```

- [ ] **Step 2: Run tests — expect widespread failures**

Run: `cd ModCFServerV3 && npm test 2>&1 | tail -40`

Expected: many failures referencing missing `insertV3User` / `insertToken`, `installation_id`, `users` / `tokens`. This is intentional — the impl/tests get rewritten in Phase 3.

- [ ] **Step 3: Commit**

```bash
git add ModCFServerV3/test/helpers/seed.ts
git commit -m "Drop auth/installation_id helpers from seed.ts"
```

---

## Phase 3 — Server endpoint rewrites and test updates

### Task 5: Rewrite uploadRunBundle.ts

**Files:**
- Modify: `ModCFServerV3/src/features/v3/uploadRunBundle.ts`

- [ ] **Step 1: Apply the targeted edits**

Make these specific changes:

1. Delete the line `const LegacyInstallationId = "legacy";`.
2. Remove `installation_id` from the `RunBundleRequest` body type — currently the type has no explicit `installation_id` field, but the JSON does. Keep the type as-is (the body field is just ignored).
3. Replace the `loadKnownOpponentAccountIds` function body so it queries `seen_player_accounts` instead of `users`:

```typescript
async function loadKnownOpponentAccountIds(
  battleProjections: BattleProjection[],
  uploaderPlayerAccountId: string,
  env: Env,
): Promise<Set<string>> {
  const opponentAccountIds = new Set<string>();
  const knownOpponentAccountIds = new Set<string>();

  if (uploaderPlayerAccountId && uploaderPlayerAccountId !== AnonymousPlayerAccountId) {
    knownOpponentAccountIds.add(uploaderPlayerAccountId);
  }

  for (const battle of battleProjections) {
    const opponentAccountId = optionalTrimmedString(battle.opponent_account_id);
    if (opponentAccountId && opponentAccountId !== uploaderPlayerAccountId) {
      opponentAccountIds.add(opponentAccountId);
    }
  }

  if (opponentAccountIds.size === 0) {
    return knownOpponentAccountIds;
  }

  const opponentAccountIdList = Array.from(opponentAccountIds);
  const placeholders = opponentAccountIdList.map(() => "?").join(", ");
  const result = await env.DB.prepare(
    `
      SELECT player_account_id
      FROM seen_player_accounts
      WHERE player_account_id IN (${placeholders})
    `,
  )
    .bind(...opponentAccountIdList)
    .all<{ player_account_id: string }>();

  for (const row of result.results) {
    knownOpponentAccountIds.add(row.player_account_id);
  }

  return knownOpponentAccountIds;
}
```

4. Strip `installation_id` from `insertRunBundleProjection`. Remove the `LegacyInstallationId` bind and the column from the SQL:

```typescript
async function insertRunBundleProjection(args: {
  env: Env;
  bundleId: string;
  persistedPlayerAccountId: string;
  runId: string;
  payloadHash: string;
  schemaVersion: number;
  objectKey: string;
  artifactCodec: string;
  sizeBytes: number;
  submittedAtUtc: string;
  createdAtUtc: string;
}): Promise<void> {
  // bundle_id is deterministically the run_id: one run -> one run_bundles row.
  // INSERT OR REPLACE keeps idempotent retries cheap on PK conflict.
  await args.env.DB.prepare(
    `
      INSERT OR REPLACE INTO run_bundles (
        bundle_id,
        player_account_id,
        run_id,
        payload_hash,
        schema_version,
        object_key,
        codec,
        size_bytes,
        submitted_at_utc,
        created_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `,
  )
    .bind(
      args.bundleId,
      args.persistedPlayerAccountId,
      args.runId,
      args.payloadHash,
      args.schemaVersion,
      args.objectKey,
      args.artifactCodec,
      args.sizeBytes,
      args.submittedAtUtc,
      args.createdAtUtc,
    )
    .run();
}
```

5. Strip `installation_id` from `upsertRunProjection`'s INSERT/UPDATE column list and bind list. The runs table no longer has the column.

6. Strip `installation_id` from `upsertBattleProjections`'s prepared statement. Remove `installation_id` from the column list and from `excluded.installation_id` in the ON CONFLICT clause; remove the `LegacyInstallationId` bind.

7. After the run bundle / runs / battles upserts succeed, register the uploader in `seen_player_accounts`. Add this helper above `handleUploadRunBundle`:

```typescript
async function rememberUploader(
  env: Env,
  uploaderPlayerAccountId: string,
  nowUtc: string,
): Promise<void> {
  if (
    !uploaderPlayerAccountId ||
    uploaderPlayerAccountId === AnonymousPlayerAccountId
  ) {
    return;
  }

  await env.DB.prepare(
    `
      INSERT INTO seen_player_accounts (
        player_account_id,
        first_seen_at_utc,
        last_seen_at_utc
      ) VALUES (?, ?, ?)
      ON CONFLICT(player_account_id) DO UPDATE SET
        last_seen_at_utc = excluded.last_seen_at_utc
    `,
  )
    .bind(uploaderPlayerAccountId, nowUtc, nowUtc)
    .run();
}
```

Call it at the end of `handleUploadRunBundle`, before the success response:

```typescript
  await rememberUploader(env, persistedPlayerAccountId, createdAtUtc);

  return json({ status: "accepted", bundle_id: bundleId, object_key: objectKey });
```

- [ ] **Step 2: Commit**

```bash
git add ModCFServerV3/src/features/v3/uploadRunBundle.ts
git commit -m "Swap users registry for seen_player_accounts in uploadRunBundle"
```

### Task 6: Rewrite v3.runBundles.test.ts

**Files:**
- Modify: `ModCFServerV3/test/v3.runBundles.test.ts`

- [ ] **Step 1: Replace `registerPlayer`**

Change the helper from `insertV3User` to `insertSeenPlayerAccount`:

```typescript
import {
  countRows,
  insertSeenPlayerAccount,
  resetTestState,
  selectFirst,
} from "./helpers/seed";

async function registerPlayer(playerAccountId: string): Promise<void> {
  await insertSeenPlayerAccount(env.DB, {
    playerAccountId,
    firstSeenAtUtc: "2026-04-10T00:00:00.000Z",
    lastSeenAtUtc: "2026-04-10T00:00:00.000Z",
  });
}
```

- [ ] **Step 2: Drop every `installation_id` assertion**

In each `selectFirst<{ installation_id: string }>(...)` block, drop the field from the type, the SELECT list, and the corresponding `expect(...).toBe("legacy")`. The remaining asserts on the row (e.g. `bundle?.object_key`) stay.

- [ ] **Step 3: Add a `seen_player_accounts` upsert assertion to the happy-path test**

Append to the first test ("run bundle upload stores one artifact object and projection rows"):

```typescript
  const seen = await selectFirst<{
    player_account_id: string;
    first_seen_at_utc: string;
    last_seen_at_utc: string;
  }>(
    env.DB,
    `
      SELECT player_account_id, first_seen_at_utc, last_seen_at_utc
      FROM seen_player_accounts
      WHERE player_account_id = ?
    `,
    "player-account-001",
  );
  expect(seen?.player_account_id).toBe("player-account-001");
  expect(seen?.first_seen_at_utc).toBe(seen?.last_seen_at_utc);
```

- [ ] **Step 4: Add an upsert-on-retry assertion to the duplicate-retry test**

In "run bundle upload accepts duplicate payload retries idempotently", after the second response, assert `seen_player_accounts` still has exactly one row whose `last_seen_at_utc >= first_seen_at_utc`:

```typescript
  const seenRows = await env.DB.prepare(
    `SELECT player_account_id, first_seen_at_utc, last_seen_at_utc FROM seen_player_accounts`,
  ).all<{
    player_account_id: string;
    first_seen_at_utc: string;
    last_seen_at_utc: string;
  }>();
  expect(seenRows.results.length).toBe(1);
  expect(seenRows.results[0]!.player_account_id).toBe("player-account-001");
  expect(
    Date.parse(seenRows.results[0]!.last_seen_at_utc) >=
      Date.parse(seenRows.results[0]!.first_seen_at_utc),
  ).toBe(true);
```

- [ ] **Step 5: Anonymous-uploader test must NOT register**

In "run bundle upload accepts requests without player account id", append:

```typescript
  expect(await countRows(env.DB, "seen_player_accounts")).toBe(0);
```

- [ ] **Step 6: Run the file in isolation**

Run: `cd ModCFServerV3 && npm test -- test/v3.runBundles.test.ts 2>&1 | tail -40`
Expected: PASS (all 9 tests).

- [ ] **Step 7: Commit**

```bash
git add ModCFServerV3/test/v3.runBundles.test.ts
git commit -m "Update run bundle tests to seen_player_accounts and no installation_id"
```

### Task 7: Rewrite queryGhostBattles.ts

**Files:**
- Modify: `ModCFServerV3/src/features/v3/queryGhostBattles.ts`

- [ ] **Step 1: Replace handler**

```typescript
import type { Env } from "../../env";
import { getGhostQueryLookbackDays } from "../../config/v3";
import { json } from "../../http/json";
import { parseClampedInteger, trimString } from "../../http/request";

type GhostBattleRow = {
  battle_id: string;
  recorded_at_utc: string;
  day: number | null;
  player_name: string | null;
  player_account_id_in_payload: string | null;
  player_hero: string | null;
  player_rank: string | null;
  player_rating: number | null;
  player_level: number | null;
  opponent_name: string | null;
  opponent_account_id: string | null;
  opponent_hero: string | null;
  opponent_rank: string | null;
  opponent_rating: number | null;
  opponent_level: number | null;
  result: string | null;
  replay_available: number;
  is_bundle_final_battle: number;
};

export async function handleQueryGhostBattles(
  request: Request,
  env: Env,
): Promise<Response> {
  const url = new URL(request.url);
  const playerAccountId = trimString(url.searchParams.get("player_account_id"));
  if (!playerAccountId) {
    return json({ error: "invalid_request" }, { status: 400 });
  }

  const lookbackDays = getGhostQueryLookbackDays(env);
  const limit = parseClampedInteger(url.searchParams.get("limit"), 200, 1, 200);
  const fromUtc = new Date(Date.now() - lookbackDays * 24 * 60 * 60 * 1000).toISOString();

  const result = await env.DB.prepare(
    `
      SELECT
        b.battle_id,
        b.recorded_at_utc,
        b.day,
        b.player_name,
        b.player_account_id_in_payload,
        b.player_hero,
        b.player_rank,
        b.player_rating,
        b.player_level,
        b.opponent_name,
        b.opponent_account_id,
        b.opponent_hero,
        b.opponent_rank,
        b.opponent_rating,
        b.opponent_level,
        b.result,
        b.replay_available,
        b.is_bundle_final_battle
      FROM battles AS b
      WHERE b.opponent_account_id = ?
        AND b.recorded_at_utc >= ?
      ORDER BY b.recorded_at_utc DESC, b.battle_id DESC
      LIMIT ?
    `,
  )
    .bind(playerAccountId, fromUtc, limit)
    .all<GhostBattleRow>();

  return json({
    battles: result.results.map((row) => ({
      battle_id: row.battle_id,
      recorded_at_utc: row.recorded_at_utc,
      day: row.day,
      player_name: row.player_name,
      player_account_id: row.player_account_id_in_payload,
      player_hero: row.player_hero,
      player_rank: row.player_rank,
      player_rating: row.player_rating,
      player_level: row.player_level,
      opponent_name: row.opponent_name,
      opponent_account_id: row.opponent_account_id,
      opponent_hero: row.opponent_hero,
      opponent_rank: row.opponent_rank,
      opponent_rating: row.opponent_rating,
      opponent_level: row.opponent_level,
      result: row.result,
      is_bundle_final_battle: row.is_bundle_final_battle === 1,
      replay: {
        available: row.replay_available === 1,
      },
    })),
  });
}
```

- [ ] **Step 2: Commit**

```bash
git add ModCFServerV3/src/features/v3/queryGhostBattles.ts
git commit -m "Read player_account_id from query string in queryGhostBattles"
```

### Task 8: Rewrite v3.ghostBattles.test.ts

**Files:**
- Modify: `ModCFServerV3/test/v3.ghostBattles.test.ts`

- [ ] **Step 1: Replace file content**

```typescript
import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import { insertV3Battle, resetTestState } from "./helpers/seed";

beforeEach(async () => {
  await resetTestState(env);
});

async function insertOpponentBattle(
  battleId: string,
  opponentAccountId: string,
  recordedAtUtc: string,
  overrides: Partial<Parameters<typeof insertV3Battle>[1]> = {},
): Promise<void> {
  await insertV3Battle(env.DB, {
    battleId,
    runId: `run-${battleId}`,
    playerAccountId: "remote-player",
    bundleId: `bundle-${battleId}`,
    recordedAtUtc,
    day: 8,
    playerName: "Remote",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1500,
    playerLevel: 10,
    opponentName: "Local",
    opponentAccountId,
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1510,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
    ...overrides,
  });
}

test("ghost-battles returns 400 when player_account_id is missing", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles?limit=5", { method: "GET" }),
    env as never,
  );

  expect(response.status).toBe(400);
  expect(await response.json()).toEqual({ error: "invalid_request" });
});

test("ghost-battles returns 400 when player_account_id is whitespace", async () => {
  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=%20%20&limit=5",
      { method: "GET" },
    ),
    env as never,
  );

  expect(response.status).toBe(400);
  expect(await response.json()).toEqual({ error: "invalid_request" });
});

test("ghost-battles returns battles within the lookback window", async () => {
  env.GHOST_QUERY_LOOKBACK_DAYS = "6";

  await insertOpponentBattle(
    "battle-recent",
    "player-account-001",
    new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString(),
  );
  await insertOpponentBattle(
    "battle-old",
    "player-account-001",
    new Date(Date.now() - 5 * 24 * 60 * 60 * 1000).toISOString(),
  );

  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=player-account-001&limit=200",
      { method: "GET" },
    ),
    env as never,
  );

  expect(response.status).toBe(200);
  const json = (await response.json()) as { battles: Array<{ battle_id: string }> };
  expect(json.battles.map((battle) => battle.battle_id)).toEqual([
    "battle-recent",
    "battle-old",
  ]);
});

test("ghost-battles honors the caller limit parameter after server clamping", async () => {
  await insertOpponentBattle(
    "battle-003",
    "player-account-001",
    new Date(Date.now() - 1 * 60 * 60 * 1000).toISOString(),
  );
  await insertOpponentBattle(
    "battle-002",
    "player-account-001",
    new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
  );
  await insertOpponentBattle(
    "battle-001",
    "player-account-001",
    new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(),
  );

  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=player-account-001&limit=1",
      { method: "GET" },
    ),
    env as never,
  );

  expect(response.status).toBe(200);
  const json = (await response.json()) as { battles: Array<{ battle_id: string }> };
  expect(json.battles.map((battle) => battle.battle_id)).toEqual(["battle-003"]);
});

test("ghost-battles only returns rows where opponent_account_id matches", async () => {
  await insertOpponentBattle(
    "battle-owned",
    "player-account-001",
    new Date().toISOString(),
  );
  await insertOpponentBattle(
    "battle-other",
    "someone-else",
    new Date(Date.now() - 60 * 1000).toISOString(),
  );

  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=player-account-001&limit=5",
      { method: "GET" },
    ),
    env as never,
  );

  expect(response.status).toBe(200);
  const json = (await response.json()) as { battles: Array<{ battle_id: string }> };
  expect(json.battles.map((battle) => battle.battle_id)).toEqual(["battle-owned"]);
});

test("ghost-battles includes the bundle-final battle marker", async () => {
  await insertOpponentBattle(
    "battle-final-loss",
    "player-account-001",
    new Date().toISOString(),
    { result: "Lost", isBundleFinalBattle: 1 },
  );

  const response = await worker.fetch(
    new Request(
      "https://example.com/ghost-battles?player_account_id=player-account-001&limit=5",
      { method: "GET" },
    ),
    env as never,
  );

  expect(response.status).toBe(200);
  const json = (await response.json()) as {
    battles: Array<{ battle_id: string; is_bundle_final_battle: boolean }>;
  };
  expect(json.battles).toMatchObject([
    {
      battle_id: "battle-final-loss",
      is_bundle_final_battle: true,
    },
  ]);
});
```

- [ ] **Step 2: Run the file**

Run: `cd ModCFServerV3 && npm test -- test/v3.ghostBattles.test.ts 2>&1 | tail -30`
Expected: PASS (6 tests).

- [ ] **Step 3: Commit**

```bash
git add ModCFServerV3/test/v3.ghostBattles.test.ts
git commit -m "Rewrite ghost-battles tests for query-string identity"
```

### Task 9: Rewrite createReplayLink.ts and downloadReplay.ts

**Files:**
- Modify: `ModCFServerV3/src/features/v3/createReplayLink.ts`
- Modify: `ModCFServerV3/src/features/v3/downloadReplay.ts`

- [ ] **Step 1: Replace createReplayLink.ts**

```typescript
import type { Env } from "../../env";
import { json } from "../../http/json";

export async function handleCreateReplayLink(
  request: Request,
  env: Env,
  battleId: string,
): Promise<Response> {
  const battleRow = await env.DB.prepare(
    `
      SELECT
        battle_id,
        player_account_id,
        opponent_account_id
      FROM battles
      WHERE battle_id = ?
    `,
  )
    .bind(battleId)
    .first<{
      battle_id: string;
      player_account_id: string | null;
      opponent_account_id: string | null;
    }>();
  if (!battleRow) {
    return json({ error: "battle_not_found" }, { status: 404 });
  }

  const tokenOwnerPlayerAccountId = battleRow.opponent_account_id;
  if (!tokenOwnerPlayerAccountId) {
    return json({ error: "replay_forbidden" }, { status: 403 });
  }

  const createdAtUtc = new Date().toISOString();
  const expiresAtUtc = new Date(Date.now() + 5 * 60 * 1000).toISOString();
  const token = `replay_${crypto.randomUUID().replace(/-/g, "")}`;

  await env.DB.prepare(
    `
      INSERT INTO replay_tokens (
        token,
        battle_id,
        requested_by_player_account_id,
        expires_at_utc,
        created_at_utc,
        used_at_utc,
        revoked_at_utc
      ) VALUES (?, ?, ?, ?, ?, ?, ?)
    `,
  )
    .bind(
      token,
      battleId,
      tokenOwnerPlayerAccountId,
      expiresAtUtc,
      createdAtUtc,
      null,
      null,
    )
    .run();

  return json({
    download_url: new URL(`/replays/${token}`, request.url).toString(),
    expires_at_utc: expiresAtUtc,
  });
}
```

- [ ] **Step 2: Replace downloadReplay.ts**

```typescript
import type { Env } from "../../env";
import { json } from "../../http/json";

type ReplayTokenRow = {
  token: string;
  battle_id: string;
  requested_by_player_account_id: string;
  expires_at_utc: string;
  used_at_utc: string | null;
  revoked_at_utc: string | null;
};

type BattleRow = { bundle_id: string };
type RunBundleRow = { object_key: string };

export async function handleDownloadReplay(
  request: Request,
  env: Env,
  token: string,
): Promise<Response> {
  const replayToken = await env.DB.prepare(
    `
      SELECT
        token,
        battle_id,
        requested_by_player_account_id,
        expires_at_utc,
        used_at_utc,
        revoked_at_utc
      FROM replay_tokens
      WHERE token = ?
    `,
  )
    .bind(token)
    .first<ReplayTokenRow>();
  if (!replayToken || replayToken.revoked_at_utc != null) {
    return json({ error: "replay_token_not_found" }, { status: 404 });
  }
  if (Date.parse(replayToken.expires_at_utc) < Date.now()) {
    return json({ error: "replay_token_expired" }, { status: 410 });
  }

  const battle = await env.DB.prepare(
    `SELECT bundle_id FROM battles WHERE battle_id = ?`,
  )
    .bind(replayToken.battle_id)
    .first<BattleRow>();
  if (!battle) {
    return json({ error: "battle_not_found" }, { status: 404 });
  }

  const runBundle = await env.DB.prepare(
    `SELECT object_key FROM run_bundles WHERE bundle_id = ?`,
  )
    .bind(battle.bundle_id)
    .first<RunBundleRow>();
  if (!runBundle?.object_key) {
    return json({ error: "artifact_expired" }, { status: 410 });
  }

  const object = await env.RUN_BUNDLE_BUCKET.get(runBundle.object_key);
  if (!object) {
    return json({ error: "artifact_expired" }, { status: 410 });
  }

  if (replayToken.used_at_utc == null) {
    await env.DB.prepare(
      `UPDATE replay_tokens SET used_at_utc = ? WHERE token = ?`,
    )
      .bind(new Date().toISOString(), replayToken.token)
      .run();
  }

  const headers = new Headers({
    "content-type": object.httpMetadata?.contentType ?? "application/octet-stream",
  });
  if (typeof object.size === "number") {
    headers.set("content-length", String(object.size));
  }

  return new Response(object.body, { status: 200, headers });
}
```

- [ ] **Step 3: Commit**

```bash
git add ModCFServerV3/src/features/v3/createReplayLink.ts ModCFServerV3/src/features/v3/downloadReplay.ts
git commit -m "Drop conditional auth from replay link and download handlers"
```

### Task 10: Rewrite v3.replays.test.ts

**Files:**
- Modify: `ModCFServerV3/test/v3.replays.test.ts`

- [ ] **Step 1: Replace file content**

```typescript
import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import {
  insertReplayToken,
  insertRunBundle,
  insertV3Battle,
  resetTestState,
  selectFirst,
} from "./helpers/seed";

const JsonEncoder = new TextEncoder();

async function insertReplayArtifact(args: {
  battleId: string;
  bundleId: string;
  runId: string;
  objectKey: string;
  opponentAccountId: string;
  bodyText?: string;
}): Promise<void> {
  const nowUtc = new Date().toISOString();

  await insertV3Battle(env.DB, {
    battleId: args.battleId,
    runId: args.runId,
    playerAccountId: "remote-player",
    bundleId: args.bundleId,
    recordedAtUtc: nowUtc,
    day: 9,
    playerName: "Remote",
    playerAccountIdInPayload: "remote-player",
    playerHero: "HeroA",
    playerRank: "Gold",
    playerRating: 1600,
    playerLevel: 10,
    opponentName: "Local",
    opponentAccountId: args.opponentAccountId,
    opponentHero: "HeroB",
    opponentRank: "Gold",
    opponentRating: 1610,
    opponentLevel: 11,
    result: "Won",
    replayAvailable: 1,
    updatedAtUtc: nowUtc,
  });
  await insertRunBundle(env.DB, {
    bundleId: args.bundleId,
    playerAccountId: "remote-player",
    runId: args.runId,
    payloadHash: `${args.bundleId}-hash`,
    schemaVersion: 3,
    objectKey: args.objectKey,
    codec: "application/json",
    sizeBytes: 12,
    submittedAtUtc: nowUtc,
    createdAtUtc: nowUtc,
  });

  if (args.bodyText != null) {
    await env.RUN_BUNDLE_BUCKET.put(
      args.objectKey,
      JsonEncoder.encode(args.bodyText),
      { httpMetadata: { contentType: "application/json" } },
    );
  }
}

beforeEach(async () => {
  await resetTestState(env);
});

test("replay-link returns 404 when battle does not exist", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/missing-battle/replay-link", {
      method: "POST",
    }),
    env as never,
  );

  expect(response.status).toBe(404);
  expect(await response.json()).toEqual({ error: "battle_not_found" });
});

test("replay-link returns 403 when battle has no opponent account id", async () => {
  await insertV3Battle(env.DB, {
    battleId: "battle-no-opponent",
    runId: "run-no-opponent",
    playerAccountId: "remote-player",
    bundleId: "bundle-no-opponent",
    recordedAtUtc: new Date().toISOString(),
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-no-opponent/replay-link", {
      method: "POST",
    }),
    env as never,
  );

  expect(response.status).toBe(403);
  expect(await response.json()).toEqual({ error: "replay_forbidden" });
});

test("replay-link returns download metadata for any battle with an opponent", async () => {
  await insertV3Battle(env.DB, {
    battleId: "battle-public-link",
    runId: "run-public-link",
    playerAccountId: "remote-player",
    bundleId: "bundle-public-link",
    recordedAtUtc: new Date().toISOString(),
    opponentAccountId: "player-account-001",
    replayAvailable: 1,
    updatedAtUtc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/ghost-battles/battle-public-link/replay-link", {
      method: "POST",
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  const payload = (await response.json()) as {
    download_url: string;
    expires_at_utc: string;
  };
  expect(payload.download_url).toMatch(/^https:\/\/example\.com\/replays\/replay_/);
  expect(payload.expires_at_utc).toMatch(/^\d{4}-\d{2}-\d{2}T/);

  const replayToken = await selectFirst<{
    requested_by_player_account_id: string;
    expires_at_utc: string;
  }>(
    env.DB,
    `
      SELECT requested_by_player_account_id, expires_at_utc
      FROM replay_tokens
      WHERE token = ?
    `,
    payload.download_url.split("/").pop() ?? "",
  );
  expect(replayToken?.requested_by_player_account_id).toBe("player-account-001");
});

test("download replay accepts a valid short-lived token", async () => {
  await insertReplayToken(env.DB, {
    token: "token-valid",
    battleId: "battle-owned",
    requestedByPlayerAccountId: "player-account-001",
    expiresAtUtc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    createdAtUtc: new Date().toISOString(),
  });
  await insertReplayArtifact({
    battleId: "battle-owned",
    bundleId: "bundle-owned",
    runId: "run-owned",
    objectKey: "run-bundles/remote-player/run-owned/payload-hash.mpack.gz",
    opponentAccountId: "player-account-001",
    bodyText: '{"battle_id":"battle-owned"}',
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-valid", { method: "GET" }),
    env as never,
  );

  expect(response.status).toBe(200);
  expect(await response.text()).toBe('{"battle_id":"battle-owned"}');
});

test("download replay returns artifact_expired when artifact is missing", async () => {
  await insertReplayToken(env.DB, {
    token: "token-expired-artifact",
    battleId: "battle-expired",
    requestedByPlayerAccountId: "player-account-001",
    expiresAtUtc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    createdAtUtc: new Date().toISOString(),
  });
  await insertReplayArtifact({
    battleId: "battle-expired",
    bundleId: "bundle-expired",
    runId: "run-expired",
    objectKey: "run-bundles/remote-player/run-expired/payload-hash-expired.mpack.gz",
    opponentAccountId: "player-account-001",
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-expired-artifact", { method: "GET" }),
    env as never,
  );

  expect(response.status).toBe(410);
  expect(await response.json()).toEqual({ error: "artifact_expired" });
});

test("download replay returns 410 when token has expired", async () => {
  await insertReplayToken(env.DB, {
    token: "token-too-old",
    battleId: "battle-irrelevant",
    requestedByPlayerAccountId: "player-account-001",
    expiresAtUtc: new Date(Date.now() - 60 * 1000).toISOString(),
    createdAtUtc: new Date(Date.now() - 10 * 60 * 1000).toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-too-old", { method: "GET" }),
    env as never,
  );

  expect(response.status).toBe(410);
  expect(await response.json()).toEqual({ error: "replay_token_expired" });
});

test("download replay returns 404 when token has been revoked", async () => {
  await insertReplayToken(env.DB, {
    token: "token-revoked",
    battleId: "battle-irrelevant",
    requestedByPlayerAccountId: "player-account-001",
    expiresAtUtc: new Date(Date.now() + 5 * 60 * 1000).toISOString(),
    createdAtUtc: new Date().toISOString(),
    revokedAtUtc: new Date().toISOString(),
  });

  const response = await worker.fetch(
    new Request("https://example.com/replays/token-revoked", { method: "GET" }),
    env as never,
  );

  expect(response.status).toBe(404);
  expect(await response.json()).toEqual({ error: "replay_token_not_found" });
});
```

- [ ] **Step 2: Run the file**

Run: `cd ModCFServerV3 && npm test -- test/v3.replays.test.ts 2>&1 | tail -30`
Expected: PASS (7 tests).

- [ ] **Step 3: Commit**

```bash
git add ModCFServerV3/test/v3.replays.test.ts
git commit -m "Rewrite replay tests without bearer auth"
```

### Task 11: Update src/config/v3.ts and wrangler.toml

**Files:**
- Modify: `ModCFServerV3/src/config/v3.ts`
- Modify: `ModCFServerV3/wrangler.toml`

- [ ] **Step 1: Trim config/v3.ts to required vars only**

```typescript
import type { Env } from "../env";

type RequiredNumericConfigKey =
  | "GHOST_QUERY_LOOKBACK_DAYS"
  | "RUN_BUNDLE_RETENTION_DAYS";

function requireNonNegativeInteger(env: Env, key: RequiredNumericConfigKey): number {
  const raw = env[key]?.trim();
  if (!raw) {
    throw new Error(`Missing required V3 config var: ${key}`);
  }

  const parsed = Number.parseInt(raw, 10);
  if (!Number.isSafeInteger(parsed) || parsed < 0 || String(parsed) !== raw) {
    throw new Error(`Invalid required V3 config var: ${key}=${raw}`);
  }

  return parsed;
}

export function getGhostQueryLookbackDays(env: Env): number {
  return requireNonNegativeInteger(env, "GHOST_QUERY_LOOKBACK_DAYS");
}

export function getRunBundleRetentionDays(env: Env): number {
  return requireNonNegativeInteger(env, "RUN_BUNDLE_RETENTION_DAYS");
}
```

- [ ] **Step 2: Trim wrangler.toml [vars]**

Replace lines 6–12 of `wrangler.toml`:

```
[vars]
GHOST_QUERY_LOOKBACK_DAYS = "5"
RUN_BUNDLE_RETENTION_DAYS = "5"
```

- [ ] **Step 3: Commit**

```bash
git add ModCFServerV3/src/config/v3.ts ModCFServerV3/wrangler.toml
git commit -m "Drop ALLOW_UNAUTHENTICATED_REPLAY_* config flags"
```

### Task 12: Delete /activate, /login, /logout endpoints and supporting modules

**Files:**
- Delete: `ModCFServerV3/src/features/v3/activate.ts`
- Delete: `ModCFServerV3/src/features/v3/login.ts`
- Delete: `ModCFServerV3/src/features/v3/logout.ts`
- Delete: `ModCFServerV3/src/features/v3/requireBearerAuth.ts`
- Delete: `ModCFServerV3/src/crypto/password.ts`
- Delete: `ModCFServerV3/src/token/generate.ts`
- Modify: `ModCFServerV3/src/index.ts`

- [ ] **Step 1: Remove the files**

```bash
git rm ModCFServerV3/src/features/v3/activate.ts \
       ModCFServerV3/src/features/v3/login.ts \
       ModCFServerV3/src/features/v3/logout.ts \
       ModCFServerV3/src/features/v3/requireBearerAuth.ts \
       ModCFServerV3/src/crypto/password.ts \
       ModCFServerV3/src/token/generate.ts
rmdir ModCFServerV3/src/token
```

- [ ] **Step 2: Drop route entries from src/index.ts**

```typescript
import type { Env } from "./env";
import { handleCreateReplayLink } from "./features/v3/createReplayLink";
import { handleDownloadReplay } from "./features/v3/downloadReplay";
import { handleQueryGhostBattles } from "./features/v3/queryGhostBattles";
import { handleUploadRunBundle } from "./features/v3/uploadRunBundle";
import { preflight, withCors } from "./http/cors";
import { json } from "./http/json";

type StaticRoute = {
  method: string;
  path: string;
  handle: (request: Request, env: Env) => Promise<Response> | Response;
};

const StaticRoutes: StaticRoute[] = [
  { method: "GET", path: "/health", handle: () => json({ ok: true }) },
  { method: "POST", path: "/run-bundles", handle: handleUploadRunBundle },
  { method: "GET", path: "/ghost-battles", handle: handleQueryGhostBattles },
];

function findStaticRoute(method: string, path: string): StaticRoute | undefined {
  return StaticRoutes.find((route) => route.method === method && route.path === path);
}

export default {
  async fetch(request: Request, _env: Env): Promise<Response> {
    if (request.method === "OPTIONS") {
      return preflight(request);
    }

    try {
      const url = new URL(request.url);
      const staticRoute = findStaticRoute(request.method, url.pathname);
      if (staticRoute) {
        return withCors(request, await staticRoute.handle(request, _env));
      }

      const replayLinkMatch = url.pathname.match(/^\/ghost-battles\/([^/]+)\/replay-link$/);
      if (request.method === "POST" && replayLinkMatch) {
        return withCors(
          request,
          await handleCreateReplayLink(
            request,
            _env,
            decodeURIComponent(replayLinkMatch[1] ?? ""),
          ),
        );
      }

      const replayDownloadMatch = url.pathname.match(/^\/replays\/([^/]+)$/);
      if (request.method === "GET" && replayDownloadMatch) {
        return withCors(
          request,
          await handleDownloadReplay(
            request,
            _env,
            decodeURIComponent(replayDownloadMatch[1] ?? ""),
          ),
        );
      }

      return withCors(request, json({ error: "not_found" }, { status: 404 }));
    } catch (error) {
      if (error instanceof Response) {
        return withCors(request, error);
      }

      throw error;
    }
  },
};
```

- [ ] **Step 3: Commit**

```bash
git add ModCFServerV3/src/index.ts ModCFServerV3/src/features/v3 ModCFServerV3/src/crypto ModCFServerV3/src/token
git commit -m "Delete activate, login, logout, requireBearerAuth, password, token modules"
```

### Task 13: Trim src/types/db.ts

**Files:**
- Modify: `ModCFServerV3/src/types/db.ts`

- [ ] **Step 1: Replace contents**

`V3InstallationRow`, `V3InstallationSessionRow`, `V3InstallationObservationRow` were already orphan after migration 0002 dropped those tables, but the type file kept them. Drop everything that refers to deleted/auth concepts. With `users` and `tokens` gone, this file ends up empty of useful types — delete it.

```bash
git rm ModCFServerV3/src/types/db.ts
rmdir ModCFServerV3/src/types
```

If anything imports from `src/types/db`, the TypeScript compiler will surface it in the next check; expected to be unused.

- [ ] **Step 2: Verify nothing imports the deleted types**

Run: `git grep -n "types/db" -- ModCFServerV3/`
Expected: no matches.

- [ ] **Step 3: Commit**

```bash
git add ModCFServerV3
git commit -m "Drop dead users and installation row types"
```

### Task 14: Trim CORS allowed headers

**Files:**
- Modify: `ModCFServerV3/src/http/cors.ts`

- [ ] **Step 1: Drop `authorization` and `x-bpp-installation-id` from `ALLOWED_HEADERS`**

Replace lines 1–9 of the file:

```typescript
const ALLOWED_METHODS = "GET, POST, OPTIONS";
const ALLOWED_HEADERS = [
  "content-type",
  "x-bpp-timestamp",
  "x-bpp-content-sha256",
  "x-bpp-signature",
].join(", ");
```

(The remaining `x-bpp-*` headers are unrelated to login/installation — they belong to a separate signature flow that is out of scope.)

- [ ] **Step 2: Commit**

```bash
git add ModCFServerV3/src/http/cors.ts
git commit -m "Remove authorization and x-bpp-installation-id from CORS allow-list"
```

### Task 15: Delete obsolete server tests

**Files:**
- Delete: `ModCFServerV3/test/v3.activate.test.ts`
- Delete: `ModCFServerV3/test/v3.login.test.ts`
- Delete: `ModCFServerV3/test/v3.logout.test.ts`
- Delete: `ModCFServerV3/test/v3.requireBearerAuth.test.ts`
- Delete: `ModCFServerV3/test/token.generate.test.ts`

- [ ] **Step 1: Remove the files**

```bash
git rm ModCFServerV3/test/v3.activate.test.ts \
       ModCFServerV3/test/v3.login.test.ts \
       ModCFServerV3/test/v3.logout.test.ts \
       ModCFServerV3/test/v3.requireBearerAuth.test.ts \
       ModCFServerV3/test/token.generate.test.ts
```

- [ ] **Step 2: Commit**

```bash
git commit -m "Delete login, activate, logout, bearer, and token tests"
```

### Task 16: Update test/index.test.ts and test/schema.test.ts

**Files:**
- Modify: `ModCFServerV3/test/index.test.ts`
- Modify: `ModCFServerV3/test/schema.test.ts`

- [ ] **Step 1: Rewrite test/index.test.ts**

Drop the `/activate` test, drop `authorization` and `x-bpp-installation-id` from CORS expectations:

```typescript
import { beforeEach, expect, test } from "vitest";
import { env } from "cloudflare:test";

import worker from "../src/index";
import { resetTestState } from "./helpers/seed";

const EXPECTED_ALLOWED_HEADERS =
  "content-type, x-bpp-timestamp, x-bpp-content-sha256, x-bpp-signature";

beforeEach(async () => {
  await resetTestState(env);
});

test("responds to the health endpoint", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/health", {
      method: "GET",
      headers: { origin: "https://frontend.example.com" },
    }),
    env as never,
  );

  expect(response.status).toBe(200);
  expect(await response.json()).toEqual({ ok: true });
  expect(response.headers.get("access-control-allow-origin")).toBe(
    "https://frontend.example.com",
  );
  expect(response.headers.get("access-control-allow-headers")).toBe(
    EXPECTED_ALLOWED_HEADERS,
  );
});

test("returns not_found for unsupported routes", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/nope", { method: "POST" }),
    env as never,
  );

  expect(response.status).toBe(404);
  expect(await response.json()).toEqual({ error: "not_found" });
});

test("activate route is gone", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/activate", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: "{}",
    }),
    env as never,
  );

  expect(response.status).toBe(404);
  expect(await response.json()).toEqual({ error: "not_found" });
});

test("login route is gone", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/login", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: "{}",
    }),
    env as never,
  );

  expect(response.status).toBe(404);
});

test("responds to CORS preflight for browser requests", async () => {
  const response = await worker.fetch(
    new Request("https://example.com/run-bundles", {
      method: "OPTIONS",
      headers: {
        origin: "https://frontend.example.com",
        "access-control-request-method": "POST",
        "access-control-request-headers": "content-type",
      },
    }),
    env as never,
  );

  expect(response.status).toBe(204);
  expect(response.headers.get("access-control-allow-origin")).toBe(
    "https://frontend.example.com",
  );
  expect(response.headers.get("access-control-allow-methods")).toBe(
    "GET, POST, OPTIONS",
  );
  expect(response.headers.get("access-control-allow-headers")).toBe(
    EXPECTED_ALLOWED_HEADERS,
  );
});
```

- [ ] **Step 2: Rewrite test/schema.test.ts**

Drop tests that assert deleted tables / columns. Add tests that assert the new migrations contain the expected SQL.

```typescript
import { expect, test } from "vitest";

import dropAuthTablesSql from "../migrations/0009_drop_auth_tables.sql?raw";
import dropInstallationIdSql from "../migrations/0010_drop_installation_id.sql?raw";
import createSeenPlayerAccountsSql from "../migrations/0011_create_seen_player_accounts.sql?raw";
import runsEndedAtIndexSql from "../migrations/0005_runs_ended_at_index.sql?raw";
import runBundlesCreatedAtIndexSql from "../migrations/0006_run_bundles_created_at_index.sql?raw";
import runsUpdatedAtIndexSql from "../migrations/0007_runs_updated_at_index.sql?raw";
import battlesBundleFinalFlagSql from "../migrations/0008_battles_bundle_final_flag.sql?raw";
import initialSchemaSql from "../migrations/0001_initial_schema.sql?raw";

function getTableSection(sql: string, tableName: string): string {
  const section = sql.match(
    new RegExp(`CREATE TABLE(?: IF NOT EXISTS)? ${tableName} \\(([\\s\\S]*?)\\);`),
  )?.[1];
  expect(section).toBeTruthy();
  return section!;
}

test("initial migration defines the V3 projection tables", () => {
  for (const tableName of ["run_bundles", "runs", "battles", "replay_tokens"]) {
    expect(getTableSection(initialSchemaSql, tableName)).toBeTruthy();
  }
});

test("runs ended_at index migration adds the mirror sync index", () => {
  expect(runsEndedAtIndexSql).toMatch(
    /CREATE INDEX IF NOT EXISTS idx_runs_ended_at\s+ON runs \(ended_at_utc, run_id\);/,
  );
});

test("run_bundles created_at index migration adds the server-clock mirror index", () => {
  expect(runBundlesCreatedAtIndexSql).toMatch(
    /CREATE INDEX IF NOT EXISTS idx_run_bundles_created_at\s+ON run_bundles \(created_at_utc, bundle_id\);/,
  );
});

test("runs updated_at index migration adds the server-clock mirror index", () => {
  expect(runsUpdatedAtIndexSql).toMatch(
    /CREATE INDEX IF NOT EXISTS idx_runs_updated_at\s+ON runs \(updated_at_utc, run_id\);/,
  );
});

test("battles bundle-final migration adds the ghost display flag to the covering index", () => {
  expect(battlesBundleFinalFlagSql).toContain(
    "ADD COLUMN is_bundle_final_battle INTEGER NOT NULL DEFAULT 0",
  );
  expect(battlesBundleFinalFlagSql).toContain("idx_battles_opponent_recorded_covering");
});

test("0009 drops auth tables in dependency order", () => {
  expect(dropAuthTablesSql).toMatch(/DROP TABLE IF EXISTS tokens;/);
  expect(dropAuthTablesSql).toMatch(/DROP TABLE IF EXISTS users;/);
  expect(dropAuthTablesSql.indexOf("DROP TABLE IF EXISTS tokens")).toBeLessThan(
    dropAuthTablesSql.indexOf("DROP TABLE IF EXISTS users"),
  );
});

test("0010 drops installation_id and rebuilds run_bundles without UNIQUE", () => {
  expect(dropInstallationIdSql).toMatch(/ALTER TABLE runs\s+DROP COLUMN installation_id;/);
  expect(dropInstallationIdSql).toMatch(/ALTER TABLE battles\s+DROP COLUMN installation_id;/);
  expect(dropInstallationIdSql).toMatch(/CREATE TABLE run_bundles_new \(/);
  expect(dropInstallationIdSql).toMatch(/ALTER TABLE run_bundles_new RENAME TO run_bundles;/);
  expect(dropInstallationIdSql).not.toMatch(/UNIQUE \(installation_id/);
});

test("0011 creates seen_player_accounts and backfills uploaders", () => {
  expect(createSeenPlayerAccountsSql).toMatch(
    /CREATE TABLE seen_player_accounts \(/,
  );
  expect(createSeenPlayerAccountsSql).toMatch(
    /INSERT OR IGNORE INTO seen_player_accounts/,
  );
  expect(createSeenPlayerAccountsSql).toMatch(/FROM run_bundles/);
});
```

- [ ] **Step 3: Run the full server test suite**

Run: `cd ModCFServerV3 && npm run check && npm test 2>&1 | tail -40`
Expected: tsc passes, all vitest tests PASS.

- [ ] **Step 4: Commit**

```bash
git add ModCFServerV3/test/index.test.ts ModCFServerV3/test/schema.test.ts
git commit -m "Refresh index and schema tests for new server contract"
```

---

## Phase 4 — Client deletions and simplifications

### Task 17: Strip ShouldReRegister from V3HttpFailureClassifier

**Files:**
- Modify: `Game/Online/V3HttpFailureClassifier.cs`

- [ ] **Step 1: Replace the file**

```csharp
#nullable enable
namespace BazaarPlusPlus.Game.Online;

internal readonly struct V3HttpFailureDecision
{
    public V3HttpFailureDecision(bool shouldFallback)
    {
        ShouldFallback = shouldFallback;
    }

    public bool ShouldFallback { get; }
}

internal static class V3HttpFailureClassifier
{
    public static V3HttpFailureDecision Classify(int statusCode) =>
        new(shouldFallback: statusCode >= 500 || statusCode == 429);
}
```

- [ ] **Step 2: Commit**

```bash
git add Game/Online/V3HttpFailureClassifier.cs
git commit -m "Drop ShouldReRegister from V3 HTTP failure classifier"
```

### Task 18: Drop bearerToken parameters from GhostBattleApiClient

**Files:**
- Modify: `Game/HistoryPanel/Ghost/GhostBattleApiClient.cs`

- [ ] **Step 1: Apply the targeted edits**

1. Rename `CreateBearerRequest` to `CreateRequest` and remove the bearer header logic:

```csharp
private static HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
{
    return new HttpRequestMessage(method, endpoint);
}
```

2. In `QueryAgainstMeAsync`, drop the `string bearerToken` parameter and update the body:

```csharp
public async Task<GhostBattleApiResult> QueryAgainstMeAsync(
    string playerAccountId,
    int limit,
    CancellationToken cancellationToken
)
{
    try
    {
        if (string.IsNullOrWhiteSpace(playerAccountId))
        {
            return GhostBattleApiResult.Failure(
                "player_account_id_required",
                shouldFallback: false
            );
        }

        var endpoint = new UriBuilder(_routes.QueryGhostBattles)
        {
            Query =
                $"player_account_id={Uri.EscapeDataString(playerAccountId.Trim())}&limit={Math.Clamp(limit, 1, 200)}",
        }.Uri.ToString();
        using var request = CreateRequest(HttpMethod.Get, endpoint);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var decision = V3HttpFailureClassifier.Classify(statusCode);
            return GhostBattleApiResult.Failure(
                V3ErrorFormatter.FormatHttpFailure(statusCode, responseBody),
                shouldFallback: decision.ShouldFallback
            );
        }

        // (parsing code unchanged)
        var payload = JObject.Parse(responseBody);
        var battlesToken = payload["battles"] as JArray;
        var importRecords = new List<GhostBattleImportRecord>();
        if (battlesToken != null)
        {
            foreach (var battleChild in battlesToken)
            {
                if (battleChild is not JObject battleToken)
                    continue;

                var importRecord = TryParseBattle(battleToken);
                if (importRecord != null)
                    importRecords.Add(importRecord);
            }
        }

        return GhostBattleApiResult.Success(importRecords);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        return GhostBattleApiResult.Failure(
            V3ErrorFormatter.Truncate(ex.Message),
            shouldFallback: true
        );
    }
}
```

3. In `RequestReplayDownloadLinkAsync`, drop the `string bearerToken` param:

```csharp
public async Task<GhostBattleReplayDownloadLinkResult> RequestReplayDownloadLinkAsync(
    string battleId,
    CancellationToken cancellationToken
)
{
    try
    {
        var endpoint = _routes.CreateReplayLink(battleId);
        using var request = CreateRequest(HttpMethod.Post, endpoint);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var decision = V3HttpFailureClassifier.Classify(statusCode);
            return GhostBattleReplayDownloadLinkResult.Failure(
                V3ErrorFormatter.FormatHttpFailure(statusCode, responseBody),
                shouldFallback: decision.ShouldFallback
            );
        }

        var payload = JObject.Parse(responseBody);
        var downloadUrl = payload["download_url"]?.Value<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return GhostBattleReplayDownloadLinkResult.Failure(
                "download_url_missing",
                shouldFallback: false
            );
        }

        return GhostBattleReplayDownloadLinkResult.Success(downloadUrl);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        return GhostBattleReplayDownloadLinkResult.Failure(
            V3ErrorFormatter.Truncate(ex.Message),
            shouldFallback: true
        );
    }
}
```

4. In `DownloadReplayPayloadAsync`, drop `bearerToken`:

```csharp
public async Task<GhostBattleReplayPayloadResult> DownloadReplayPayloadAsync(
    string battleId,
    string downloadUrl,
    CancellationToken cancellationToken
)
{
    // ... body unchanged except the CreateBearerRequest call:
    using var request = CreateRequest(HttpMethod.Get, downloadUrl);
    // ... rest of method as today, with no bearer reference
}
```

5. Drop `ShouldReRegister` from the result struct ctors. Replace the result-struct definitions with two-arg variants:

```csharp
internal readonly struct GhostBattleApiResult
{
    private GhostBattleApiResult(
        bool succeeded,
        IReadOnlyList<GhostBattleImportRecord>? battles,
        string? error,
        bool shouldFallback
    )
    {
        Succeeded = succeeded;
        Battles = battles ?? Array.Empty<GhostBattleImportRecord>();
        Error = error;
        ShouldFallback = shouldFallback;
    }

    public bool Succeeded { get; }
    public IReadOnlyList<GhostBattleImportRecord> Battles { get; }
    public string? Error { get; }
    public bool ShouldFallback { get; }

    public static GhostBattleApiResult Success(IReadOnlyList<GhostBattleImportRecord> battles) =>
        new(true, battles, null, false);

    public static GhostBattleApiResult Failure(string error, bool shouldFallback) =>
        new(false, null, error, shouldFallback);
}

internal readonly struct GhostBattleReplayDownloadLinkResult
{
    private GhostBattleReplayDownloadLinkResult(
        bool succeeded,
        string? downloadUrl,
        string? error,
        bool shouldFallback
    )
    {
        Succeeded = succeeded;
        DownloadUrl = downloadUrl;
        Error = error;
        ShouldFallback = shouldFallback;
    }

    public bool Succeeded { get; }
    public string? DownloadUrl { get; }
    public string? Error { get; }
    public bool ShouldFallback { get; }

    public static GhostBattleReplayDownloadLinkResult Success(string downloadUrl) =>
        new(true, downloadUrl, null, false);

    public static GhostBattleReplayDownloadLinkResult Failure(string error, bool shouldFallback) =>
        new(false, null, error, shouldFallback);
}
```

`GhostBattleReplayPayloadResult` already had no auth fields; leave it.

- [ ] **Step 2: Commit**

```bash
git add Game/HistoryPanel/Ghost/GhostBattleApiClient.cs
git commit -m "Drop bearer token plumbing from GhostBattleApiClient"
```

### Task 19: Simplify ModOnlineClient.cs

**Files:**
- Modify: `Game/Online/ModOnlineClient.cs`

- [ ] **Step 1: Replace contents**

```csharp
#nullable enable
using System;
using System.Net.Http;

namespace BazaarPlusPlus.Game.Online;

internal sealed class ModOnlineClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly V3Routes _routes;

    public ModOnlineClient(HttpClient httpClient, V3Routes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public HttpClient HttpClient => _httpClient;

    public V3Routes Routes => _routes;

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Game/Online/ModOnlineClient.cs
git commit -m "Strip BearerState plumbing from ModOnlineClient"
```

### Task 20: Simplify GhostBattleSyncService.cs

**Files:**
- Modify: `Game/HistoryPanel/Ghost/GhostBattleSyncService.cs`

The service needs a `player_account_id`. Read it from `BppClientCacheBridge.TryGetProfileAccountId()` directly, matching the pattern already used by `RunBundleUploadService`.

- [ ] **Step 1: Replace contents**

```csharp
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Online;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattleSyncService : IDisposable
{
    private const int MaxSyncBattleLimit = 200;

    private readonly HistoryPanelRepository _repository;
    private readonly ModOnlineClient _onlineClient;

    public GhostBattleSyncService(
        HistoryPanelRepository repository,
        ModOnlineClient onlineClient
    )
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _onlineClient = onlineClient ?? throw new ArgumentNullException(nameof(onlineClient));
    }

    public async Task<GhostBattleSyncResult> SyncRecentBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        var playerAccountId = ResolvePlayerAccountId();
        if (string.IsNullOrWhiteSpace(playerAccountId))
            return GhostBattleSyncResult.Failure("player_account_id_unavailable");

        var apiClient = new GhostBattleApiClient(_onlineClient.HttpClient, _onlineClient.Routes);
        var syncStartedAtUtc = DateTimeOffset.UtcNow;
        var queryResult = await apiClient.QueryAgainstMeAsync(
            playerAccountId!,
            MaxSyncBattleLimit,
            cancellationToken
        );
        if (!queryResult.Succeeded)
        {
            return GhostBattleSyncResult.Failure(queryResult.Error ?? "ghost_sync_failed");
        }

        _repository.UpsertGhostBattles(playerAccountId!, queryResult.Battles);
        _repository.MarkOldUndownloadedGhostBattlesDeleted(syncStartedAtUtc);
        if (ShouldAdvanceCheckpoint(queryResult.Battles.Count, MaxSyncBattleLimit))
            _repository.SaveGhostSyncCheckpointUtc(playerAccountId!, syncStartedAtUtc);
        return GhostBattleSyncResult.Success(queryResult.Battles.Count);
    }

    public async Task<GhostBattleReplayDownloadResult> DownloadReplayAsync(
        string battleId,
        string replayDirectoryPath,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return GhostBattleReplayDownloadResult.Failure("battle_id_required");
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return GhostBattleReplayDownloadResult.Failure("replay_directory_required");

        var apiClient = new GhostBattleApiClient(_onlineClient.HttpClient, _onlineClient.Routes);
        var linkResult = await apiClient.RequestReplayDownloadLinkAsync(
            battleId,
            cancellationToken
        );
        if (!linkResult.Succeeded)
        {
            return GhostBattleReplayDownloadResult.Failure(
                linkResult.Error ?? "ghost_replay_link_failed"
            );
        }

        var payloadResult = await apiClient.DownloadReplayPayloadAsync(
            battleId,
            linkResult.DownloadUrl!,
            cancellationToken
        );
        if (!payloadResult.Succeeded || payloadResult.Payload?.ReplayPayload == null)
        {
            return GhostBattleReplayDownloadResult.Failure(
                payloadResult.Error ?? "ghost_replay_payload_failed"
            );
        }
        if (
            !string.Equals(
                payloadResult.Payload.ReplayPayload.BattleId,
                battleId,
                StringComparison.Ordinal
            )
        )
        {
            return GhostBattleReplayDownloadResult.Failure("ghost_replay_battle_id_mismatch");
        }

        var payloadStore = new GhostBattlePayloadStore(
            BuildGhostBattlePayloadDirectoryPath(replayDirectoryPath)
        );
        payloadStore.Save(payloadResult.Payload);
        _repository.MarkGhostReplayDownloaded(battleId);
        return GhostBattleReplayDownloadResult.Success();
    }

    public void Dispose() { }

    private static string? ResolvePlayerAccountId()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileAccountId()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldAdvanceCheckpoint(int importedCount, int limit)
    {
        return importedCount < limit;
    }

    private static string BuildGhostBattlePayloadDirectoryPath(string replayDirectoryPath)
    {
        var parentDirectory = System.IO.Path.GetDirectoryName(replayDirectoryPath);
        return string.IsNullOrWhiteSpace(parentDirectory)
            ? System.IO.Path.Combine(replayDirectoryPath, "GhostBattlePayloads")
            : System.IO.Path.Combine(parentDirectory, "GhostBattlePayloads");
    }
}

internal readonly struct GhostBattleSyncResult
{
    private GhostBattleSyncResult(bool succeeded, int importedCount, string? error)
    {
        Succeeded = succeeded;
        ImportedCount = importedCount;
        Error = error;
    }

    public bool Succeeded { get; }
    public int ImportedCount { get; }
    public string? Error { get; }

    public static GhostBattleSyncResult Success(int importedCount) =>
        new(true, importedCount, null);

    public static GhostBattleSyncResult Failure(string error) => new(false, 0, error);
}

internal readonly struct GhostBattleReplayDownloadResult
{
    private GhostBattleReplayDownloadResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }
    public string? Error { get; }

    public static GhostBattleReplayDownloadResult Success() => new(true, null);

    public static GhostBattleReplayDownloadResult Failure(string error) => new(false, error);
}
```

- [ ] **Step 2: Commit**

```bash
git add Game/HistoryPanel/Ghost/GhostBattleSyncService.cs
git commit -m "Drop AuthStore from GhostBattleSyncService and read account from BppClientCacheBridge"
```

### Task 21: Simplify HistoryPanelFactory.cs

**Files:**
- Modify: `Game/HistoryPanel/HistoryPanelFactory.cs`

- [ ] **Step 1: Replace contents**

```csharp
#nullable enable
using System;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.Online;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelFactory
{
    public static HistoryPanelDependencies Create(
        IHistoryPanelRuntime runtime,
        ModOnlineClient onlineClient
    )
    {
        if (runtime == null)
            throw new ArgumentNullException(nameof(runtime));
        if (onlineClient == null)
            throw new ArgumentNullException(nameof(onlineClient));

        HistoryPanelRepository? repository = null;
        if (!string.IsNullOrWhiteSpace(runtime.RunLogDatabasePath))
            repository = new HistoryPanelRepository(runtime.RunLogDatabasePath);

        var ghostSyncService = CreateGhostSyncService(repository, onlineClient);
        var dataService = new HistoryPanelDataService(repository, ghostSyncService);
        var replayService = new HistoryPanelReplayService(
            runtime.CombatReplayRuntimeAccessor,
            () => runtime.CombatReplayDirectoryPath,
            ghostSyncService
        );
        return new HistoryPanelDependencies(runtime, dataService, replayService, ghostSyncService);
    }

    private static GhostBattleSyncService? CreateGhostSyncService(
        HistoryPanelRepository? repository,
        ModOnlineClient onlineClient
    )
    {
        if (repository == null)
            return null;

        return new GhostBattleSyncService(repository, onlineClient);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Game/HistoryPanel/HistoryPanelFactory.cs
git commit -m "Drop AuthStore parameter from HistoryPanelFactory"
```

### Task 22: Simplify PlayerObservationController.cs

**Files:**
- Modify: `Game/Identity/PlayerObservationController.cs`

- [ ] **Step 1: Replace contents**

```csharp
#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.Identity;

internal sealed class PlayerObservationController : MonoBehaviour
{
    private const float PollIntervalSeconds = 5f;

    private PlayerObservationStore? _store;
    private float _nextPollAt;
    private string? _lastPlayerAccountId;
    private string? _lastPlayerUsername;

    internal void Configure(PlayerObservationStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    private void Update()
    {
        if (_store == null)
            return;
        if (Time.unscaledTime < _nextPollAt)
            return;

        _nextPollAt = Time.unscaledTime + PollIntervalSeconds;

        try
        {
            var playerAccountId = BppClientCacheBridge.TryGetProfileAccountId()?.Trim();
            var playerUsername = BppClientCacheBridge.TryGetProfileUsername()?.Trim();
            if (
                string.IsNullOrWhiteSpace(playerAccountId)
                || string.IsNullOrWhiteSpace(playerUsername)
            )
                return;

            var hasRow = _store.TryLoad(out _);
            var shouldRewrite =
                !string.Equals(_lastPlayerAccountId, playerAccountId, StringComparison.Ordinal)
                || !string.Equals(_lastPlayerUsername, playerUsername, StringComparison.Ordinal)
                || !hasRow;
            if (shouldRewrite)
            {
                _store.Save(
                    new PlayerObservationRecord(
                        playerAccountId,
                        playerUsername,
                        DateTimeOffset.UtcNow.ToString("o")
                    )
                );
                BppLog.Info(
                    "PlayerObservationController",
                    $"Wrote player observation for account {playerAccountId} to observation.v1.json."
                );
            }

            _lastPlayerAccountId = playerAccountId;
            _lastPlayerUsername = playerUsername;
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "PlayerObservationController",
                "Failed while updating player observation.",
                ex
            );
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Game/Identity/PlayerObservationController.cs
git commit -m "Drop AuthStore and online wiring from PlayerObservationController"
```

### Task 23: Drop InstallationId from RunBundleUploadRequestV3

**Files:**
- Modify: `Game/Online/Models/RunBundleUploadRequestV3.cs`

- [ ] **Step 1: Delete the InstallationId property**

Remove these lines:

```csharp
[JsonProperty("installation_id")]
public string InstallationId { get; set; } = string.Empty;
```

- [ ] **Step 2: Commit**

```bash
git add Game/Online/Models/RunBundleUploadRequestV3.cs
git commit -m "Drop InstallationId from run bundle upload request model"
```

### Task 24: Drop installationId from RunBundleUploadStore and RunBundleUploadService

**Files:**
- Modify: `Game/RunLogging/Upload/RunBundleUploadStore.cs`
- Modify: `Game/RunLogging/Upload/RunBundleUploadService.cs`

- [ ] **Step 1: Update `RunBundleUploadStore.TryBuildRunBundleSnapshot` signature and body**

Change:

```csharp
public RunBundleUploadSnapshot? TryBuildRunBundleSnapshot(
    string runId,
    string installationId,
    string playerAccountId
)
```

to:

```csharp
public RunBundleUploadSnapshot? TryBuildRunBundleSnapshot(
    string runId,
    string playerAccountId
)
```

In the constructed `RunBundleUploadRequestV3` payload, drop `InstallationId = installationId`.

- [ ] **Step 2: Update RunBundleUploadService**

Inside `UploadPendingRunBundlesAsync`:

```csharp
var playerAccountId = ResolvePlayerAccountId() ?? AnonymousPlayerAccountId;

// remove: var installationId = string.Empty;

// later:
var snapshot = _store.TryBuildRunBundleSnapshot(runId, playerAccountId);
```

- [ ] **Step 3: Commit**

```bash
git add Game/RunLogging/Upload/RunBundleUploadStore.cs Game/RunLogging/Upload/RunBundleUploadService.cs
git commit -m "Drop installationId from run bundle upload store and service"
```

### Task 25: Trim IdentityJsonFileStore

**Files:**
- Modify: `Game/Identity/IdentityJsonFileStore.cs`

- [ ] **Step 1: Remove auth helpers**

Drop `AuthFileName` and `AuthPath`. The `DeleteLegacyDatabaseFiles` helper still cleans `identity.db*` and stays. `Write` / `TryRead` / `DeleteIfExists` are still used by `PlayerObservationStore` so they stay.

```csharp
#nullable enable
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.Identity;

internal static class IdentityJsonFileStore
{
    private static readonly JsonSerializerSettings SerializerSettings =
        new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy(),
            },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
        };

    internal static string ObservationFileName => "observation.v1.json";

    internal static string ObservationPath(string identityDirectoryPath) =>
        Path.Combine(identityDirectoryPath, ObservationFileName);

    internal static bool TryRead<T>(string filePath, out T? payload)
        where T : class
    {
        payload = null;
        if (!File.Exists(filePath))
            return false;

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            payload = JsonConvert.DeserializeObject<T>(json, SerializerSettings);
            return payload != null;
        }
        catch
        {
            payload = null;
            return false;
        }
    }

    internal static void Write<T>(string filePath, T payload)
        where T : class
    {
        var json = JsonConvert.SerializeObject(payload, SerializerSettings);
        WriteStringAtomically(filePath, json);
    }

    internal static void DeleteIfExists(string filePath)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    internal static void DeleteLegacyDatabaseFiles(string identityDirectoryPath)
    {
        TryDelete(Path.Combine(identityDirectoryPath, "identity.db"));
        TryDelete(Path.Combine(identityDirectoryPath, "identity.db-wal"));
        TryDelete(Path.Combine(identityDirectoryPath, "identity.db-shm"));
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            DeleteIfExists(filePath);
        }
        catch
        {
            // Legacy cleanup must not break the current JSON observation path.
        }
    }

    private static void WriteStringAtomically(string filePath, string contents)
    {
        var directoryPath =
            Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Identity path must have a parent directory.");
        Directory.CreateDirectory(directoryPath);

        var tempPath = Path.Combine(
            directoryPath,
            $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp"
        );

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(filePath))
                File.Replace(tempPath, filePath, null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Game/Identity/IdentityJsonFileStore.cs
git commit -m "Drop auth helpers from IdentityJsonFileStore"
```

### Task 26: Delete AuthStore, AuthRecord, BearerState

**Files:**
- Delete: `Game/Identity/AuthStore.cs`
- Delete: `Game/Identity/AuthRecord.cs`
- Delete: `Game/Online/BearerState.cs`

- [ ] **Step 1: Remove the files**

```bash
git rm Game/Identity/AuthStore.cs Game/Identity/AuthRecord.cs Game/Online/BearerState.cs
```

- [ ] **Step 2: Commit**

```bash
git commit -m "Delete AuthStore, AuthRecord, and BearerState"
```

### Task 27: Update Plugin.cs

**Files:**
- Modify: `Plugin.cs`

- [ ] **Step 1: Replace identity-related sections**

1. Drop the `_authStore` field.
2. Add a one-shot `auth.v1.json` cleanup helper that runs after the identity directory is known.
3. Drop AuthStore wiring from `BuildIdentityAndOnlineServices`, `AddConfiguredPlayerObservationController`, and `AddConfiguredHistoryPanel`. Update `DisposeIdentityAndOnlineServices` to no longer null `_authStore`.

Apply these changes in the file:

```csharp
private BppComposition? _composition;
private ModOnlineClient? _onlineClient;
private PlayerObservationStore? _playerObservationStore;
private bool _patchesApplied;
```

```csharp
private void BuildIdentityAndOnlineServices(IBppServices services)
{
    var identityDirectoryPath = services.Paths.IdentityDirectoryPath;
    if (string.IsNullOrWhiteSpace(identityDirectoryPath))
    {
        BppLog.Warn(
            "Plugin",
            "Identity directory path unavailable; online services will be inactive."
        );
        return;
    }

    DeleteLegacyAuthFile(identityDirectoryPath);

    _playerObservationStore = new PlayerObservationStore(identityDirectoryPath);

    var routes = V3Routes.TryCreate(V3UploadDefaults.ApiBaseUrl);
    if (routes == null)
    {
        BppLog.Warn("Plugin", "V3 API base URL invalid; online services will be inactive.");
        return;
    }

    var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(Math.Max(10, V3UploadDefaults.RequestTimeoutSeconds)),
    };
    _onlineClient = new ModOnlineClient(httpClient, routes);
    BppLog.Info("Plugin", "Identity JSON store and online client ready.");
}

private static void DeleteLegacyAuthFile(string identityDirectoryPath)
{
    var legacyAuthPath = Path.Combine(identityDirectoryPath, "auth.v1.json");
    if (!File.Exists(legacyAuthPath))
        return;

    try
    {
        File.Delete(legacyAuthPath);
        BppLog.Info("Plugin", "Removed legacy auth.v1.json from identity directory.");
    }
    catch (Exception ex)
    {
        BppLog.Info("Plugin", $"Could not delete legacy auth.v1.json: {ex.Message}");
    }
}
```

```csharp
private void AddConfiguredPlayerObservationController()
{
    var controller = gameObject.AddComponent<PlayerObservationController>();
    if (_playerObservationStore == null)
    {
        BppLog.Warn(
            "Plugin",
            "Skipping PlayerObservationController configuration; observation store unavailable."
        );
        return;
    }

    controller.Configure(_playerObservationStore);
}
```

```csharp
private void AddConfiguredHistoryPanel(
    IBppServices services,
    CombatReplayRuntime combatReplayRuntime
)
{
    BppLog.Info("Plugin", "Adding HistoryPanel");
    var historyPanel = gameObject.AddComponent<HistoryPanel>();

    var historyPanelRuntime = new HistoryPanelRuntime(
        services.RunContext,
        services.Paths.RunLogDatabasePath,
        services.Paths.CombatReplayDirectoryPath,
        () => combatReplayRuntime
    );

    if (_onlineClient == null)
    {
        BppLog.Warn(
            "Plugin",
            "Skipping HistoryPanel online wiring; online client unavailable."
        );
        return;
    }

    historyPanel.Configure(
        HistoryPanelFactory.Create(historyPanelRuntime, _onlineClient)
    );
}
```

```csharp
private void DisposeIdentityAndOnlineServices()
{
    _onlineClient?.Dispose();
    _onlineClient = null;
    _playerObservationStore = null;
}
```

- [ ] **Step 2: Run client build**

Run: `dotnet build BazaarPlusPlus.csproj 2>&1 | tail -20`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Plugin.cs
git commit -m "Strip auth wiring from Plugin and clean up legacy auth.v1.json on startup"
```

---

## Phase 5 — Client tests

### Task 28: Delete tests/IdentityDatabase.Tests/

**Files:**
- Delete: `tests/IdentityDatabase.Tests/`

- [ ] **Step 1: Remove the directory**

```bash
git rm -r tests/IdentityDatabase.Tests
```

- [ ] **Step 2: Commit**

```bash
git commit -m "Delete IdentityDatabase test project"
```

### Task 29: Update tests/GhostBattleSync.Tests/Program.cs

The existing test reaches into `GhostBattleSyncService` via reflection (`RequireType(...)`). After Task 20 the constructor signature changed (no AuthStore) and the `QueryAgainstMeAsync` / `RequestReplayDownloadLinkAsync` signatures lost their bearer parameter.

**Files:**
- Modify: `tests/GhostBattleSync.Tests/Program.cs`

- [ ] **Step 1: Run the test, capture failures**

Run: `dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj 2>&1 | tail -30`
Expected: any FAIL lines reference removed members (`AuthStore`, bearer parameter, etc.).

- [ ] **Step 2: Patch Program.cs**

For each FAIL the runner prints, locate the corresponding reflection block in the test file. Two kinds of fixes:

1. Any line of the form `RequireType("...AuthStore")` or `RequireType("...BearerState")` → delete that line and any usage downstream.
2. Any `GetMethod("QueryAgainstMeAsync", ...)` or `GetMethod("RequestReplayDownloadLinkAsync", ...)` lookup whose follow-up `Invoke` passes a bearer-token positional argument → drop the bearer argument from the array.
3. Any `GhostBattleSyncService` ctor `Activator.CreateInstance(...)` call that passed three args (repository, onlineClient, authStore) → drop the third argument.

- [ ] **Step 3: Re-run**

Run: `dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj 2>&1 | tail -30`
Expected: `OK` at end.

- [ ] **Step 4: Commit**

```bash
git add tests/GhostBattleSync.Tests/Program.cs
git commit -m "Update GhostBattleSync.Tests for new sync service signature"
```

---

## Phase 6 — Documentation

### Task 30: Refresh ModCFServerV3 README and api-reference

**Files:**
- Modify: `ModCFServerV3/README.md`
- Modify: `ModCFServerV3/docs/api-reference.md`

- [ ] **Step 1: Audit README**

Open `ModCFServerV3/README.md`, search for any of these tokens and remove or rewrite the corresponding paragraph: `installation_id`, `users`, `tokens`, `bearer`, `Bearer`, `password`, `auth`, `Authorization`, `ALLOW_UNAUTHENTICATED_REPLAY`, `/activate`, `/login`, `/logout`. Add a note that ghost-battles is identified by `?player_account_id=…` and that `seen_player_accounts` is the new opponent registry.

- [ ] **Step 2: Audit api-reference.md**

Same audit as above, more rigorously since this file contains the contract:

1. Delete §2.1 (`POST /activate`), §2.2 (`POST /login`), §2.3 (`POST /logout`) entirely.
2. In §1.3 "鉴权": replace with a short paragraph stating the worker no longer issues or accepts bearer tokens; ghost-battles identifies the caller by query string; replay link/download are unconditionally anonymous.
3. In §3.1 (`POST /run-bundles`): drop `installation_id` from request body and the `loadKnownOpponentAccountIds` description; add a paragraph about `seen_player_accounts` upsert + the previous `users` table being gone.
4. In §4.1 (`GET /ghost-battles`): rewrite the "鉴权" line as "公开端点，身份由 `player_account_id` query 参数提供"; update the "Query 参数" list to include `player_account_id` (required) and remove the note about query parameter being ignored. Add the `400 invalid_request` error row.
5. In §5.1 / §5.2 (replay link / download): drop the `ALLOW_UNAUTHENTICATED_REPLAY_*` paragraphs. Update the auth row in the error table to remove the 401/403 cases for missing/foreign bearer; keep the 403 `replay_forbidden` for the no-opponent edge case.
6. In §6 "鉴权身份谱": delete the `users.player_account_id` and `tokens.player_account_id` rows. Add a row for `seen_player_accounts.player_account_id`.

- [ ] **Step 3: Commit**

```bash
git add ModCFServerV3/README.md ModCFServerV3/docs/api-reference.md
git commit -m "Refresh ModCFServerV3 README and API reference for unauthenticated contract"
```

### Task 31: Refresh mod-side docs

**Files:**
- Modify: `docs/run-upload.md`
- Modify: `docs/reference/sqlite-schema-reference.md`
- Modify: `docs/reference/ghost-battle-data-flow.md`
- Modify: `docs/mod-features-overview.md`
- Modify: `docs/mod-cf-server-deploy.md`
- Audit-only (modify only if hits found): `docs/reference/settings-and-debug-surfaces.md`

- [ ] **Step 1: Audit each file**

In every file above, search for these tokens and remove/rewrite any paragraph or table row that references them:

`installation_id`, `Installer`, `Installer 重新登录`, `bearer`, `Bearer`, `Authorization`, `auth`, `AuthStore`, `BearerState`, `/activate`, `/login`, `/logout`, `ALLOW_UNAUTHENTICATED_REPLAY`, `users` (only when used as the table name), `tokens` (only as the table name).

When in doubt about a passage, prefer "delete" over "rewrite" — out-of-date docs are worse than missing docs.

- [ ] **Step 2: Update sqlite-schema-reference.md specifically**

Find the section listing D1 tables. Remove the `users` and `tokens` entries. Drop the `installation_id` columns from `runs` / `battles` / `run_bundles`. Add a `seen_player_accounts` entry: `player_account_id TEXT PK, first_seen_at_utc TEXT NOT NULL, last_seen_at_utc TEXT NOT NULL`.

- [ ] **Step 3: Update ghost-battle-data-flow.md specifically**

Find the diagram or sequence describing how the mod identifies itself. Replace any "load bearer from auth.v1.json" step with "read player_account_id from BppClientCacheBridge profile cache". Drop any "401 → re-register" branches.

- [ ] **Step 4: Commit**

```bash
git add docs/
git commit -m "Refresh mod docs to drop auth and installation_id concepts"
```

---

## Phase 7 — Final verification

### Task 32: Run all server tests

- [ ] **Step 1: tsc**

Run: `cd ModCFServerV3 && npm run check 2>&1 | tail -10`
Expected: no output, exit 0.

- [ ] **Step 2: vitest**

Run: `cd ModCFServerV3 && npm test 2>&1 | tail -40`
Expected: all tests PASS, no skipped tests, summary should show 0 failures.

- [ ] **Step 3: No commit (verification only)**

If anything fails, fix in place — do not commit a wrap-up "fix verification" commit unless that's the only way.

### Task 33: Run dotnet build and full test sweep

- [ ] **Step 1: Mod build**

Run: `dotnet build BazaarPlusPlus.csproj 2>&1 | tail -10`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: All C# test projects**

The `.rules` file says "Targeted code changes should run the smallest relevant test project or build first, not the whole repo by default." For this large refactor though, every test project in `tests/` should compile — run them all once:

```bash
for proj in tests/*/*.Tests.csproj; do
  echo "=== $proj ==="
  dotnet run --project "$proj" 2>&1 | tail -5 || echo "FAIL: $proj"
done
```

Expected: every `=== … ===` block ends in `OK` or `PASS` lines with no `FAIL`.

- [ ] **Step 3: No commit**

### Task 34: Cross-repo grep sweep

- [ ] **Step 1: Verify no remaining references**

Run each of these and confirm no hits in tracked files (sample finds in `decompiled/` are fine — that directory is read-only):

```bash
git grep -n -E "AuthStore|BearerState|installation_id|HandleUnauthorized|requireBearerAuth|/activate\b|/login\b|/logout\b|password_hash|ALLOW_UNAUTHENTICATED_REPLAY"
```

Expected: only matches inside `decompiled/`, `docs/remove-login-and-installation-id-design.md`, `docs/remove-login-and-installation-id-plan.md`, or migration files (`0009_drop_auth_tables.sql`, `0002_auth_simplification.sql`). Any other hit is a missed deletion — go back and fix.

- [ ] **Step 2: No commit**

### Task 35: Update verification checklist

**Files:**
- Modify: `docs/remove-login-and-installation-id-design.md`

- [ ] **Step 1: Tick the spec's verification checklist**

Open the spec doc and check off the §8 boxes that are now verified. Leave "Manual smoke against staging worker" unchecked — that's a deploy-time step.

- [ ] **Step 2: Commit**

```bash
git add docs/remove-login-and-installation-id-design.md
git commit -m "Tick verification checklist for login removal implementation"
```

---

## Out-of-band: deploy

Deploy is not a code task. Coordinate a window, then:

```bash
cd ModCFServerV3
npx wrangler d1 migrations apply --remote   # applies 0009 → 0010 → 0011 to production D1
npx wrangler deploy                         # ships the new worker
```

Smoke after:
```bash
curl -s https://mod-api-v3.bazaarplusplus.com/health
curl -s "https://mod-api-v3.bazaarplusplus.com/ghost-battles?player_account_id=<known-id>&limit=1" | head -100
```

Mod release is decoupled — old mods send `Authorization: Bearer …` headers that the new worker silently ignores (CORS allow-list no longer enumerates `authorization`, but the request still succeeds because the server doesn't read the header).
