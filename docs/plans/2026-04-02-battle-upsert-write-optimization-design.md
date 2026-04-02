# Battle Upsert Write Optimization Design

## Goal

Reduce unnecessary D1 writes for repeated `battles` uploads in `ModCFServer` without changing the existing API contract, R2 object flow, or ghost battle query behavior.

## Background

`ModCFServer` currently persists uploaded battle metadata with a single `INSERT INTO battles ... ON CONFLICT(battle_id) DO UPDATE` statement.

The `battles` table has:

- a primary key on `battle_id`
- four explicit secondary indexes:
  - `idx_battles_opponent_recorded`
  - `idx_battles_player_recorded`
  - `idx_battles_run_recorded`
  - `idx_battles_client_recorded`

When the same `battle_id` is uploaded again, the Worker always executes the `DO UPDATE` branch and rewrites the row even if the incoming payload is identical to the existing row. Because the update touches indexed columns such as `opponent_account_id`, `player_account_id`, `run_id`, `client_id`, and `recorded_at_utc`, D1 also has to maintain the affected indexes.

This creates write amplification on duplicate or retried uploads.

## Motivation

The current behavior is acceptable functionally, but wasteful operationally.

The main problems are:

- identical battle reuploads still perform a real database update
- `updated_at_utc` changes on every retry, guaranteeing a write even when business data is unchanged
- three of the four secondary indexes do not have an in-repo read path in the current Worker implementation
- the extra writes increase D1 cost and reduce headroom without producing user-visible value

The goal is to keep the write path correct while making duplicate uploads cheap.

## Current Read And Write Shape

### Writes

The Worker writes one `battles` row per uploaded battle artifact. The write path is:

- upload replay payload bytes to R2
- resolve player link
- upsert the `battles` row in D1

### Reads

Current in-repo `ModCFServer` read paths only require:

- `battle_id` primary key lookups for replay-link creation and replay download
- `opponent_account_id` plus `recorded_at_utc` ordering for ghost battle queries

No current Worker code queries `battles` by:

- `player_account_id`
- `run_id`
- `client_id`

That means the corresponding three secondary indexes currently impose write cost without serving an active in-repo read path.

## Non-Goals

- changing the `POST /battles` request or response contract
- changing the R2 object key strategy
- redesigning the `battles` table
- introducing compatibility work for external scripts or ad hoc operational queries
- optimizing non-`battles` tables

## Design Summary

Use a conservative two-part optimization:

1. Add a guarded `DO UPDATE ... WHERE` clause so the conflict branch only performs a real update when battle content differs from the stored row.
2. Drop the three currently unused secondary indexes on `player_account_id`, `run_id`, and `client_id`, while keeping the `opponent_account_id` index used by ghost battle queries.

This keeps the API and query behavior stable while cutting duplicate-write amplification.

## Detailed Design

### 1. Guard The UPSERT Update Path

The `battles` upsert will continue to use `ON CONFLICT(battle_id) DO UPDATE`, but the update branch will gain a `WHERE` clause.

The `WHERE` clause will compare persisted values against `excluded` values for the fields that define the stored battle projection. If every compared field is unchanged, SQLite will skip the update.

Key points:

- comparisons must be null-safe
- `updated_at_utc` must not be the sole reason an update occurs
- `created_at_utc` remains insert-only
- `updated_at_utc` changes only when at least one business field actually changes

This preserves the meaning of `updated_at_utc` as "last meaningful change" instead of "last retry time".

The compared field set should be explicit and match the fields that can legitimately change in the persisted projection:

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

Field intent:

- `uploader_player_account_id` is included because it may legitimately change after the client becomes player-bound. A later reupload should be able to backfill that linkage.
- `replay_object_key` and `replay_size_bytes` are included because replay object metadata is part of the stored projection used for replay download. If the uploaded replay bytes differ enough to produce a new object key or size, that is a meaningful persisted change and should update the row.
- `created_at_utc` is intentionally excluded from comparisons and from `UPDATE SET`. This is already true in the current SQL, so the insert-only behavior does not require a new code change.

### 2. Remove Unused Secondary Indexes

For already deployed D1 databases, index removal must be delivered as a new Wrangler migration. Editing `0001_initial_schema.sql` alone would only affect fresh databases because Wrangler tracks applied migrations in D1.

The follow-up migration will drop these indexes:

- `idx_battles_player_recorded`
- `idx_battles_run_recorded`
- `idx_battles_client_recorded`

The new migration should be a dedicated file such as `ModCFServer/migrations/0002_drop_unused_battles_indexes.sql` containing:

```sql
DROP INDEX IF EXISTS idx_battles_player_recorded;
DROP INDEX IF EXISTS idx_battles_run_recorded;
DROP INDEX IF EXISTS idx_battles_client_recorded;
```

The schema will continue to keep:

- the `battle_id` primary key
- `idx_battles_opponent_recorded`

This retains the index needed for:

- `WHERE opponent_account_id = ?`
- `ORDER BY recorded_at_utc DESC`

and removes index maintenance for columns that are not currently queried by the Worker.

## Expected Outcome

### Duplicate Uploads With No Data Change

Before:

- conflict path always performs an update
- `updated_at_utc` always changes
- table row and relevant indexes are rewritten

After:

- conflict path resolves successfully
- no row update occurs if all business fields are unchanged
- `updated_at_utc` remains unchanged
- duplicate upload becomes close to a no-op on the D1 side

### New Uploads Or Genuine Battle Corrections

Before and after:

- inserts still create one `battles` row
- meaningful changes to an existing battle still update the row
- ghost query behavior remains unchanged

## Risks

The main risk is hidden dependence on the three removed indexes outside the repository.

Examples:

- manual SQL investigation against D1 by `client_id`
- future Worker code added later without reintroducing the needed index
- external scripts querying by `run_id` or `player_account_id`

Within the current repository, no such read path exists. If an external dependency exists, the safe response is to keep or reintroduce the specific index that query path needs.

## Execution Plan

1. Add or update focused `ModCFServer` tests that prove repeated identical battle upserts do not refresh timestamps or otherwise behave like meaningful updates.
2. Update `ModCFServer/src/persistence/battles.ts` to add the guarded `DO UPDATE ... WHERE` logic.
3. Add a new Wrangler migration such as `ModCFServer/migrations/0002_drop_unused_battles_indexes.sql` to drop the three unused `battles` indexes from already deployed databases.
4. Update any schema assertions or test helpers affected by the migration change.
5. Run the focused `ModCFServer` test suite as the verification step.

## Verification

Verification should stay proportional to the change:

- run the focused `ModCFServer` test suite
- ensure schema tests reflect the new index set
- ensure ghost battle query tests still pass

No full-repo `BuildAll` run is required for this change because the scope is limited to Worker-side SQL and schema behavior.
