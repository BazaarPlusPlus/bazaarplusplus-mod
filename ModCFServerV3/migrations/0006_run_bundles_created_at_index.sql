-- Migration 0006: index run_bundles by created_at_utc
--
-- The analytics mirror is moving its partition key from the client-supplied
-- submitted_at_utc to the server-assigned created_at_utc, to eliminate
-- cross-day drops caused by client clock drift. After the switch, bpp.mirror
-- pages bundles using:
--   SELECT ... FROM run_bundles
--   WHERE created_at_utc >= ? AND created_at_utc < ?
--   ORDER BY created_at_utc ASC, bundle_id ASC;
--
-- Without an index on created_at_utc this is a full table scan on every
-- hourly ingest and backfill window. bundle_id is added as a trailing key so
-- the ORDER BY in the discover query is satisfied directly from the index.
--
-- The existing idx_run_bundles_submitted_at is left in place for any other
-- consumers (e.g. ghost-battles, login) that still paginate by submitted_at.

CREATE INDEX IF NOT EXISTS idx_run_bundles_created_at
  ON run_bundles (created_at_utc, bundle_id);
