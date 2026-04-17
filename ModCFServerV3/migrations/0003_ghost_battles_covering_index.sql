-- Migration 0003: covering index for ghost battles query
--
-- queryGhostBattles selects ~17 columns from battles for each lookup. The
-- existing idx_battles_opponent_recorded(opponent_account_id, recorded_at_utc DESC, battle_id DESC)
-- can satisfy WHERE+ORDER but still requires up to LIMIT (200) random reads
-- back into the table to fetch the projected columns. On the hot login path
-- (one query per active player) this dominates ghost-battle latency once the
-- battles table grows beyond a few hundred thousand rows.
--
-- This index includes every column read by queryGhostBattles so SQLite can
-- answer the request from index pages alone.

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
    replay_available
  );

DROP INDEX IF EXISTS idx_battles_opponent_recorded;
