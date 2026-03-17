#nullable enable
namespace BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;

public static class RunLogSqliteSchema
{
    public static int CurrentSchemaVersion => 1;

    public static string DatabaseFileName => "run-logs.db";

    public static string RunsTableName => "runs";

    public static string RunEventsTableName => "run_events";

    public static string RunCheckpointsTableName => "run_checkpoints";

    public static string RunStatusTableName => "run_status";

    public static string BootstrapSql =>
        $"""
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS {RunsTableName} (
                run_id TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL,
                started_at_utc TEXT NOT NULL,
                hero TEXT NOT NULL,
                game_mode TEXT NOT NULL,
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
                state TEXT NULL,
                current_encounter_id TEXT NULL,
                last_state_fingerprint TEXT NULL,
                last_selection_fingerprint TEXT NULL,
                pending_selection_seq INTEGER NULL,
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
                victories INTEGER NULL,
                losses INTEGER NULL,
                reason TEXT NULL,
                FOREIGN KEY (run_id) REFERENCES {RunsTableName}(run_id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_{RunEventsTableName}_ts_utc
                ON {RunEventsTableName}(ts_utc);

            CREATE INDEX IF NOT EXISTS idx_{RunCheckpointsTableName}_last_seen_at_utc
                ON {RunCheckpointsTableName}(last_seen_at_utc);
            """;
}
