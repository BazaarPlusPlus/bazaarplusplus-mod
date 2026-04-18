-- Migration 0004: index run_bundles by submitted_at_utc
--
-- The analytics pipeline (bpp.discover) pulls bundles in event-time windows:
--   SELECT ... FROM run_bundles
--   WHERE submitted_at_utc >= ? AND submitted_at_utc < ?
--   ORDER BY submitted_at_utc ASC, bundle_id ASC;
--
-- Without an index on submitted_at_utc this is a full table scan on every
-- hourly ingest (and on every backfill window). As run_bundles grows this
-- dominates D1 rows-read cost and cron query latency.
--
-- bundle_id is added as a trailing key so the ORDER BY in the discover query
-- is satisfied directly from the index, avoiding a sort.

CREATE INDEX IF NOT EXISTS idx_run_bundles_submitted_at
  ON run_bundles (submitted_at_utc, bundle_id);
