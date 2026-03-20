#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.RunLogging.Models;
using Microsoft.Data.Sqlite;

var storeType = RequireType("BazaarPlusPlus.Game.RunLogging.Persistence.SqliteRunLogStore");
var ctor = storeType.GetConstructor([typeof(string)]);
Assert(ctor != null, "SqliteRunLogStore should expose a constructor taking the database path.");

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-run-log-sqlite-store-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "run-logs.db");

try
{
    var store = ctor!.Invoke([dbPath]);
    var startedAt = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
    const string runId = "run_20260315t121530z_vanessa_ranked_002a_deadbeef";

    var sessionState = Invoke<RunLogSessionState>(
        storeType,
        store,
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

    Assert(sessionState.RunId == runId, "CreateRun should return the created run state.");

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
                PendingSelectionSeq = 2,
                PendingSelection = new RunLogPendingSelectionState
                {
                    Day = 1,
                    Hour = 2,
                    State = "Encounter",
                    SelectionSeq = 2,
                    Options =
                    [
                        new RunLogOptionSnapshot
                        {
                            InstanceId = "instance-a",
                            TemplateId = "template-a",
                            Name = "Frost Street",
                        },
                    ],
                },
                Completed = false,
            },
        ]
    );

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

    Assert(File.Exists(dbPath), "CreateRun should initialize the SQLite database file.");

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();

        Assert(CountRows(connection, "runs") == 1, "runs should contain exactly one row.");
        Assert(
            GetString(connection, "SELECT run_id FROM runs WHERE run_id = $runId;", runId) == runId,
            "runs should contain the created run."
        );
        Assert(CountRows(connection, "run_events") == 2, "run_events should contain exactly two rows.");
        Assert(
            GetInt64(connection, "SELECT MIN(seq) FROM run_events WHERE run_id = $runId;", runId) == 1,
            "run_events should persist seq=1."
        );
        Assert(
            GetInt64(connection, "SELECT MAX(seq) FROM run_events WHERE run_id = $runId;", runId) == 2,
            "run_events should persist seq=2."
        );
        Assert(
            GetInt64(connection, "SELECT last_seq FROM run_checkpoints WHERE run_id = $runId;", runId)
                == 2,
            "run_checkpoints should persist the checkpoint last_seq."
        );
        Assert(
            GetString(
                connection,
                "SELECT pending_selection_json FROM run_checkpoints WHERE run_id = $runId;",
                runId
            ).Contains("template-a", StringComparison.Ordinal),
            "run_checkpoints should persist pending selection payload JSON."
        );
        Assert(
            GetString(connection, "SELECT status FROM run_status WHERE run_id = $runId;", runId)
                == "completed",
            "run_status should persist terminal status."
        );

        const string abandonedRunId = "server-run-456";
        Invoke<RunLogSessionState>(
            storeType,
            store,
            "CreateRun",
            [
                new RunLogCreateRequest
                {
                    SchemaVersion = 1,
                    RunId = abandonedRunId,
                    StartedAtUtc = startedAt.AddHours(1),
                    Hero = "Pygmalien",
                    GameMode = "Unranked",
                    Day = 1,
                    Hour = 1,
                },
            ]
        );
        InvokeVoid(
            storeType,
            store,
            "CompleteRun",
            [
                abandonedRunId,
                new RunLogCompletion
                {
                    SchemaVersion = 1,
                    RunId = abandonedRunId,
                    Status = "abandoned",
                    EndedAtUtc = startedAt.AddHours(1).AddMinutes(5),
                    FinalDay = 1,
                    FinalHour = 2,
                    Reason = "interrupted",
                },
            ]
        );
        Assert(
            GetString(
                connection,
                "SELECT status FROM run_status WHERE run_id = $runId;",
                abandonedRunId
            ) == "abandoned",
            "run_status should preserve interrupted/abandoned terminal statuses."
        );
    }
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("RunLogging SQLite store checks passed.");

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

static long CountRows(SqliteConnection connection, string tableName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
    return (long)(command.ExecuteScalar() ?? 0L);
}

static long GetInt64(SqliteConnection connection, string sql, string runId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$runId", runId);
    return (long)(
        command.ExecuteScalar()
        ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
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
