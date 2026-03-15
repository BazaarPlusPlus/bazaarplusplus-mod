#nullable enable
using System.Reflection;
using System.Text.Json;
using BazaarPlusPlus.Game.RunLogging.Models;

var storeType = RequireType("BazaarPlusPlus.Game.RunLogging.Persistence.JsonRunLogStore");
var ctor = storeType.GetConstructor([typeof(string)]);
Assert(ctor != null, "JsonRunLogStore should expose a constructor taking the log root path.");

var tempRoot = Path.Combine(Path.GetTempPath(), "bpp-run-log-store-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    var store = ctor!.Invoke([tempRoot]);
    var startedAt = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
    const string runId = "run_20260315t121530z_vanessa_ranked_002a_deadbeef";

    var createRequest = new RunLogCreateRequest
    {
        SchemaVersion = 1,
        RunId = runId,
        StartedAtUtc = startedAt,
        Hero = "Vanessa",
        GameMode = "Ranked",
        Day = 1,
        Hour = 1,
        Seed = 42,
    };

    var sessionState = Invoke<RunLogSessionState>(storeType, store, "CreateRun", [createRequest]);
    Assert(sessionState.RunId == runId, "CreateRun should return the created run id.");

    var activeRunPath = Path.Combine(tempRoot, "active-run.json");
    Assert(File.Exists(activeRunPath), "CreateRun should create active-run.json.");
    Assert(ReadJsonString(activeRunPath, "run_id") == runId, "active-run.json should reference the active run.");

    InvokeVoid(
        storeType,
        store,
        "AppendEvent",
        [
            runId,
            new RunLogEvent
            {
                SchemaVersion = 1,
                RunId = runId,
                Seq = 1,
                Ts = startedAt,
                Kind = "run_started",
                Day = 1,
                Hour = 1,
                Hero = "Vanessa",
                GameMode = "Ranked",
            },
        ]
    );
    InvokeVoid(
        storeType,
        store,
        "AppendEvent",
        [
            runId,
            new RunLogEvent
            {
                SchemaVersion = 1,
                RunId = runId,
                Seq = 2,
                Ts = startedAt.AddSeconds(5),
                Kind = "run_progress",
                Day = 1,
                Hour = 2,
                Victories = 1,
                Losses = 0,
            },
        ]
    );

    InvokeVoid(
        storeType,
        store,
        "SaveCheckpoint",
        [
            runId,
            new RunLogCheckpoint
            {
                SchemaVersion = 1,
                RunId = runId,
                LastSeq = 2,
                LastSeenAtUtc = startedAt.AddSeconds(5),
                Day = 1,
                Hour = 2,
                State = "Encounter",
                Completed = false,
            },
        ]
    );

    Assert(ReadJsonInt32(activeRunPath, "last_seq") == 2, "active-run.json should track the latest sequence.");

    InvokeVoid(
        storeType,
        store,
        "CompleteRun",
        [
            runId,
            new RunLogCompletion
            {
                SchemaVersion = 1,
                RunId = runId,
                Status = "completed",
                EndedAtUtc = startedAt.AddMinutes(15),
                FinalDay = 3,
                FinalHour = 1,
                Victories = 10,
                Losses = 1,
                Reason = "run_end_event",
            },
        ]
    );

    var runDirectory = Path.Combine(tempRoot, "2026-03-15", runId);
    Assert(Directory.Exists(runDirectory), "Run directory should exist.");

    var metaPath = Path.Combine(runDirectory, "meta.json");
    Assert(File.Exists(metaPath), "meta.json should exist.");
    Assert(ReadJsonString(metaPath, "run_id") == runId, "meta.json should persist run_id.");

    var eventsPath = Path.Combine(runDirectory, "events.ndjson");
    Assert(File.Exists(eventsPath), "events.ndjson should exist.");
    Assert(File.ReadAllLines(eventsPath).Length == 2, "events.ndjson should contain exactly two events.");

    var checkpointPath = Path.Combine(runDirectory, "checkpoint.json");
    Assert(File.Exists(checkpointPath), "checkpoint.json should exist.");
    Assert(ReadJsonInt32(checkpointPath, "last_seq") == 2, "checkpoint.json should persist last_seq.");

    var statusPath = Path.Combine(runDirectory, "status.json");
    Assert(File.Exists(statusPath), "status.json should exist.");
    Assert(ReadJsonString(statusPath, "status") == "completed", "status.json should persist terminal status.");

    Assert(!File.Exists(activeRunPath), "CompleteRun should clear active-run tracking.");
}
finally
{
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("RunLogging JSON store checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static T Invoke<T>(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    return (T)method.Invoke(instance, args)!;
}

static void InvokeVoid(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    method.Invoke(instance, args);
}

static string ReadJsonString(string path, string propertyName)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    return document.RootElement.GetProperty(propertyName).GetString()
        ?? throw new InvalidOperationException($"Property {propertyName} was null in {path}");
}

static int ReadJsonInt32(string path, string propertyName)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    return document.RootElement.GetProperty(propertyName).GetInt32();
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
