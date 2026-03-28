import type { Env } from "../env";

async function ensureOptionalColumn(
  env: Env,
  tableName: string,
  columnName: string,
  columnDefinition: string,
): Promise<void> {
  try {
    await env.DB.prepare(
      `ALTER TABLE ${tableName} ADD COLUMN ${columnName} ${columnDefinition}`,
    ).run();
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (
      !message.includes("duplicate column name") &&
      !message.includes("already exists")
    ) {
      throw error;
    }
  }
}

export async function ensureSchema(env: Env): Promise<void> {
  await env.DB.prepare(
    `
      CREATE TABLE IF NOT EXISTS registered_clients (
        client_id TEXT PRIMARY KEY,
        install_id TEXT NOT NULL,
        purpose TEXT NOT NULL,
        modulus_b64 TEXT NOT NULL,
        exponent_b64 TEXT NOT NULL,
        plugin_version TEXT NULL,
        registered_at_utc TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS replay_uploads (
        battle_id TEXT PRIMARY KEY,
        client_id TEXT NOT NULL,
        install_id TEXT NOT NULL,
        run_id TEXT NULL,
        payload_sha256 TEXT NOT NULL,
        object_key TEXT NOT NULL,
        uploaded_at_utc TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS run_uploads (
        run_id TEXT PRIMARY KEY,
        client_id TEXT NOT NULL,
        install_id TEXT NOT NULL,
        payload_sha256 TEXT NOT NULL,
        payload_json TEXT NOT NULL,
        uploaded_at_utc TEXT NOT NULL,
        projection_status TEXT NOT NULL,
        projected_at_utc TEXT NULL,
        projection_error TEXT NULL
      );

      CREATE TABLE IF NOT EXISTS request_nonces (
        nonce_key TEXT PRIMARY KEY,
        created_at_utc TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS client_uid_bindings (
        binding_id TEXT PRIMARY KEY,
        client_id TEXT NOT NULL,
        uid TEXT NOT NULL,
        bound_at_utc TEXT NOT NULL,
        unbound_at_utc TEXT NULL
      );

      CREATE TABLE IF NOT EXISTS uid_player_accounts (
        uid TEXT NOT NULL,
        player_account_id TEXT NOT NULL,
        first_seen_at_utc TEXT NOT NULL,
        last_seen_at_utc TEXT NOT NULL,
        last_client_id TEXT NOT NULL,
        PRIMARY KEY (uid, player_account_id)
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
        payload_json TEXT NOT NULL,
        created_at_utc TEXT NOT NULL,
        updated_at_utc TEXT NOT NULL
      );

      CREATE INDEX IF NOT EXISTS idx_client_uid_bindings_uid
        ON client_uid_bindings(uid);

      CREATE INDEX IF NOT EXISTS idx_client_uid_bindings_uid_active
        ON client_uid_bindings(uid, unbound_at_utc);

      CREATE INDEX IF NOT EXISTS idx_client_uid_bindings_client
        ON client_uid_bindings(client_id);

      CREATE UNIQUE INDEX IF NOT EXISTS idx_client_uid_bindings_client_active
        ON client_uid_bindings(client_id)
        WHERE unbound_at_utc IS NULL;

      CREATE INDEX IF NOT EXISTS idx_uid_player_accounts_uid
        ON uid_player_accounts(uid);

      CREATE INDEX IF NOT EXISTS idx_uid_player_accounts_account
        ON uid_player_accounts(player_account_id);

      CREATE INDEX IF NOT EXISTS idx_run_uploads_projection_status
        ON run_uploads(projection_status, uploaded_at_utc);

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
    `,
  ).run();

  await ensureOptionalColumn(env, "pvp_battles", "player_hero", "TEXT NULL");
  await ensureOptionalColumn(env, "pvp_battles", "player_level", "INTEGER NULL");
}
