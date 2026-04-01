-- Clean-break schema for bazaarplusplus-mod-api

CREATE TABLE IF NOT EXISTS clients (
  client_id TEXT PRIMARY KEY,
  install_id TEXT NOT NULL,
  modulus_b64 TEXT NOT NULL,
  exponent_b64 TEXT NOT NULL,
  plugin_version TEXT NULL,
  registered_at_utc TEXT NOT NULL,
  last_seen_at_utc TEXT NULL,
  revoked_at_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS player_links (
  client_id TEXT PRIMARY KEY,
  player_account_id TEXT NOT NULL,
  bound_at_utc TEXT NOT NULL,
  last_confirmed_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS runs (
  run_id TEXT PRIMARY KEY,
  client_id TEXT NOT NULL,
  player_account_id TEXT NULL,
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
  summary_object_key TEXT NULL,
  created_at_utc TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS battles (
  battle_id TEXT PRIMARY KEY,
  run_id TEXT NULL,
  client_id TEXT NOT NULL,
  uploader_player_account_id TEXT NULL,
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
  combat_kind TEXT NOT NULL,
  result TEXT NULL,
  winner_combatant_id TEXT NULL,
  loser_combatant_id TEXT NULL,
  replay_schema_version INTEGER NOT NULL,
  replay_object_key TEXT NOT NULL,
  replay_size_bytes INTEGER NOT NULL,
  created_at_utc TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS replay_tokens (
  token TEXT PRIMARY KEY,
  battle_id TEXT NOT NULL,
  requested_by_player_account_id TEXT NOT NULL,
  expires_at_utc TEXT NOT NULL,
  created_at_utc TEXT NOT NULL,
  used_at_utc TEXT NULL,
  revoked_at_utc TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_clients_install
  ON clients(install_id);

CREATE INDEX IF NOT EXISTS idx_player_links_player
  ON player_links(player_account_id);

CREATE INDEX IF NOT EXISTS idx_runs_player_ended
  ON runs(player_account_id, ended_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_runs_client_ended
  ON runs(client_id, ended_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_battles_opponent_recorded
  ON battles(opponent_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_battles_player_recorded
  ON battles(player_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_battles_run_recorded
  ON battles(run_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_battles_client_recorded
  ON battles(client_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_replay_tokens_expires
  ON replay_tokens(expires_at_utc);
