#nullable enable
namespace BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

public static class RunLogSqliteSchema
{
    public static int CurrentSchemaVersion => 1;

    public static string DatabaseFileName => "bazaarplusplus.db";

    public static string RunsTableName => "runs";

    public static string RunEventsTableName => "run_events";

    public static string RunCheckpointsTableName => "run_checkpoints";

    public static string RunStatusTableName => "run_status";

    public static string PvpBattlesTableName => "pvp_battles";

    public static string GhostBattlesTableName => "ghost_battles";

    public static string RunSyncStateTableName => "run_sync_state";

    public static string ReplaySyncStateTableName => "replay_sync_state";

    public static string BootstrapSql =>
        $"""
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS {RunsTableName} (
                run_id TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL,
                started_at_utc TEXT NOT NULL,
                hero TEXT NOT NULL,
                game_mode TEXT NOT NULL,
                player_rank TEXT NULL,
                player_rating INTEGER NULL,
                day INTEGER NULL,
                hour INTEGER NULL,
                seed INTEGER NULL,
                status TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {RunEventsTableName} (
                run_id TEXT NOT NULL,
                seq INTEGER NOT NULL,
                ts_utc TEXT NOT NULL,
                kind TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                PRIMARY KEY (run_id, seq),
                FOREIGN KEY (run_id) REFERENCES {RunsTableName}(run_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS {RunCheckpointsTableName} (
                run_id TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL,
                last_seq INTEGER NOT NULL,
                last_seen_at_utc TEXT NOT NULL,
                day INTEGER NULL,
                hour INTEGER NULL,
                max_health INTEGER NULL,
                prestige INTEGER NULL,
                level INTEGER NULL,
                income INTEGER NULL,
                gold INTEGER NULL,
                state TEXT NULL,
                current_encounter_id TEXT NULL,
                last_state_fingerprint TEXT NULL,
                last_selection_fingerprint TEXT NULL,
                pending_selection_seq INTEGER NULL,
                pending_selection_json TEXT NULL,
                completed INTEGER NOT NULL,
                FOREIGN KEY (run_id) REFERENCES {RunsTableName}(run_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS {RunStatusTableName} (
                run_id TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL,
                status TEXT NOT NULL,
                ended_at_utc TEXT NOT NULL,
                final_day INTEGER NULL,
                final_hour INTEGER NULL,
                max_health INTEGER NULL,
                prestige INTEGER NULL,
                level INTEGER NULL,
                income INTEGER NULL,
                gold INTEGER NULL,
                victories INTEGER NULL,
                losses INTEGER NULL,
                reason TEXT NULL,
                FOREIGN KEY (run_id) REFERENCES {RunsTableName}(run_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS {PvpBattlesTableName} (
                battle_id TEXT PRIMARY KEY,
                run_id TEXT NULL,
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
                opponent_hero TEXT NULL,
                opponent_rank TEXT NULL,
                opponent_rating INTEGER NULL,
                opponent_level INTEGER NULL,
                opponent_account_id TEXT NULL,
                combat_kind TEXT NOT NULL,
                result TEXT NULL,
                winner_combatant_id TEXT NULL,
                loser_combatant_id TEXT NULL,
                player_hand_json TEXT NOT NULL,
                player_skills_json TEXT NOT NULL,
                opponent_hand_json TEXT NOT NULL,
                opponent_skills_json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {GhostBattlesTableName} (
                battle_id TEXT PRIMARY KEY,
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
                opponent_hero TEXT NULL,
                opponent_rank TEXT NULL,
                opponent_rating INTEGER NULL,
                opponent_level INTEGER NULL,
                opponent_account_id TEXT NULL,
                combat_kind TEXT NOT NULL,
                result TEXT NULL,
                winner_combatant_id TEXT NULL,
                loser_combatant_id TEXT NULL,
                player_hand_json TEXT NOT NULL,
                player_skills_json TEXT NOT NULL,
                opponent_hand_json TEXT NOT NULL,
                opponent_skills_json TEXT NOT NULL,
                replay_available INTEGER NOT NULL DEFAULT 0,
                replay_downloaded INTEGER NOT NULL DEFAULT 0,
                last_synced_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {RunSyncStateTableName} (
                run_id TEXT PRIMARY KEY,
                dirty INTEGER NOT NULL,
                uploaded_seq INTEGER NULL,
                uploaded_status TEXT NULL,
                last_attempt_at_utc TEXT NULL,
                last_uploaded_at_utc TEXT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                FOREIGN KEY (run_id) REFERENCES {RunsTableName}(run_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS {ReplaySyncStateTableName} (
                battle_id TEXT PRIMARY KEY,
                dirty INTEGER NOT NULL,
                payload_sha256 TEXT NULL,
                object_key TEXT NULL,
                last_attempt_at_utc TEXT NULL,
                last_uploaded_at_utc TEXT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                FOREIGN KEY (battle_id) REFERENCES {PvpBattlesTableName}(battle_id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_{RunEventsTableName}_ts_utc
                ON {RunEventsTableName}(ts_utc);

            CREATE INDEX IF NOT EXISTS idx_{RunCheckpointsTableName}_last_seen_at_utc
                ON {RunCheckpointsTableName}(last_seen_at_utc);

            CREATE INDEX IF NOT EXISTS idx_{PvpBattlesTableName}_run_id
                ON {PvpBattlesTableName}(run_id);

            CREATE INDEX IF NOT EXISTS idx_{PvpBattlesTableName}_recorded_at_utc
                ON {PvpBattlesTableName}(recorded_at_utc);

            CREATE INDEX IF NOT EXISTS idx_{GhostBattlesTableName}_recorded_at_utc
                ON {GhostBattlesTableName}(recorded_at_utc DESC);

            CREATE INDEX IF NOT EXISTS idx_{RunSyncStateTableName}_dirty
                ON {RunSyncStateTableName}(dirty, last_attempt_at_utc);

            CREATE INDEX IF NOT EXISTS idx_{ReplaySyncStateTableName}_dirty
                ON {ReplaySyncStateTableName}(dirty, last_attempt_at_utc);
            """;
}
