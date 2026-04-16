-- Migration 0002: auth simplification -- per-user bearer tokens, drop installations

CREATE TABLE tokens (
  token              TEXT    PRIMARY KEY,
  player_account_id  TEXT    NOT NULL REFERENCES users(player_account_id),
  issued_at_utc      TEXT    NOT NULL,
  revoked_at_utc     TEXT    NULL,
  last_used_at_utc   TEXT    NULL
);

CREATE INDEX tokens_by_user ON tokens(player_account_id, revoked_at_utc);

-- Drop retired tables
DROP TABLE IF EXISTS installation_sessions;
DROP TABLE IF EXISTS installation_observations;
DROP TABLE IF EXISTS installations;

-- Swap run_bundles unique key: (installation_id, run_id, payload_hash) -> (player_account_id, run_id, payload_hash)
-- installation_id columns are kept nullable on runs/battles/run_bundles for conservative retention.
CREATE UNIQUE INDEX run_bundles_player_run_payload_unique
  ON run_bundles(player_account_id, run_id, payload_hash);
