#nullable enable
using System.Reflection;

var factoryType = RequireType("BazaarPlusPlus.Game.RunLogging.RunIdFactory");
var createMethod = factoryType.GetMethod(
    "Create",
    BindingFlags.Public | BindingFlags.Static,
    [typeof(DateTimeOffset), typeof(string), typeof(string), typeof(int?), typeof(string)]
);
Assert(createMethod != null, "RunIdFactory should expose Create(startedAtUtc, hero, gameMode, seed, nonce).");

var startedAt = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
var runId = (string?)createMethod!.Invoke(null, [startedAt, "Vanessa", "Ranked", 42, "deadbeef"]);

Assert(!string.IsNullOrWhiteSpace(runId), "RunIdFactory should return a run id.");
Assert(runId!.StartsWith("run_"), $"Run id should start with run_, got: {runId}");
Assert(runId.Contains("20260315t121530z"), $"Run id should include UTC start time fragment, got: {runId}");
Assert(runId.Contains("vanessa"), $"Run id should include normalized hero fragment, got: {runId}");
Assert(runId.Contains("ranked"), $"Run id should include normalized mode fragment, got: {runId}");
Assert(runId.Contains("002a"), $"Run id should include a readable seed fragment, got: {runId}");
Assert(runId.EndsWith("deadbeef"), $"Run id should include the nonce fragment, got: {runId}");

var schemaType = RequireType("BazaarPlusPlus.Game.RunLogging.Json.RunLogJsonSchema");
Assert(GetStaticValue<int>(schemaType, "CurrentSchemaVersion") == 1, "Schema version should start at 1.");
Assert(GetStaticValue<string>(schemaType, "MetaFileName") == "meta.json", "Meta file name mismatch.");
Assert(GetStaticValue<string>(schemaType, "EventsFileName") == "events.ndjson", "Events file name mismatch.");
Assert(GetStaticValue<string>(schemaType, "CheckpointFileName") == "checkpoint.json", "Checkpoint file name mismatch.");
Assert(GetStaticValue<string>(schemaType, "StatusFileName") == "status.json", "Status file name mismatch.");
Assert(GetStaticValue<string>(schemaType, "ActiveRunFileName") == "active-run.json", "Active-run file name mismatch.");
Assert(GetStaticValue<string>(schemaType, "RunsIndexFileName") == "runs-index.json", "Runs index file name mismatch.");

Console.WriteLine("RunLogging JSON schema checks passed.");

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
