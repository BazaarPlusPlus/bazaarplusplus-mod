# ModCFServerV3 SQL Review 2026-04

## Scope

This note captures a focused review of `ModCFServerV3` SQL usage after investigating D1 cost around run-bundle uploads.

Reviewed areas:

- `ModCFServerV3/src/features/v3/uploadRunBundle.ts`
- `ModCFServerV3/src/features/v3/queryGhostBattles.ts`
- `ModCFServerV3/src/features/v3/createReplayLink.ts`
- `ModCFServerV3/src/features/v3/downloadReplay.ts`
- `ModCFServerV3/src/features/v3/requireInstallationAuth.ts`
- `ModCFServerV3/migrations/0001_initial_schema.sql`

## Implemented Changes

The following changes were made during this review:

- Removed the per-run `DELETE FROM battles WHERE run_id = ?` step from run-bundle upload.
- Removed the server-side `duplicate_battle_id` validation in `uploadRunBundle`.
- Changed battle projection writes to `INSERT ... ON CONFLICT(battle_id) DO UPDATE`.
- Changed per-battle writes from serial `await ...run()` calls to a single `env.DB.batch(...)`.

The intent is to reduce D1 write cost on the upload path while keeping duplicate payload retries idempotent.

## Main Finding

The most expensive SQL pattern found in the reviewed code was the old run-level delete in `uploadRunBundle`:

```sql
DELETE FROM battles WHERE run_id = ?
```

That statement scaled with the number of previously projected battles for the run and was executed on every re-upload of the same run. It has been removed.

## Current Query Assessment

### Ghost battles query

`GET /ghost-battles` currently uses:

```sql
SELECT ...
FROM battles AS b
WHERE b.opponent_account_id = ?
  AND b.recorded_at_utc >= ?
ORDER BY b.recorded_at_utc DESC, b.battle_id DESC
LIMIT ?
```

This query matches the existing composite index:

```sql
CREATE INDEX IF NOT EXISTS idx_battles_opponent_recorded
  ON battles(opponent_account_id, recorded_at_utc DESC, battle_id DESC);
```

This is the best-shaped query in the service and was not identified as a likely cost problem.

### Replay lookup path

Replay download currently does three keyed D1 reads before the R2 fetch:

1. `replay_tokens` by `token`
2. `battles` by `battle_id`
3. `run_bundles` by `bundle_id`

These are all point lookups on primary keys, so there is no obvious full-scan risk. The main concern here is cumulative request latency from sequential lookups, not a bad SQL shape.

### Auth and login paths

The following lookups are also keyed and look structurally fine:

- `installations` by `installation_id`
- `installation_sessions` by `session_id`
- `users` by `player_username`
- `users` by `player_account_id`

No additional obviously expensive SQL statement was found in these paths.

## Remaining Cost Candidates

### 1. `last_seen_at_utc` write on every authenticated request

`requireInstallationAuth` performs an `UPDATE installations SET last_seen_at_utc = ?` after successful auth.

This means hot authenticated routes such as:

- `POST /run-bundles`
- `POST /installations/observations`
- `POST /ghost-battles/:battleId/replay-link`
- `GET /replays/:token`

all turn one auth check into a read plus a write.

This is the next most likely source of avoidable D1 cost if request volume grows.

Potential future options:

- only update `last_seen_at_utc` if the stored value is older than a threshold
- only update it on selected write routes
- move it to lower-priority telemetry instead of the hot auth path

### 2. Sequential replay lookup chain

Replay download still performs multiple sequential reads before touching R2. Each query is cheap individually, but the chain may become noticeable under higher replay traffic.

Potential future options:

- collapse the battle and bundle lookup into a join
- denormalize enough data onto `replay_tokens` to skip one lookup

## What Was Not Identified As A Problem

- No second statement comparable to the removed run-level `DELETE` was found in the current Worker SQL.
- `ghost-battles` already has an index that matches its filter and sort shape.
- `activate` does multiple existence checks before insert, but the route is low-frequency and not a current optimization target.

## Practical Conclusion

For the current workload shape discussed during review, where one run usually contains only around a dozen battles:

- changing battle writes to `batch` is still reasonable, but the gain is expected to be moderate rather than dramatic
- removing the run-level battle delete was the most important SQL-side cleanup
- if more optimization is needed later, start with throttling or removing the per-request `last_seen_at_utc` update
