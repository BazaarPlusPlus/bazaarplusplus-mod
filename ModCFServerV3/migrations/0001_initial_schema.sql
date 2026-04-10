-- V3 schema for bazaarplusplus-mod-api-v3

CREATE TABLE IF NOT EXISTS users (
  player_account_id TEXT PRIMARY KEY,
  player_username TEXT NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  stream_platform TEXT NULL,
  stream_channel_id TEXT NULL,
  stream_url TEXT NULL,
  created_at_utc TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL,
  last_login_at_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS installations (
  installation_id TEXT PRIMARY KEY,
  player_account_id TEXT NOT NULL,
  public_key TEXT NOT NULL,
  status TEXT NOT NULL,
  created_at_utc TEXT NOT NULL,
  last_seen_at_utc TEXT NULL,
  revoked_at_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS installation_sessions (
  session_id TEXT PRIMARY KEY,
  player_account_id TEXT NOT NULL,
  created_at_utc TEXT NOT NULL,
  expires_at_utc TEXT NOT NULL,
  revoked_at_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS installation_observations (
  installation_id TEXT NOT NULL,
  observed_player_account_id TEXT NOT NULL,
  observed_player_username TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL,
  signature TEXT NOT NULL,
  status TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS run_bundles (
  bundle_id TEXT PRIMARY KEY,
  installation_id TEXT NOT NULL,
  player_account_id TEXT NOT NULL,
  run_id TEXT NOT NULL,
  payload_hash TEXT NOT NULL,
  schema_version INTEGER NOT NULL,
  object_key TEXT NOT NULL,
  codec TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  submitted_at_utc TEXT NOT NULL,
  created_at_utc TEXT NOT NULL,
  UNIQUE (installation_id, run_id, payload_hash)
);

CREATE TABLE IF NOT EXISTS runs (
  run_id TEXT PRIMARY KEY,
  installation_id TEXT NOT NULL,
  player_account_id TEXT NOT NULL,
  bundle_id TEXT NOT NULL,
  status TEXT NOT NULL,
  hero_id TEXT NULL,
  hero_name TEXT NULL,
  player_rank TEXT NULL,
  player_rating INTEGER NULL,
  player_position INTEGER NULL,
  started_at_utc TEXT NULL,
  ended_at_utc TEXT NOT NULL,
  final_day INTEGER NULL,
  final_wins INTEGER NULL,
  final_losses INTEGER NULL,
  final_player_rank TEXT NULL,
  final_player_rating INTEGER NULL,
  final_player_position INTEGER NULL,
  updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS battles (
  battle_id TEXT PRIMARY KEY,
  run_id TEXT NOT NULL,
  installation_id TEXT NOT NULL,
  player_account_id TEXT NOT NULL,
  bundle_id TEXT NOT NULL,
  recorded_at_utc TEXT NOT NULL,
  day INTEGER NULL,
  player_name TEXT NULL,
  player_account_id_in_payload TEXT NULL,
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
  result TEXT NULL,
  replay_available INTEGER NOT NULL,
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

CREATE INDEX IF NOT EXISTS idx_battles_opponent_recorded
  ON battles(opponent_account_id, recorded_at_utc DESC, battle_id DESC);
