CREATE TABLE IF NOT EXISTS sync_runs (
  sync_run_id TEXT PRIMARY KEY,
  started_at_utc TEXT NOT NULL,
  finished_at_utc TEXT NULL,
  mode TEXT NOT NULL CHECK (mode IN ('sync', 'rebuild')),
  status TEXT NOT NULL CHECK (status IN ('running', 'succeeded', 'failed')),
  scanned_file_count INTEGER NOT NULL DEFAULT 0,
  changed_file_count INTEGER NOT NULL DEFAULT 0,
  parsed_file_count INTEGER NOT NULL DEFAULT 0,
  error_count INTEGER NOT NULL DEFAULT 0,
  error_message TEXT NULL
);

CREATE TABLE IF NOT EXISTS sync_run_errors (
  sync_run_id TEXT NOT NULL,
  object_key TEXT NULL,
  local_path TEXT NULL,
  stage TEXT NOT NULL,
  error_message TEXT NOT NULL,
  created_at_utc TEXT NOT NULL,
  FOREIGN KEY (sync_run_id) REFERENCES sync_runs(sync_run_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_sync_run_errors_run
  ON sync_run_errors(sync_run_id);

CREATE TABLE IF NOT EXISTS r2_objects (
  object_key TEXT PRIMARY KEY,
  local_path TEXT NOT NULL,
  object_kind TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  mtime_utc TEXT NOT NULL,
  content_hash TEXT NOT NULL,
  last_seen_sync_run_id TEXT NOT NULL,
  parsed_at_utc TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_r2_objects_kind
  ON r2_objects(object_kind);

CREATE TABLE IF NOT EXISTS battle_replays (
  object_key TEXT PRIMARY KEY,
  battle_id TEXT NOT NULL,
  run_id TEXT NULL,
  client_id TEXT NULL,
  recorded_at_utc TEXT NOT NULL,
  day INTEGER NULL,
  hour INTEGER NULL,
  player_name TEXT NULL,
  player_account_id TEXT NULL,
  player_hero TEXT NULL,
  player_rank TEXT NULL,
  player_rating INTEGER NULL,
  player_level INTEGER NULL,
  opponent_name TEXT NULL,
  opponent_account_id TEXT NULL,
  opponent_hero TEXT NULL,
  opponent_rank TEXT NULL,
  opponent_rating INTEGER NULL,
  opponent_level INTEGER NULL,
  combat_kind TEXT NULL,
  result TEXT NULL,
  winner_combatant_id TEXT NULL,
  loser_combatant_id TEXT NULL,
  replay_schema_version INTEGER NULL,
  replay_size_bytes INTEGER NULL,
  FOREIGN KEY (object_key) REFERENCES r2_objects(object_key) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_battle_replays_recorded
  ON battle_replays(recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_battle_replays_player
  ON battle_replays(player_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_battle_replays_opponent
  ON battle_replays(opponent_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_battle_replays_run
  ON battle_replays(run_id);

CREATE TABLE IF NOT EXISTS battle_replay_cards (
  object_key TEXT NOT NULL,
  battle_id TEXT NOT NULL,
  battle_day INTEGER NULL,
  recorded_at_utc TEXT NOT NULL,
  side TEXT NOT NULL,
  card_group TEXT NOT NULL,
  result TEXT NULL,
  card_name TEXT NOT NULL,
  enchant TEXT NOT NULL,
  tier TEXT NOT NULL,
  slot_index INTEGER NOT NULL,
  battle_card_key TEXT NOT NULL,
  FOREIGN KEY (object_key) REFERENCES r2_objects(object_key) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_battle_replay_cards_side_day
  ON battle_replay_cards(side, battle_day);

CREATE INDEX IF NOT EXISTS idx_battle_replay_cards_card_lookup
  ON battle_replay_cards(side, card_name, enchant, tier);

CREATE INDEX IF NOT EXISTS idx_battle_replay_cards_battle
  ON battle_replay_cards(battle_id);

CREATE TABLE IF NOT EXISTS run_summaries (
  object_key TEXT PRIMARY KEY,
  run_id TEXT NOT NULL,
  client_id TEXT NULL,
  status TEXT NOT NULL,
  hero_id TEXT NULL,
  hero_name TEXT NULL,
  started_at_utc TEXT NULL,
  ended_at_utc TEXT NOT NULL,
  final_day INTEGER NULL,
  final_wins INTEGER NULL,
  final_losses INTEGER NULL,
  mmr INTEGER NULL,
  summary_schema_version INTEGER NULL,
  FOREIGN KEY (object_key) REFERENCES r2_objects(object_key) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_run_summaries_ended
  ON run_summaries(ended_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_run_summaries_status
  ON run_summaries(status, ended_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_run_summaries_run_id
  ON run_summaries(run_id);

CREATE VIEW IF NOT EXISTS run_summaries_latest AS
SELECT rs.*
FROM run_summaries rs
WHERE rs.object_key = (
  SELECT rs2.object_key
  FROM run_summaries rs2
  JOIN r2_objects ro2
    ON ro2.object_key = rs2.object_key
  WHERE rs2.run_id = rs.run_id
  ORDER BY rs2.ended_at_utc DESC, ro2.mtime_utc DESC, rs2.object_key DESC
  LIMIT 1
);
