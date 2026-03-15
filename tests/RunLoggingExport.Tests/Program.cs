#nullable enable
using System.Diagnostics;
using System.Text.Json;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;

var tempRoot = Path.Combine(Path.GetTempPath(), "bpp-run-log-export-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

var dbPath = Path.Combine(tempRoot, "run-logs.db");
var singleOutDir = Path.Combine(tempRoot, "single-export");
var allOutDir = Path.Combine(tempRoot, "all-export");
var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../scripts/export_run_log.py"));

try
{
    var startedAt = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
    const string runId1 = "run_20260315t121530z_vanessa_ranked_002a_deadbeef";
    const string runId2 = "run_20260315t131530z_dooly_ranked_002b_feedface";

    var store = new SqliteRunLogStore(dbPath);
    WriteCompletedRun(store, runId1, startedAt, "Vanessa", 42);
    WriteCompletedRun(store, runId2, startedAt.AddHours(1), "Dooly", 43);

    RunPython(scriptPath, ["--db", dbPath, "--run-id", runId1, "--out", singleOutDir]);
    RunPython(scriptPath, ["--db", dbPath, "--all", "--out", allOutDir]);

    var singleRunDir = Path.Combine(singleOutDir, "2026-03-15", runId1);
    Assert(Directory.Exists(singleOutDir), "Single-run output directory should exist.");
    Assert(File.Exists(Path.Combine(singleRunDir, "meta.json")), "meta.json should exist.");
    Assert(
        File.ReadAllLines(Path.Combine(singleRunDir, "events.ndjson")).Length == 2,
        "events.ndjson should contain the expected number of lines."
    );
    Assert(
        ReadJsonString(Path.Combine(singleRunDir, "checkpoint.json"), "run_id") == runId1,
        "checkpoint.json should match the requested run."
    );
    Assert(
        ReadJsonString(Path.Combine(singleRunDir, "status.json"), "run_id") == runId1,
        "status.json should match the requested run."
    );

    var exportedRunDirectories = Directory.GetDirectories(allOutDir, "run_*", SearchOption.AllDirectories);
    Assert(exportedRunDirectories.Length == 2, "--all should write one export directory per run.");
    Assert(
        Directory.Exists(Path.Combine(allOutDir, "2026-03-15", runId1)),
        "--all should export the first run."
    );
    Assert(
        Directory.Exists(Path.Combine(allOutDir, "2026-03-15", runId2)),
        "--all should export the second run."
    );
}
finally
{
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("RunLogging export checks passed.");

static void WriteCompletedRun(
    SqliteRunLogStore store,
    string runId,
    DateTimeOffset startedAt,
    string hero,
    int seed
)
{
    store.CreateRun(
        new RunLogCreateRequest
        {
            SchemaVersion = 1,
            RunId = runId,
            StartedAtUtc = startedAt,
            Hero = hero,
            GameMode = "Ranked",
            Day = 1,
            Hour = 1,
            Seed = seed,
        }
    );

    store.AppendEvent(
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
            Hero = hero,
            GameMode = "Ranked",
        }
    );
    store.AppendEvent(
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
        }
    );

    store.SaveCheckpoint(
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
        }
    );

    store.CompleteRun(
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
        }
    );
}

static void RunPython(string scriptPath, IReadOnlyList<string> arguments)
{
    var startInfo = new ProcessStartInfo("python3")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add(scriptPath);
    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start python3.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"Python export failed with exit code {process.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}"
        );
    }
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
