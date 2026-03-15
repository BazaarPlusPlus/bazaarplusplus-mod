#nullable enable
using System.Reflection;
using System.Text.Json;
using BazaarPlusPlus.Game.RunLogging.Models;

var storeType = RequireType("BazaarPlusPlus.Game.RunLogging.Persistence.JsonRunLogStore");
var ctor = storeType.GetConstructor([typeof(string)]);
Assert(ctor != null, "JsonRunLogStore should expose a constructor taking the log root path.");

var tempRoot = Path.Combine(Path.GetTempPath(), "bpp-run-log-recovery-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    var startedAt = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
    const string runId = "run_20260315t121530z_vanessa_ranked_002a_deadbeef";

    var firstStore = ctor!.Invoke([tempRoot]);
    Invoke<RunLogSessionState>(
        storeType,
        firstStore,
        "CreateRun",
        [
            new RunLogCreateRequest
            {
                SchemaVersion = 1,
                RunId = runId,
                StartedAtUtc = startedAt,
                Hero = "Vanessa",
                GameMode = "Ranked",
                Day = 1,
                Hour = 1,
                Seed = 42,
            },
        ]
    );
    InvokeVoid(
        storeType,
        firstStore,
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
            },
        ]
    );
    InvokeVoid(
        storeType,
        firstStore,
        "SaveCheckpoint",
        [
            runId,
            new RunLogCheckpoint
            {
                SchemaVersion = 1,
                RunId = runId,
                LastSeq = 1,
                LastSeenAtUtc = startedAt.AddSeconds(10),
                Day = 2,
                Hour = 1,
                State = "Choice",
                CurrentEncounterId = "encounter-123",
                LastStateFingerprint = "state-fp-1",
                LastSelectionFingerprint = "selection-fp-1",
                PendingSelectionSeq = 1,
                Completed = false,
            },
        ]
    );

    var resumedStore = ctor.Invoke([tempRoot]);
    var resumed = Invoke<RunLogSessionState?>(storeType, resumedStore, "TryResumeActiveRun", []);

    Assert(resumed != null, "TryResumeActiveRun should restore an unfinished run.");
    Assert(resumed!.RunId == runId, "Recovered session should preserve run_id.");
    Assert(resumed.LastSeq == 1, "Recovered session should preserve last_seq.");
    Assert(resumed.Day == 2, "Recovered session should preserve checkpoint day.");
    Assert(resumed.Hour == 1, "Recovered session should preserve checkpoint hour.");
    Assert(resumed.State == "Choice", "Recovered session should preserve checkpoint state.");
    Assert(
        resumed.CurrentEncounterId == "encounter-123",
        "Recovered session should preserve encounter id."
    );
    Assert(
        resumed.LastStateFingerprint == "state-fp-1",
        "Recovered session should preserve state dedupe anchor."
    );
    Assert(
        resumed.LastSelectionFingerprint == "selection-fp-1",
        "Recovered session should preserve selection dedupe anchor."
    );
    Assert(
        resumed.PendingSelectionSeq == 1,
        "Recovered session should preserve pending selection sequence."
    );

    InvokeVoid(
        storeType,
        resumedStore,
        "MarkRunAbandoned",
        [
            runId,
            new RunLogAbandonment
            {
                SchemaVersion = 1,
                RunId = runId,
                Status = "abandoned",
                EndedAtUtc = startedAt.AddMinutes(20),
                FinalDay = 2,
                FinalHour = 1,
                Reason = "process_exit",
            },
        ]
    );

    var runDirectory = Path.Combine(tempRoot, "2026-03-15", runId);
    var statusPath = Path.Combine(runDirectory, "status.json");
    Assert(File.Exists(statusPath), "MarkRunAbandoned should persist status.json.");
    Assert(ReadJsonString(statusPath, "status") == "abandoned", "status.json should mark the run abandoned.");
    Assert(
        !File.Exists(Path.Combine(tempRoot, "active-run.json")),
        "MarkRunAbandoned should clear active-run tracking."
    );
}
finally
{
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("RunLogging recovery checks passed.");

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

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
