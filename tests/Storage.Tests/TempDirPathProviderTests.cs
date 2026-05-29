#nullable enable
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;

internal static class TempDirPathProviderTests
{
    public static void Run()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "bpp-storage-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);
        var dbPath = Path.Combine(tempRoot, "test.db");

        try
        {
            // IPathProvider is a pure Storage type — no BepInEx dependency.
            IPathProvider paths = new TempDirPathProvider(dbPath);
            Assert(paths.RunLogDatabasePath == dbPath, "RunLogDatabasePath should match.");
            Assert(
                paths.CombatReplayDirectoryPath == null,
                "CombatReplayDirectoryPath should be null."
            );
            Assert(
                paths.ScreenshotsDirectoryPath == null,
                "ScreenshotsDirectoryPath should be null."
            );
            Assert(
                paths.CombatReplayVideoDirectoryPath == null,
                "CombatReplayVideoDirectoryPath should be null."
            );
            Assert(paths.PluginsDirectoryPath == null, "PluginsDirectoryPath should be null.");

            // RunLogStore is constructable from IPathProvider alone.
            var store = new RunLogStore(paths);
            _ = store; // RunLogStore is constructable from IPathProvider — no BepInEx dependency.

            // Schema constants are accessible from the Storage assembly.
            Assert(
                RunLogSchema.LocalDatabaseSchemaVersion > 0,
                "LocalDatabaseSchemaVersion should be positive."
            );
            Assert(
                !string.IsNullOrEmpty(RunLogSchema.DatabaseFileName),
                "DatabaseFileName should not be empty."
            );
            Assert(
                !string.IsNullOrEmpty(RunLogSchema.RunsTableName),
                "RunsTableName should not be empty."
            );
            Assert(
                !string.IsNullOrEmpty(RunLogSchema.BootstrapSql),
                "BootstrapSql should not be empty."
            );

            // A round-trip through RunLogStore proves IPathProvider is sufficient to drive persistence.
            var startedAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
            const string runId = "run_20260301t100000z_test_ranked_001_cafebabe";

            var session = store.CreateRun(
                new RunLogCreateRequest
                {
                    RunId = runId,
                    StartedAtUtc = startedAt,
                    Hero = "Test",
                    GameMode = "Ranked",
                    Day = 1,
                    Hour = 1,
                    Seed = 7,
                }
            );
            Assert(session.RunId == runId, "CreateRun should return matching run id.");
            Assert(session.LastSeq == 0, "New run should start with seq 0.");

            store.AppendEvent(
                runId,
                new RunLogEvent
                {
                    RunId = runId,
                    Seq = 1,
                    Ts = startedAt,
                    Kind = "run_started",
                    Day = 1,
                    Hour = 1,
                }
            );
            store.SaveCheckpoint(
                runId,
                new RunLogCheckpoint
                {
                    RunId = runId,
                    LastSeq = 1,
                    LastSeenAtUtc = startedAt.AddSeconds(5),
                    Day = 1,
                    Hour = 2,
                    Completed = false,
                }
            );

            // TryResumeActiveRun returns the persisted session.
            var resumed = store.TryResumeActiveRun();
            Assert(resumed != null, "TryResumeActiveRun should find the active run.");
            Assert(resumed!.RunId == runId, "Resumed session should match run id.");
            Assert(resumed.LastSeq == 1, "Resumed session should have last_seq 1.");

            // CompleteRun marks the run terminal.
            store.CompleteRun(
                runId,
                new RunLogCompletion
                {
                    RunId = runId,
                    Status = "completed",
                    EndedAtUtc = startedAt.AddMinutes(10),
                    FinalDay = 1,
                    FinalHour = 3,
                }
            );
            var resumedAfter = store.TryResumeActiveRun();
            Assert(resumedAfter == null, "Completed runs should not be resumed.");

            Assert(File.Exists(dbPath), "SQLite database file should exist after operations.");

            Console.WriteLine("TempDirPathProviderTests passed.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

sealed class TempDirPathProvider : IPathProvider
{
    private readonly string _dbPath;

    public TempDirPathProvider(string dbPath) => _dbPath = dbPath;

    public string? RunLogDatabasePath => _dbPath;
    public string? CombatReplayDirectoryPath => null;
    public string? ScreenshotsDirectoryPath => null;
    public string? CombatReplayVideoDirectoryPath => null;
    public string? PluginsDirectoryPath => null;
}
