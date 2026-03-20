#nullable enable
using System.Reflection;

var schemaType = RequireType(
    "BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite.RunLogSqliteSchema"
);

Assert(
    GetStaticValue<string>(schemaType, "DatabaseFileName") == "bazaarplusplus.db",
    "Database file name mismatch."
);
Assert(GetStaticValue<string>(schemaType, "RunsTableName") == "runs", "Runs table name mismatch.");
Assert(
    GetStaticValue<string>(schemaType, "RunEventsTableName") == "run_events",
    "Run events table name mismatch."
);
Assert(
    GetStaticValue<string>(schemaType, "RunCheckpointsTableName") == "run_checkpoints",
    "Run checkpoints table name mismatch."
);
Assert(
    GetStaticValue<string>(schemaType, "RunStatusTableName") == "run_status",
    "Run status table name mismatch."
);
Assert(
    GetStaticValue<string>(schemaType, "PvpBattlesTableName") == "pvp_battles",
    "PVP battles table name mismatch."
);

var bootstrapSql = GetStaticValue<string>(schemaType, "BootstrapSql");
Assert(!string.IsNullOrWhiteSpace(bootstrapSql), "Bootstrap SQL should not be empty.");
Assert(
    bootstrapSql.Contains("CREATE TABLE", StringComparison.Ordinal),
    "Bootstrap SQL should create tables."
);
Assert(
    bootstrapSql.Contains("runs", StringComparison.Ordinal),
    "Bootstrap SQL should define runs."
);
Assert(
    bootstrapSql.Contains("run_events", StringComparison.Ordinal),
    "Bootstrap SQL should define run_events."
);
Assert(
    bootstrapSql.Contains("run_checkpoints", StringComparison.Ordinal),
    "Bootstrap SQL should define run_checkpoints."
);
Assert(
    bootstrapSql.Contains("run_status", StringComparison.Ordinal),
    "Bootstrap SQL should define run_status."
);
Assert(
    bootstrapSql.Contains("pvp_battles", StringComparison.Ordinal),
    "Bootstrap SQL should define pvp_battles."
);
Assert(
    !bootstrapSql.Contains("replay_id", StringComparison.Ordinal),
    "Bootstrap SQL should not retain replay_id in pvp_battles."
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
        && bootstrapSql.Contains("result", StringComparison.Ordinal),
    "Bootstrap SQL should define PVP battle player identity and outcome columns."
);
Assert(
    bootstrapSql.Contains("player_hand_json", StringComparison.Ordinal)
        && bootstrapSql.Contains("player_skills_json", StringComparison.Ordinal)
        && bootstrapSql.Contains("opponent_hand_json", StringComparison.Ordinal)
        && bootstrapSql.Contains("opponent_skills_json", StringComparison.Ordinal),
    "Bootstrap SQL should define snapshot capture JSON columns."
);

Console.WriteLine("RunLogging SQLite schema checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
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
