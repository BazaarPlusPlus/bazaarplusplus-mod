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
