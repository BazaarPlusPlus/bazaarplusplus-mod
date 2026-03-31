-- Initial production schema for bazaarplusplus-mod-api

CREATE TABLE IF NOT EXISTS registered_clients (
  client_id TEXT PRIMARY KEY,
  install_id TEXT NOT NULL,
  purpose TEXT NOT NULL,
  modulus_b64 TEXT NOT NULL,
  exponent_b64 TEXT NOT NULL,
  plugin_version TEXT NULL,
  registered_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS client_player_account_bindings (
  binding_id TEXT PRIMARY KEY,
  client_id TEXT NOT NULL,
  player_account_id TEXT NOT NULL,
  binding_source TEXT NOT NULL,
  confidence INTEGER NOT NULL,
  bound_at_utc TEXT NOT NULL,
  unbound_at_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS client_player_account_observations (
  client_id TEXT NOT NULL,
  player_account_id TEXT NOT NULL,
  first_seen_at_utc TEXT NOT NULL,
  last_seen_at_utc TEXT NOT NULL,
  evidence_count INTEGER NOT NULL DEFAULT 1,
  PRIMARY KEY (client_id, player_account_id)
);

CREATE TABLE IF NOT EXISTS run_uploads (
  run_id TEXT PRIMARY KEY,
  client_id TEXT NOT NULL,
  install_id TEXT NOT NULL,
  payload_sha256 TEXT NOT NULL,
  payload_object_key TEXT NULL,
  payload_bytes INTEGER NULL,
  schema_version INTEGER NULL,
  projection_status TEXT NOT NULL,
  projected_at_utc TEXT NULL,
  last_error_code TEXT NULL,
  last_error_detail TEXT NULL,
  created_at_utc TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS pvp_battles (
  battle_id TEXT PRIMARY KEY,
  run_id TEXT NULL,
  source_client_id TEXT NOT NULL,
  recorded_at_utc TEXT NOT NULL,
  day INTEGER NULL,
  hour INTEGER NULL,
  encounter_id TEXT NULL,
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
  replay_available INTEGER NOT NULL DEFAULT 0,
  replay_object_key TEXT NULL,
  replay_uploaded_at_utc TEXT NULL,
  created_at_utc TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_client_player_account_bindings_account
  ON client_player_account_bindings(player_account_id);

CREATE INDEX IF NOT EXISTS idx_client_player_account_bindings_client
  ON client_player_account_bindings(client_id);

CREATE INDEX IF NOT EXISTS idx_client_player_account_bindings_account_active
  ON client_player_account_bindings(player_account_id, unbound_at_utc);

CREATE UNIQUE INDEX IF NOT EXISTS idx_client_player_account_bindings_client_active
  ON client_player_account_bindings(client_id)
  WHERE unbound_at_utc IS NULL;

CREATE INDEX IF NOT EXISTS idx_client_player_account_observations_client_recent
  ON client_player_account_observations(client_id, last_seen_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_client_player_account_observations_account
  ON client_player_account_observations(player_account_id);

CREATE INDEX IF NOT EXISTS idx_run_uploads_projection_status
  ON run_uploads(projection_status, updated_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_run_uploads_client_created
  ON run_uploads(client_id, created_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_run_uploads_payload_sha256
  ON run_uploads(payload_sha256);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_recorded_at_utc
  ON pvp_battles(recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_opponent_recent
  ON pvp_battles(opponent_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_player_recent
  ON pvp_battles(player_account_id, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_combat_kind_recent
  ON pvp_battles(combat_kind, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_opponent_kind_result_recent
  ON pvp_battles(opponent_account_id, combat_kind, result, recorded_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_pvp_battles_source_run
  ON pvp_battles(source_client_id, run_id);
