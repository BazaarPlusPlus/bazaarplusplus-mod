#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.ModApi.Models;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

var root = Path.Combine(Path.GetTempPath(), $"bpp-history-v5-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
try
{
    var paths = new TestPaths(root);
    var store = new RunLogStore(paths);
    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = "run-delete",
            StartedAtUtc = DateTimeOffset.UtcNow,
            Hero = "Vanessa",
            GameMode = "Ranked",
            PlayerAccountId = "account-local",
            BundleScreenshotRequested = true,
            ModVersion = "5.0.0",
        }
    );
    var databasePath = PathConstants.RunLogDatabase(root);
    InsertPendingOutbox(databasePath, "run-delete");

    var repository = new HistoryPanelRepository(databasePath);
    Assert(
        repository.ListRecentRuns(10).Single().RunId == "run-delete",
        "Fresh V5 run must be visible."
    );
    repository.DeleteRun("run-delete");
    Assert(
        repository.ListRecentRuns(10).Count == 0,
        "HistoryPanel DeleteRun must delete the source run."
    );
    Assert(
        ReadScalar(databasePath, "SELECT COUNT(*) FROM bundle_outbox;") == 1,
        "Outbox audit rows must not block or follow DeleteRun."
    );

    var first = Ghost(
        "battle-remote",
        "bundle-a",
        "https://r2.example/old",
        DateTimeOffset.UtcNow.AddMinutes(1)
    );
    repository.UpsertGhostBattles("account-local", [first]);
    var local = repository.ListRecentGhostBattles(10).Single();
    Assert(!local.SnapshotCounts.Known, "Undownloaded Ghost counts must remain unknown.");
    var localId = local.BattleId;

    var newer = Ghost(
        "battle-remote",
        "bundle-b",
        "https://r2.example/new",
        DateTimeOffset.UtcNow.AddMinutes(10)
    );
    repository.UpsertGhostBattles("account-local", [newer]);
    var reference = repository.TryGetGhostBundleReference(localId)!;
    Assert(
        reference.BundleId == "bundle-b"
            && reference.DownloadUrl.EndsWith("/new", StringComparison.Ordinal),
        "Discovery must overwrite the URL, expiry, and bundle identity."
    );
    repository.MarkGhostReplayUnavailable(localId, "unavailable_payload", "corrupt");
    repository.UpsertGhostBattles("account-local", [newer]);
    Assert(
        repository.TryGetGhostBundleReference(localId)?.ReplayState == "unavailable_payload",
        "Permanent payload failure must survive discovery refresh."
    );

    Assert(
        RunLogSchema.LocalDatabaseSchemaVersion == 1 && RunLogSchema.RowSchemaVersion == 1,
        "V5 is a fresh schema v1."
    );
    Assert(
        ReadScalar(
            databasePath,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('run_sync_state','battle_replay_sync_state','sync_cursors','sync_checkpoints');"
        ) == 0,
        "V4 sync tables must not exist."
    );

    Console.WriteLine("History panel V5 repository tests passed.");
}
finally
{
    if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
}

static GhostBattleImportRecord Ghost(
    string battleId,
    string bundleId,
    string url,
    DateTimeOffset expiry
) =>
    new()
    {
        BattleId = battleId,
        BundleId = bundleId,
        DownloadUrl = url,
        DownloadExpiresAtUtc = expiry,
        RecordedAtUtc = DateTimeOffset.UtcNow,
        Day = 3,
        Hour = 4,
        PlayerAccountId = "account-uploader",
        PlayerName = "Uploader",
        OpponentAccountId = "account-local",
        OpponentName = "Local",
        CombatKind = "PVPCombat",
        Result = "unknown",
    };

static void InsertPendingOutbox(string databasePath, string runId)
{
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO bundle_outbox (
            bundle_id, run_id, file_name, content_sha256_hex, content_digest,
            total_bytes, has_screenshot, sealed_at_utc, status
        ) VALUES (
            '01K1ABCDEF0123456789ABCDEF', $runId, 'bundle.bundle', $sha, $digest,
            399, 0, $sealed, 'pending'
        );
        """;
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$sha", new string('a', 64));
    command.Parameters.AddWithValue(
        "$digest",
        "sha-256=:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=:"
    );
    command.Parameters.AddWithValue("$sealed", DateTimeOffset.UtcNow.ToString("o"));
    command.ExecuteNonQuery();
}

static long ReadScalar(string databasePath, string sql)
{
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(command.ExecuteScalar());
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

file sealed class TestPaths(string dataRoot) : IPathProvider
{
    public string? DataRootDirectoryPath { get; } = dataRoot;
    public string? PluginsDirectoryPath => null;
}
