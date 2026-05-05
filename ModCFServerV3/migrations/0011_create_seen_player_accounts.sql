-- Migration 0011: lightweight registry of player accounts that have uploaded
-- a run bundle. Replaces the auth-era users table for the ghost-battle
-- opponent filter. Backfill from existing run_bundles uploaders so the first
-- post-deploy ghost-battles query does not lose visibility for opponents we
-- already knew about.

CREATE TABLE seen_player_accounts (
  player_account_id TEXT PRIMARY KEY,
  first_seen_at_utc TEXT NOT NULL,
  last_seen_at_utc  TEXT NOT NULL
);

INSERT OR IGNORE INTO seen_player_accounts (
  player_account_id,
  first_seen_at_utc,
  last_seen_at_utc
)
SELECT
  player_account_id,
  MIN(created_at_utc),
  MAX(created_at_utc)
FROM run_bundles
WHERE player_account_id IS NOT NULL
  AND player_account_id != ''
  AND player_account_id != 'anonymous-player'
GROUP BY player_account_id;
