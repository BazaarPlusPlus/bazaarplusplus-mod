-- Migration 0007: index runs by updated_at_utc
--
-- Companion to 0006: the analytics mirror is switching from client-clock
-- `ended_at_utc` to server-clock `updated_at_utc` as its partition key for
-- the runs table. After the switch, bpp.mirror pages runs using:
--   SELECT ... FROM runs
--   WHERE updated_at_utc >= ? AND updated_at_utc < ?
--   ORDER BY updated_at_utc ASC, run_id ASC;
--
-- updated_at_utc reflects the LAST upsert (not the first insert), so the same
-- run_id can migrate between day partitions if it is upserted across midnight.
-- The analyzer handles this via a cross-day ROW_NUMBER() dedup in merged_runs.
--
-- run_id trails as a tie-breaker so the ORDER BY is served directly from the
-- index. The existing idx_runs_ended_at is left in place; no consumer is
-- removed here.
--
-- Note: the initial schema does NOT carry a created_at_utc column on runs;
-- updated_at_utc is the only server-clock field available for this table and
-- is written (== createdAtUtc) on every upsert in uploadRunBundle.

CREATE INDEX IF NOT EXISTS idx_runs_updated_at
  ON runs (updated_at_utc, run_id);
