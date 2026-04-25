-- Migration 0008: mark the final battle within an uploaded run bundle.
--
-- Ghost battle clients need to explain when the authenticated player won the
-- uploader's final bundled battle. The flag is computed at upload time from the
-- full battle_projections array so the query path avoids per-row fan-out into
-- run bundle artifacts or sibling battle scans.
--
-- The ghost-battles hot path remains bounded by the existing
-- opponent_account_id + recorded_at_utc lookup and LIMIT 200. Rebuilding the
-- covering index with this trailing column keeps the query to one index range
-- scan with no table lookups for the projected response fields.

ALTER TABLE battles
  ADD COLUMN is_bundle_final_battle INTEGER NOT NULL DEFAULT 0;

DROP INDEX IF EXISTS idx_battles_opponent_recorded_covering;

CREATE INDEX IF NOT EXISTS idx_battles_opponent_recorded_covering
  ON battles(
    opponent_account_id,
    recorded_at_utc DESC,
    battle_id DESC,
    day,
    player_name,
    player_account_id_in_payload,
    player_hero,
    player_rank,
    player_rating,
    player_level,
    opponent_name,
    opponent_hero,
    opponent_rank,
    opponent_rating,
    opponent_level,
    result,
    replay_available,
    is_bundle_final_battle
  );
