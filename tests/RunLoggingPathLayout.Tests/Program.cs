#nullable enable
using System.Reflection;

var layoutType = RequireType("BazaarPlusPlus.Game.RunLogging.Json.RunLogPathLayout");
var ctor = layoutType.GetConstructor([typeof(string)]);
Assert(ctor != null, "RunLogPathLayout should expose a constructor taking the log root path.");

var layout = ctor!.Invoke(["/tmp/run-logs"]);
var startedAt = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
const string runId = "run_20260315t121530z_vanessa_ranked_002a_deadbeef";

Assert(
    GetProperty<string>(layoutType, layout, "LogRootPath") == "/tmp/run-logs",
    "Log root path should round-trip from the constructor."
);
Assert(
    Invoke<string>(layoutType, layout, "GetDatePartition", [startedAt]) == "2026-03-15",
    "Date partition should use yyyy-MM-dd."
);
Assert(
    Invoke<string>(layoutType, layout, "GetRunDirectoryPath", [startedAt, runId])
        == "/tmp/run-logs/2026-03-15/run_20260315t121530z_vanessa_ranked_002a_deadbeef",
    "Run directory path should be rooted under <date>/<run_id>."
);
Assert(
    Invoke<string>(layoutType, layout, "GetMetaFilePath", [startedAt, runId])
        == "/tmp/run-logs/2026-03-15/run_20260315t121530z_vanessa_ranked_002a_deadbeef/meta.json",
    "Meta path mismatch."
);
Assert(
    Invoke<string>(layoutType, layout, "GetEventsFilePath", [startedAt, runId])
        == "/tmp/run-logs/2026-03-15/run_20260315t121530z_vanessa_ranked_002a_deadbeef/events.ndjson",
    "Events path mismatch."
);
Assert(
    Invoke<string>(layoutType, layout, "GetCheckpointFilePath", [startedAt, runId])
        == "/tmp/run-logs/2026-03-15/run_20260315t121530z_vanessa_ranked_002a_deadbeef/checkpoint.json",
    "Checkpoint path mismatch."
);
Assert(
    Invoke<string>(layoutType, layout, "GetStatusFilePath", [startedAt, runId])
        == "/tmp/run-logs/2026-03-15/run_20260315t121530z_vanessa_ranked_002a_deadbeef/status.json",
    "Status path mismatch."
);
Assert(
    Invoke<string>(layoutType, layout, "GetActiveRunFilePath", []) == "/tmp/run-logs/active-run.json",
    "Active-run helper file path mismatch."
);
Assert(
    Invoke<string>(layoutType, layout, "GetRunsIndexFilePath", []) == "/tmp/run-logs/runs-index.json",
    "Runs index helper file path mismatch."
);

Console.WriteLine("RunLogging path layout checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static T GetProperty<T>(Type type, object instance, string name)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    return (T)property.GetValue(instance)!;
}

static T Invoke<T>(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    return (T)method.Invoke(instance, args)!;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
