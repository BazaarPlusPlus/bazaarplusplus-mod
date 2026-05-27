#nullable enable
using System.Reflection;

var schemaType = RequireType("BazaarPlusPlus.Storage.RunLog.RunLogSchema");

Assert(
    GetStaticValue<int>(schemaType, "LocalDatabaseSchemaVersion") == 13,
    "Local database schema version mismatch."
);
Assert(
    GetStaticValue<int>(schemaType, "UploadPayloadSchemaVersion") == 1,
    "Upload payload schema version mismatch."
);
Assert(
    GetStaticValue<string>(schemaType, "DatabaseFileName") == "bazaarplusplus.db",
    "Database file name mismatch."
);
Assert(GetStaticValue<string>(schemaType, "RunsTableName") == "runs", "Runs table name mismatch.");
Assert(
    GetStaticValue<string>(schemaType, "BattlesTableName") == "battles",
    "Battles table name mismatch."
);
Assert(
    GetStaticValue<string>(schemaType, "BattleSnapshotsTableName") == "battle_snapshots",
    "Battle snapshots table name mismatch."
);
Assert(
    GetStaticValue<string>(schemaType, "RunScreenshotsTableName") == "run_screenshots",
    "Run screenshots table name mismatch."
);
Assert(
    GetStaticValue<string>(schemaType, "SyncCursorsTableName") == "sync_cursors",
    "Sync cursors table name mismatch."
);

var bootstrapSql = GetStaticValue<string>(schemaType, "BootstrapSql");
Assert(!string.IsNullOrWhiteSpace(bootstrapSql), "Bootstrap SQL should not be empty.");
Assert(
    bootstrapSql.Contains("CREATE TABLE", StringComparison.Ordinal),
    "Bootstrap SQL should create tables."
);
Assert(
    bootstrapSql.Contains("PRAGMA user_version = 13;", StringComparison.Ordinal),
    "Bootstrap SQL should set the SQLite user_version."
);
Assert(
    bootstrapSql.Contains("runs", StringComparison.Ordinal),
    "Bootstrap SQL should define runs."
);
Assert(
    bootstrapSql.Contains("battles", StringComparison.Ordinal),
    "Bootstrap SQL should define battles."
);
Assert(
    bootstrapSql.Contains("battle_snapshots", StringComparison.Ordinal),
    "Bootstrap SQL should define battle_snapshots."
);
Assert(
    bootstrapSql.Contains("run_screenshots", StringComparison.Ordinal),
    "Bootstrap SQL should define run_screenshots."
);
Assert(
    bootstrapSql.Contains("sync_cursors", StringComparison.Ordinal),
    "Bootstrap SQL should define sync_cursors."
);
Assert(
    !bootstrapSql.Contains("schema_version", StringComparison.Ordinal),
    "Bootstrap SQL should not keep row-level schema_version columns."
);
Assert(
    bootstrapSql.Contains("player_rank", StringComparison.Ordinal)
        && bootstrapSql.Contains("player_rating", StringComparison.Ordinal),
    "Bootstrap SQL should define run and battle rank/rating columns."
);
Assert(
    !bootstrapSql.Contains("pending_selection_json", StringComparison.Ordinal)
        && !bootstrapSql.Contains("last_state_fingerprint", StringComparison.Ordinal)
        && !bootstrapSql.Contains("last_selection_fingerprint", StringComparison.Ordinal),
    "Bootstrap SQL should not define removed run process-state columns."
);
Assert(
    bootstrapSql.Contains("player_name", StringComparison.Ordinal)
        && bootstrapSql.Contains("player_account_id", StringComparison.Ordinal)
        && bootstrapSql.Contains("opponent_name", StringComparison.Ordinal)
        && bootstrapSql.Contains("opponent_hero", StringComparison.Ordinal)
        && bootstrapSql.Contains("opponent_rank", StringComparison.Ordinal)
        && bootstrapSql.Contains("opponent_rating", StringComparison.Ordinal)
        && bootstrapSql.Contains("opponent_level", StringComparison.Ordinal)
        && bootstrapSql.Contains("opponent_account_id", StringComparison.Ordinal)
        && bootstrapSql.Contains("winner_combatant_id", StringComparison.Ordinal)
        && bootstrapSql.Contains("loser_combatant_id", StringComparison.Ordinal)
        && bootstrapSql.Contains("is_bundle_final_battle", StringComparison.Ordinal)
        && bootstrapSql.Contains("result", StringComparison.Ordinal),
    "Bootstrap SQL should define unified battle identity and outcome columns."
);
Assert(
    bootstrapSql.Contains("source TEXT NOT NULL", StringComparison.Ordinal)
        && bootstrapSql.Contains("has_local_payload", StringComparison.Ordinal)
        && bootstrapSql.Contains("replay_dirty", StringComparison.Ordinal),
    "Bootstrap SQL should inline battle source and replay sync state."
);
Assert(
    bootstrapSql.Contains("player_hand_json", StringComparison.Ordinal)
        && bootstrapSql.Contains("player_skills_json", StringComparison.Ordinal)
        && bootstrapSql.Contains("opponent_hand_json", StringComparison.Ordinal)
        && bootstrapSql.Contains("opponent_skills_json", StringComparison.Ordinal),
    "Bootstrap SQL should define battle snapshot JSON columns."
);
Assert(
    bootstrapSql.Contains("screenshot_id TEXT PRIMARY KEY", StringComparison.Ordinal)
        && bootstrapSql.Contains("capture_source TEXT NOT NULL", StringComparison.Ordinal)
        && bootstrapSql.Contains("image_relative_path TEXT NOT NULL", StringComparison.Ordinal)
        && bootstrapSql.Contains("captured_at_local", StringComparison.Ordinal)
        && bootstrapSql.Contains("captured_at_utc", StringComparison.Ordinal)
        && bootstrapSql.Contains("battle_id TEXT NULL", StringComparison.Ordinal)
        && bootstrapSql.Contains("hero_name TEXT NULL", StringComparison.Ordinal)
        && bootstrapSql.Contains("player_position INTEGER NULL", StringComparison.Ordinal)
        && bootstrapSql.Contains("is_primary INTEGER NOT NULL DEFAULT 0", StringComparison.Ordinal),
    "Bootstrap SQL should define screenshot metadata columns."
);
Assert(
    bootstrapSql.Contains(
        "FOREIGN KEY (run_id) REFERENCES runs(run_id) ON DELETE CASCADE",
        StringComparison.Ordinal
    )
        && bootstrapSql.Contains(
            "FOREIGN KEY (battle_id) REFERENCES battles(battle_id) ON DELETE CASCADE",
            StringComparison.Ordinal
        ),
    "Bootstrap SQL should enforce run and battle cascade relationships."
);
Assert(
    !bootstrapSql.Contains(
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_run_screenshots_battle_id",
        StringComparison.Ordinal
    )
        && bootstrapSql.Contains(
            "CREATE UNIQUE INDEX IF NOT EXISTS idx_run_screenshots_primary_run",
            StringComparison.Ordinal
        ),
    "Bootstrap SQL should only define the primary screenshot uniqueness index."
);

Console.WriteLine("RunLogging SQLite schema checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus.Storage")
        ?? Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static T GetStaticValue<T>(Type type, string name)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    return (T)property.GetValue(null)!;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
