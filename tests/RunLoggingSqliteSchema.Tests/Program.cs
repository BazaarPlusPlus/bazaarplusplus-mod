#nullable enable
using System.Reflection;

var schemaType = RequireType(
    "BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite.RunLogSqliteSchema"
);

Assert(
    GetStaticValue<string>(schemaType, "DatabaseFileName") == "run-logs.db",
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
