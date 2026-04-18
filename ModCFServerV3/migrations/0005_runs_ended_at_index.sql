-- Migration 0005: index runs by ended_at_utc
--
-- The analytics mirror sync pages `runs` by UTC day using:
--   SELECT ... FROM runs
--   WHERE ended_at_utc >= ? AND ended_at_utc < ?
--   ORDER BY ended_at_utc ASC, run_id ASC;
--
-- Without a composite index on (ended_at_utc, run_id), D1 must scan and sort
-- the growing runs table for every mirrored day fetch. The trailing run_id key
-- matches the pagination tie-breaker and ORDER BY, so the day-range query can
-- stream rows directly from the index.

CREATE INDEX IF NOT EXISTS idx_runs_ended_at
  ON runs (ended_at_utc, run_id);
