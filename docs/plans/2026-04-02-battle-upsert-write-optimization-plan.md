# Battle Upsert Write Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce unnecessary D1 writes for repeated `battles` uploads while preserving current Worker behavior.

**Architecture:** Keep the existing `battles` UPSERT shape, but guard the conflict-update branch with explicit null-safe field comparisons so identical reuploads become no-ops. Remove the three unused `battles` secondary indexes via a new Wrangler migration instead of editing the already-applied initial migration.

**Tech Stack:** TypeScript, Cloudflare Workers, D1, Wrangler migrations, `node:test`, `tsx`

---

### Task 1: Lock The Duplicate Upload Semantics With Failing Tests

**Files:**
- Modify: `ModCFServer/test/uploadBattleArtifact.test.ts`
- Modify: `ModCFServer/test/helpers/mockEnv.ts`

- [ ] **Step 1: Add a regression test for identical battle reupload**

Add a test in `ModCFServer/test/uploadBattleArtifact.test.ts` that:

- uploads a valid battle once
- captures the stored `created_at_utc`, `updated_at_utc`, `uploader_player_account_id`, and `replay_object_key`
- uploads the same battle payload again after changing only the player binding and request timestamp
- asserts:
  - `created_at_utc` is unchanged
  - `updated_at_utc` changes because `uploader_player_account_id` changed
  - `uploader_player_account_id` reflects the new binding
- uploads the exact same battle a third time with the same binding
- asserts:
  - `created_at_utc` is still unchanged
  - `updated_at_utc` does not change on the third upload
  - `replay_object_key` is unchanged on the third upload

- [ ] **Step 2: Run the focused test to verify it fails**

Run: `npm test -- test/uploadBattleArtifact.test.ts`

Expected: FAIL because the mock D1 UPSERT currently rewrites `updated_at_utc` on every duplicate upload.

### Task 2: Implement The Guarded UPSERT Behavior

**Files:**
- Modify: `ModCFServer/src/persistence/battles.ts`
- Modify: `ModCFServer/test/helpers/mockEnv.ts`

- [ ] **Step 1: Add the explicit null-safe UPSERT guard**

Update `ModCFServer/src/persistence/battles.ts` so `ON CONFLICT(battle_id) DO UPDATE SET ...` ends with a `WHERE` clause comparing the stored row against `excluded` for:

- `run_id`
- `client_id`
- `uploader_player_account_id`
- `recorded_at_utc`
- `day`
- `hour`
- `player_name`
- `player_account_id`
- `player_hero`
- `player_rank`
- `player_rating`
- `player_level`
- `opponent_name`
- `opponent_account_id`
- `opponent_hero`
- `opponent_rank`
- `opponent_rating`
- `opponent_level`
- `combat_kind`
- `result`
- `winner_combatant_id`
- `loser_combatant_id`
- `replay_schema_version`
- `replay_object_key`
- `replay_size_bytes`

Use SQLite `IS NOT` comparisons so `NULL` values behave correctly.

- [ ] **Step 2: Mirror the guarded behavior in the mock D1 implementation**

Update `ModCFServer/test/helpers/mockEnv.ts` so the `INSERT INTO battles` branch:

- inserts a new row when absent
- preserves `created_at_utc` on conflict
- computes whether any compared field changed
- only updates the stored row and `updated_at_utc` when a compared field differs
- leaves the existing row untouched when the compared field set is identical

- [ ] **Step 3: Run the focused test to verify it passes**

Run: `npm test -- test/uploadBattleArtifact.test.ts`

Expected: PASS

### Task 3: Remove The Unused Battle Indexes Via A New Migration

**Files:**
- Create: `ModCFServer/migrations/0002_drop_unused_battles_indexes.sql`
- Modify: `ModCFServer/test/schema.test.ts`

- [ ] **Step 1: Add the follow-up Wrangler migration**

Create `ModCFServer/migrations/0002_drop_unused_battles_indexes.sql` with:

```sql
DROP INDEX IF EXISTS idx_battles_player_recorded;
DROP INDEX IF EXISTS idx_battles_run_recorded;
DROP INDEX IF EXISTS idx_battles_client_recorded;
```

- [ ] **Step 2: Extend schema tests**

Update `ModCFServer/test/schema.test.ts` so tests assert:

- `0001_initial_schema.sql` still contains the original clean-break schema
- `0002_drop_unused_battles_indexes.sql` exists
- the new migration drops exactly:
  - `idx_battles_player_recorded`
  - `idx_battles_run_recorded`
  - `idx_battles_client_recorded`

- [ ] **Step 3: Run the focused schema tests**

Run: `npm test -- test/schema.test.ts`

Expected: PASS

### Task 4: Run Focused Verification

**Files:**
- Reuse files above

- [ ] **Step 1: Run the targeted ModCFServer tests**

Run: `npm test -- test/uploadBattleArtifact.test.ts test/schema.test.ts test/ghostBattlesV2.test.ts`

Expected: PASS

- [ ] **Step 2: Run the Worker typecheck**

Run: `npm run check`

Expected: PASS
