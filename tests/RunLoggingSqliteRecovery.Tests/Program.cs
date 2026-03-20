#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.RunLogging.Models;
using Microsoft.Data.Sqlite;

var storeType = RequireType("BazaarPlusPlus.Game.RunLogging.Persistence.SqliteRunLogStore");
var ctor = storeType.GetConstructor([typeof(string)]);
Assert(ctor != null, "SqliteRunLogStore should expose a constructor taking the database path.");

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-run-log-sqlite-recovery-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "run-logs.db");

try
{
    var startedAt = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
    const string runId = "run_20260315t121530z_vanessa_ranked_002a_deadbeef";

    var firstStore = ctor!.Invoke([dbPath]);
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
                PendingSelection = new RunLogPendingSelectionState
                {
                    Day = 2,
                    Hour = 1,
                    State = "Choice",
                    EncounterId = "encounter-123",
                    ParentEncounterId = "encounter-123",
                    SelectionSeq = 1,
                    Options =
                    [
                        new RunLogOptionSnapshot
                        {
                            InstanceId = "instance-a",
                            TemplateId = "template-a",
                            Name = "Recovered Choice",
                        },
                    ],
                },
                Completed = false,
            },
        ]
    );

    var resumedStore = ctor.Invoke([dbPath]);
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
    Assert(
        resumed.PendingSelection?.Options.Count == 1
            && resumed.PendingSelection.Options[0].TemplateId == "template-a",
        "Recovered session should preserve pending selection payload."
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

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetString(connection, "SELECT status FROM run_status WHERE run_id = $runId;", runId)
                == "abandoned",
            "run_status should mark the run abandoned."
        );
    }

    var resumedAfterAbandonment = Invoke<RunLogSessionState?>(
        storeType,
        resumedStore,
        "TryResumeActiveRun",
        []
    );
    Assert(
        resumedAfterAbandonment == null,
        "Abandoned runs should no longer be returned by TryResumeActiveRun."
    );
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("RunLogging SQLite recovery checks passed.");

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

static string GetString(SqliteConnection connection, string sql, string runId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$runId", runId);
    return (string)(
        command.ExecuteScalar()
        ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
