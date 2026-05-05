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
