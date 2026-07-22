#nullable enable
using System.Reflection;
using Microsoft.Data.Sqlite;

var schemaType = RequireStorageType("BazaarPlusPlus.Storage.RunLog.RunLogSchema");
var repositoryType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryPanelRepository");
var ctor = repositoryType.GetConstructor([typeof(string)]);
Assert(
    ctor != null,
    "HistoryPanelRepository should expose a constructor taking the database path."
);

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-history-panel-repository-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "history.db");

try
{
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();

        using var bootstrap = connection.CreateCommand();
        bootstrap.CommandText = (string)(schemaType.GetProperty("BootstrapSql")!.GetValue(null)!);
        bootstrap.ExecuteNonQuery();

        InsertRun(connection, "run-1", "completed");
        InsertRun(connection, "run-2", "completed");
        InsertRunEvent(connection, "run-1", 1);
        InsertRunEvent(connection, "run-2", 1);

        InsertBattle(
            connection,
            battleId: "battle-bad",
            runId: "run-1",
            recordedAtUtc: "2026-03-15T12:01:00.0000000+00:00",
            playerHandJson: "{bad-json",
            playerSkillsJson: "{\"items\":[]}",
            opponentHandJson: "{\"items\":[]}",
            opponentSkillsJson: "{\"items\":[]}"
        );
        InsertBattle(
            connection,
            battleId: "battle-good",
            runId: "run-1",
            recordedAtUtc: "2026-03-15T12:00:00.0000000+00:00",
            playerHandJson: "{\"items\":[]}",
            playerSkillsJson: "{\"items\":[]}",
            opponentHandJson: "{\"items\":[]}",
            opponentSkillsJson: "{\"items\":[]}"
        );
        Assert(
            ColumnExists(connection, "battles", "source")
                && ColumnExists(connection, "battle_snapshots", "player_hand_json"),
            "Unified battle storage should expose battle source and snapshot tables."
        );
    }

    var repository = ctor!.Invoke([dbPath]);
    var recentRuns = (
        (System.Collections.IEnumerable)(
            repositoryType.GetMethod("ListRecentRuns")!.Invoke(repository, [10])!
        )
    )
        .Cast<object>()
        .ToList();
    Assert(recentRuns.Count == 2, "ListRecentRuns should return the inserted runs.");
    var runOneRecord = recentRuns.Single(run =>
        (string)run.GetType().GetProperty("RunId")!.GetValue(run)! == "run-1"
    );
    var recentPlayerRank = (string?)(
        runOneRecord.GetType().GetProperty("PlayerRank")!.GetValue(runOneRecord)
    );
    var recentPlayerRating = (int?)(
        runOneRecord.GetType().GetProperty("PlayerRating")!.GetValue(runOneRecord)
    );
    var recentGameMode = (string?)(
        runOneRecord.GetType().GetProperty("GameMode")!.GetValue(runOneRecord)
    );
    var recentBattleCount = (int)(
        runOneRecord.GetType().GetProperty("BattleCount")!.GetValue(runOneRecord)!
    );
    Assert(
        recentPlayerRank == "Gold 2" && recentPlayerRating == 1420,
        "ListRecentRuns should surface the persisted player rank and rating snapshot."
    );
    Assert(recentGameMode == "Ranked", "ListRecentRuns should surface the persisted game mode.");
    Assert(
        recentBattleCount == 1,
        "ListRecentRuns should count only battles whose snapshots are readable by HistoryPanel."
    );

    var records = (System.Collections.IEnumerable)(
        repositoryType.GetMethod("ListBattlesByRun")!.Invoke(repository, ["run-1"])!
    );
    var recordList = records.Cast<object>().ToList();

    Assert(
        recordList.Count == 1,
        "ListBattlesByRun should skip unreadable rows and return remaining valid battles."
    );

    var battleId = (string)(
        recordList[0].GetType().GetProperty("BattleId")!.GetValue(recordList[0])!
    );
    var playerRank = (string?)(
        recordList[0].GetType().GetProperty("PlayerRank")!.GetValue(recordList[0])
    );
    var playerRating = (int?)(
        recordList[0].GetType().GetProperty("PlayerRating")!.GetValue(recordList[0])
    );
    var playerHandItemCount = (int)(
        recordList[0].GetType().GetProperty("PlayerHandItemCount")!.GetValue(recordList[0])!
    );
    var playerSkillCount = (int)(
        recordList[0].GetType().GetProperty("PlayerSkillCount")!.GetValue(recordList[0])!
    );
    var opponentHandItemCount = (int)(
        recordList[0].GetType().GetProperty("OpponentHandItemCount")!.GetValue(recordList[0])!
    );
    var opponentSkillCount = (int)(
        recordList[0].GetType().GetProperty("OpponentSkillCount")!.GetValue(recordList[0])!
    );

    Assert(
        battleId == "battle-good",
        "ListBattlesByRun should preserve valid rows even when an earlier row is malformed."
    );
    Assert(
        playerRank == "Diamond 1" && playerRating == 1777,
        "ListBattlesByRun should surface the persisted player rank and rating snapshot."
    );
    Assert(
        playerHandItemCount == 0
            && playerSkillCount == 0
            && opponentHandItemCount == 0
            && opponentSkillCount == 0,
        "ListBattlesByRun should derive snapshot card counts from the parsed capture payloads."
    );

    var battleIds = (
        (System.Collections.IEnumerable)(
            repositoryType.GetMethod("ListBattleIdsByRun")!.Invoke(repository, ["run-1"])!
        )
    )
        .Cast<string>()
        .ToList();
    Assert(
        battleIds.SequenceEqual(["battle-bad", "battle-good"]),
        "ListBattleIdsByRun should return all linked battles ordered from newest to oldest."
    );

    const string ValidOldRecordingId = "00000000000000000000000000000001";
    const string ReportlessRecordingId = "00000000000000000000000000000002";
    const string WrongExtensionRecordingId = "00000000000000000000000000000003";
    const string FailedRecordingId = "00000000000000000000000000000004";
    const string OutsideRecordingId = "00000000000000000000000000000005";
    const string RootPrefixRecordingId = "00000000000000000000000000000006";
    const string TieARecordingId = "0000000000000000000000000000000a";
    const string TieZRecordingId = "0000000000000000000000000000000f";
    const string InvalidRecordingId = "not-a-canonical-recording-id";

    var reportRoot = Path.Combine(tempRoot, "reports");
    var rootPrefixSibling = Path.Combine(tempRoot, "reports2");
    var outsideRoot = Path.Combine(tempRoot, "outside");
    Directory.CreateDirectory(reportRoot);
    Directory.CreateDirectory(rootPrefixSibling);
    Directory.CreateDirectory(outsideRoot);
    var validReportPath = Path.Combine(reportRoot, ValidOldRecordingId + ".html");
    File.WriteAllText(validReportPath, "<!doctype html><title>valid static report</title>");
    File.WriteAllText(
        Path.Combine(reportRoot, WrongExtensionRecordingId + ".htm"),
        "<!doctype html><title>wrong extension</title>"
    );
    File.WriteAllText(
        Path.Combine(outsideRoot, OutsideRecordingId + ".html"),
        "<!doctype html><title>outside</title>"
    );
    File.WriteAllText(
        Path.Combine(rootPrefixSibling, RootPrefixRecordingId + ".html"),
        "<!doctype html><title>root prefix sibling</title>"
    );
    File.WriteAllText(
        Path.Combine(reportRoot, InvalidRecordingId + ".html"),
        "<!doctype html><title>invalid recording id</title>"
    );
    File.WriteAllText(
        Path.Combine(reportRoot, FailedRecordingId + ".html"),
        "<!doctype html><title>failed recording</title>"
    );

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        InsertVideo(
            connection,
            ValidOldRecordingId,
            "battle-good",
            "ignored-valid.mp4",
            "2026-07-22T12:00:00.0000000+00:00",
            "2026-07-22T12:01:00.0000000+00:00",
            "COMPLETED"
        );
        InsertVideo(
            connection,
            ReportlessRecordingId,
            "battle-good",
            "ignored-reportless.mp4",
            "2026-07-22T12:02:00.0000000+00:00",
            "2026-07-22T12:03:00.0000000+00:00",
            "COMPLETED"
        );
        InsertVideo(
            connection,
            WrongExtensionRecordingId,
            "battle-good",
            "ignored-wrong-extension.mp4",
            "2026-07-22T12:03:10.0000000+00:00",
            "2026-07-22T12:03:20.0000000+00:00",
            "COMPLETED"
        );
        InsertVideo(
            connection,
            OutsideRecordingId,
            "battle-good",
            "ignored-outside.mp4",
            "2026-07-22T12:03:30.0000000+00:00",
            "2026-07-22T12:03:40.0000000+00:00",
            "COMPLETED"
        );
        InsertVideo(
            connection,
            FailedRecordingId,
            "battle-good",
            "ignored-failed.mp4",
            "2026-07-22T12:04:00.0000000+00:00",
            "2026-07-22T12:05:00.0000000+00:00",
            "FAILED"
        );
        InsertVideo(
            connection,
            RootPrefixRecordingId,
            "battle-good",
            "ignored-root-prefix.mp4",
            "2026-07-22T12:06:00.0000000+00:00",
            "2026-07-22T12:07:00.0000000+00:00",
            "COMPLETED"
        );
        InsertVideo(
            connection,
            InvalidRecordingId,
            "battle-good",
            "ignored-invalid-id.mp4",
            "2026-07-22T12:08:00.0000000+00:00",
            "2026-07-22T12:09:00.0000000+00:00",
            "COMPLETED"
        );
        InsertVideo(
            connection,
            TieARecordingId,
            "battle-good",
            "ignored-tie-a.mp4",
            "2026-07-22T12:10:00.0000000+00:00",
            "2026-07-22T12:11:00.0000000+00:00",
            "COMPLETED"
        );
        InsertVideo(
            connection,
            TieZRecordingId,
            "battle-good",
            "ignored-tie-z.mp4",
            "2026-07-22T12:10:00.0000000+00:00",
            "2026-07-22T12:11:00.0000000+00:00",
            "COMPLETED"
        );
    }

    var candidateRows = (
        (System.Collections.IEnumerable)(
            repositoryType
                .GetMethod("ListCompletedReportCandidates")!
                .Invoke(repository, ["battle-good"])!
        )
    )
        .Cast<object>()
        .ToList();
    Assert(
        candidateRows
            .Select(row => (string)row.GetType().GetProperty("RecordingId")!.GetValue(row)!)
            .SequenceEqual([
                TieZRecordingId,
                TieARecordingId,
                InvalidRecordingId,
                RootPrefixRecordingId,
                OutsideRecordingId,
                WrongExtensionRecordingId,
                ReportlessRecordingId,
                ValidOldRecordingId,
            ]),
        "Report candidates should exclude failed recordings and use deterministic newest-first ordering."
    );

    var dataServiceType = RequireType(
        "BazaarPlusPlus.Game.HistoryPanel.Storage.HistoryPanelDataService"
    );
    var dataService = Activator.CreateInstance(dataServiceType, [repository, null])!;
    var resolveArgs = new object?[] { "battle-good", reportRoot, null, null };
    var reportResolved = (bool)(
        dataServiceType.GetMethod("TryResolveLatestBattleReport")!.Invoke(dataService, resolveArgs)!
    );
    var resolvedReportPath =
        resolveArgs[2]
            ?.GetType()
            .GetProperty("FullPath", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(resolveArgs[2]) as string;
    Assert(
        reportResolved && string.Equals(resolvedReportPath, validReportPath),
        "Newer missing, wrong-extension, outside-root, root-prefix, failed, or noncanonical recordings must not hide the prior usable static report."
    );

    var ghostImportType = RequireModApiType("BazaarPlusPlus.ModApi.Models.GhostBattleImportRecord");
    var replaceGhostBattles = repositoryType.GetMethod(
        "ReplaceGhostBattles",
        [typeof(string), typeof(IReadOnlyList<>).MakeGenericType(ghostImportType)]
    )!;
    var markGhostReplayDownloaded = repositoryType.GetMethod(
        "MarkGhostReplayDownloaded",
        [typeof(string)]
    )!;
    var listRecentGhostBattles = repositoryType.GetMethod("ListRecentGhostBattles", [typeof(int)])!;
    var markOldUndownloadedGhostBattlesDeleted = repositoryType.GetMethod(
        "MarkOldUndownloadedGhostBattlesDeleted",
        [typeof(DateTimeOffset)]
    )!;
    var getGhostSyncCheckpoint = repositoryType.GetMethod(
        "TryGetGhostSyncCheckpointUtc",
        [typeof(string)]
    )!;
    var saveGhostSyncCheckpoint = repositoryType.GetMethod(
        "SaveGhostSyncCheckpointUtc",
        [typeof(string), typeof(DateTimeOffset)]
    )!;
    var nowUtc = DateTimeOffset.UtcNow;

    Assert(
        getGhostSyncCheckpoint.Invoke(repository, ["player-account-a"]) == null,
        "Ghost sync checkpoint should be empty before the first successful sync."
    );

    var firstCheckpoint = new DateTimeOffset(2026, 3, 16, 9, 55, 0, TimeSpan.Zero);
    saveGhostSyncCheckpoint.Invoke(repository, ["player-account-a", firstCheckpoint]);
    Assert(
        (DateTimeOffset?)getGhostSyncCheckpoint.Invoke(repository, ["player-account-a"])
            == firstCheckpoint,
        "Ghost sync checkpoint should persist the last successful sync timestamp."
    );
    Assert(
        getGhostSyncCheckpoint.Invoke(repository, ["player-account-b"]) == null,
        "Ghost sync checkpoint should be scoped per local player account."
    );

    replaceGhostBattles.Invoke(
        repository,
        [
            "player-account-a",
            CreateGhostImports(ghostImportType, "ghost-1", nowUtc.AddHours(-2).ToString("o")),
        ]
    );
    markGhostReplayDownloaded.Invoke(repository, ["ghost-1"]);

    replaceGhostBattles.Invoke(
        repository,
        [
            "player-account-a",
            CreateGhostImports(
                ghostImportType,
                "ghost-1",
                nowUtc.AddHours(-1).ToString("o"),
                "ghost-2",
                nowUtc.AddMinutes(-30).ToString("o")
            ),
        ]
    );

    var ghostRecords = (
        (System.Collections.IEnumerable)listRecentGhostBattles.Invoke(repository, [10])!
    )
        .Cast<object>()
        .ToList();
    Assert(
        ghostRecords.Count == 2,
        "ReplaceGhostBattles should upsert current ghost rows without requiring a full table reset."
    );
    var downloadedGhost = ghostRecords.Single(record =>
        (string)record.GetType().GetProperty("BattleId")!.GetValue(record)! == "ghost-1"
    );
    Assert(
        (bool)downloadedGhost.GetType().GetProperty("ReplayDownloaded")!.GetValue(downloadedGhost)!,
        "ReplaceGhostBattles should preserve replay_downloaded for ghost battles already fetched locally."
    );
    Assert(
        (int)
            downloadedGhost.GetType().GetProperty("PlayerHandItemCount")!.GetValue(downloadedGhost)!
            == 7
            && (int)
                downloadedGhost
                    .GetType()
                    .GetProperty("PlayerSkillCount")!
                    .GetValue(downloadedGhost)! == 3
            && (int)
                downloadedGhost
                    .GetType()
                    .GetProperty("OpponentHandItemCount")!
                    .GetValue(downloadedGhost)! == 2
            && (int)
                downloadedGhost
                    .GetType()
                    .GetProperty("OpponentSkillCount")!
                    .GetValue(downloadedGhost)! == 1,
        "Ghost battle list rows should project remote summary counts into local-player perspective."
    );
    Assert(
        downloadedGhost.GetType().GetProperty("Snapshots")!.GetValue(downloadedGhost) is null,
        "Ghost battle list rows should not depend on remote snapshot payloads to display summary counts."
    );

    replaceGhostBattles.Invoke(
        repository,
        [
            "player-account-a",
            CreateGhostImportsWithoutCounts(
                ghostImportType,
                "ghost-1",
                nowUtc.AddMinutes(-45).ToString("o")
            ),
        ]
    );
    downloadedGhost = (
        (System.Collections.IEnumerable)listRecentGhostBattles.Invoke(repository, [10])!
    )
        .Cast<object>()
        .Single(record =>
            (string)record.GetType().GetProperty("BattleId")!.GetValue(record)! == "ghost-1"
        );
    Assert(
        (int)
            downloadedGhost.GetType().GetProperty("PlayerHandItemCount")!.GetValue(downloadedGhost)!
            == 7
            && (int)
                downloadedGhost
                    .GetType()
                    .GetProperty("PlayerSkillCount")!
                    .GetValue(downloadedGhost)! == 3
            && (int)
                downloadedGhost
                    .GetType()
                    .GetProperty("OpponentHandItemCount")!
                    .GetValue(downloadedGhost)! == 2
            && (int)
                downloadedGhost
                    .GetType()
                    .GetProperty("OpponentSkillCount")!
                    .GetValue(downloadedGhost)! == 1,
        "Ghost sync rows with missing summary counts should not clear counts already cached locally."
    );

    replaceGhostBattles.Invoke(
        repository,
        [
            "player-account-a",
            CreateGhostImports(ghostImportType, "ghost-2", nowUtc.AddMinutes(-30).ToString("o")),
        ]
    );
    ghostRecords = (
        (System.Collections.IEnumerable)listRecentGhostBattles.Invoke(repository, [10])!
    )
        .Cast<object>()
        .ToList();
    Assert(
        ghostRecords.Count == 2,
        "ReplaceGhostBattles should preserve previously imported ghost rows when the server only returns a sync window."
    );
    Assert(
        ghostRecords.Any(record =>
            (string)record.GetType().GetProperty("BattleId")!.GetValue(record)! == "ghost-1"
        )
            && ghostRecords.Any(record =>
                (string)record.GetType().GetProperty("BattleId")!.GetValue(record)! == "ghost-2"
            ),
        "ReplaceGhostBattles should keep both existing and newly imported ghost rows."
    );

    replaceGhostBattles.Invoke(
        repository,
        [
            "player-account-b",
            CreateGhostImports(ghostImportType, "ghost-3", nowUtc.AddMinutes(-15).ToString("o")),
        ]
    );
    var crossAccountGhostRecords = (
        (System.Collections.IEnumerable)listRecentGhostBattles.Invoke(repository, [10])!
    )
        .Cast<object>()
        .ToList();
    Assert(
        crossAccountGhostRecords.Count == 3
            && crossAccountGhostRecords.Any(record =>
                (string)record.GetType().GetProperty("BattleId")!.GetValue(record)! == "ghost-1"
            )
            && crossAccountGhostRecords.Any(record =>
                (string)record.GetType().GetProperty("BattleId")!.GetValue(record)! == "ghost-2"
            )
            && crossAccountGhostRecords.Any(record =>
                (string)record.GetType().GetProperty("BattleId")!.GetValue(record)! == "ghost-3"
            ),
        "Ghost battle rows should be visible across local player account scopes."
    );

    replaceGhostBattles.Invoke(
        repository,
        [
            "player-account-a",
            CreateGhostImports(
                ghostImportType,
                "ghost-stale-undownloaded",
                nowUtc.AddDays(-20).ToString("o"),
                "ghost-stale-downloaded",
                nowUtc.AddDays(-20).AddHours(1).ToString("o")
            ),
        ]
    );
    markGhostReplayDownloaded.Invoke(repository, ["ghost-stale-downloaded"]);
    markOldUndownloadedGhostBattlesDeleted.Invoke(repository, [nowUtc]);
    ghostRecords = (
        (System.Collections.IEnumerable)listRecentGhostBattles.Invoke(repository, [20])!
    )
        .Cast<object>()
        .ToList();
    Assert(
        !ghostRecords.Any(record =>
            (string)record.GetType().GetProperty("BattleId")!.GetValue(record)!
            == "ghost-stale-undownloaded"
        ),
        "Undownloaded ghost battles older than two weeks should be hidden after local deletion."
    );
    Assert(
        ghostRecords.Any(record =>
            (string)record.GetType().GetProperty("BattleId")!.GetValue(record)!
            == "ghost-stale-downloaded"
        ),
        "Downloaded ghost battles should be retained even when older than two weeks."
    );

    var secondCheckpoint = new DateTimeOffset(2026, 3, 16, 10, 15, 0, TimeSpan.Zero);
    saveGhostSyncCheckpoint.Invoke(repository, ["player-account-a", secondCheckpoint]);
    Assert(
        (DateTimeOffset?)getGhostSyncCheckpoint.Invoke(repository, ["player-account-a"])
            == secondCheckpoint,
        "Ghost sync checkpoint should overwrite the previous successful sync timestamp."
    );

    repositoryType.GetMethod("DeleteRun")!.Invoke(repository, ["run-1"]);

    using var verificationConnection = new SqliteConnection($"Data Source={dbPath}");
    verificationConnection.Open();
    Assert(
        CountRows(verificationConnection, "runs", "run_id = 'run-1'") == 0,
        "DeleteRun should remove the selected run row."
    );
    Assert(
        CountRows(verificationConnection, "run_events", "run_id = 'run-1'") == 0,
        "DeleteRun should cascade run event rows."
    );
    Assert(
        CountRows(verificationConnection, "battles", "run_id = 'run-1'") == 0,
        "DeleteRun should cascade linked local battle rows."
    );
    Assert(
        CountRows(
            verificationConnection,
            "battle_snapshots",
            "battle_id IN ('battle-bad', 'battle-good')"
        ) == 0,
        "DeleteRun should cascade linked local battle snapshots."
    );
    Assert(
        CountRows(verificationConnection, "runs", "run_id = 'run-2'") == 1,
        "DeleteRun should not disturb unrelated runs."
    );
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("HistoryPanelRepository checks passed.");

static Type RequireType(string fullName)
{
    var assembly = System.Reflection.Assembly.Load("BazaarPlusPlus");
    return assembly.GetType(fullName, throwOnError: false)
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static Type RequireStorageType(string fullName)
{
    var assembly = System.Reflection.Assembly.Load("BazaarPlusPlus.Storage");
    return assembly.GetType(fullName, throwOnError: false)
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static Type RequireModApiType(string fullName)
{
    var assembly = System.Reflection.Assembly.Load("BazaarPlusPlus.ModApi");
    return assembly.GetType(fullName, throwOnError: false)
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void InsertRun(SqliteConnection connection, string runId, string status)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO runs (
            run_id,
            started_at_utc,
            last_seen_at_utc,
            hero,
            game_mode,
            player_rank,
            player_rating,
            completed,
            ended_at_utc,
            final_day,
            final_hour,
            status
        ) VALUES (
            $runId,
            '2026-03-15T11:00:00.0000000+00:00',
            '2026-03-15T11:10:00.0000000+00:00',
            'Vanessa',
            'Ranked',
            'Gold 2',
            1420,
            1,
            '2026-03-15T11:10:00.0000000+00:00',
            3,
            1,
            $status
        );
        """;
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$status", status);
    command.ExecuteNonQuery();
}

static void InsertRunEvent(SqliteConnection connection, string runId, int seq)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO run_events (
            run_id,
            seq,
            ts_utc,
            kind,
            payload_json
        ) VALUES (
            $runId,
            $seq,
            '2026-03-15T11:01:00.0000000+00:00',
            'run_started',
            '{}'
        );
        """;
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$seq", seq);
    command.ExecuteNonQuery();
}

static void InsertVideo(
    SqliteConnection connection,
    string videoId,
    string battleId,
    string videoRelativePath,
    string startedAtUtc,
    string endedAtUtc,
    string status
)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO combat_replay_videos (
            video_id,
            battle_id,
            source,
            video_relative_path,
            width,
            height,
            fps,
            codec,
            started_at_utc,
            ended_at_utc,
            duration_ms,
            captured_frames,
            dropped_frames,
            file_size_bytes,
            status
        ) VALUES (
            $videoId,
            $battleId,
            'HistoryPanel',
            $videoRelativePath,
            1920,
            1080,
            60,
            'libx264',
            $startedAtUtc,
            $endedAtUtc,
            1000,
            60,
            0,
            3,
            $status
        );
        """;
    command.Parameters.AddWithValue("$videoId", videoId);
    command.Parameters.AddWithValue("$battleId", battleId);
    command.Parameters.AddWithValue("$videoRelativePath", videoRelativePath);
    command.Parameters.AddWithValue("$startedAtUtc", startedAtUtc);
    command.Parameters.AddWithValue("$endedAtUtc", endedAtUtc);
    command.Parameters.AddWithValue("$status", status);
    command.ExecuteNonQuery();
}

static void InsertBattle(
    SqliteConnection connection,
    string battleId,
    string runId,
    string recordedAtUtc,
    string playerHandJson,
    string playerSkillsJson,
    string opponentHandJson,
    string opponentSkillsJson
)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO battles (
            battle_id,
            source,
            run_id,
            recorded_at_utc,
            combat_kind,
            player_rank,
            player_rating
        ) VALUES (
            $battleId,
            'LOCAL',
            $runId,
            $recordedAtUtc,
            'PVPCombat',
            'Diamond 1',
            1777
        );
        """;
    command.Parameters.AddWithValue("$battleId", battleId);
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$recordedAtUtc", recordedAtUtc);
    command.ExecuteNonQuery();

    using var snapshotCommand = connection.CreateCommand();
    snapshotCommand.CommandText = """
        INSERT INTO battle_snapshots (
            battle_id,
            player_hand_json,
            player_skills_json,
            opponent_hand_json,
            opponent_skills_json
        ) VALUES (
            $battleId,
            $playerHandJson,
            $playerSkillsJson,
            $opponentHandJson,
            $opponentSkillsJson
        );
        """;
    snapshotCommand.Parameters.AddWithValue("$battleId", battleId);
    snapshotCommand.Parameters.AddWithValue("$playerHandJson", playerHandJson);
    snapshotCommand.Parameters.AddWithValue("$playerSkillsJson", playerSkillsJson);
    snapshotCommand.Parameters.AddWithValue("$opponentHandJson", opponentHandJson);
    snapshotCommand.Parameters.AddWithValue("$opponentSkillsJson", opponentSkillsJson);
    snapshotCommand.ExecuteNonQuery();
}

static object CreateGhostImports(Type ghostImportType, params string[] battlePairs)
{
    return CreateGhostImportsCore(ghostImportType, includeCounts: true, battlePairs);
}

static object CreateGhostImportsWithoutCounts(Type ghostImportType, params string[] battlePairs)
{
    return CreateGhostImportsCore(ghostImportType, includeCounts: false, battlePairs);
}

static object CreateGhostImportsCore(
    Type ghostImportType,
    bool includeCounts,
    params string[] battlePairs
)
{
    var listType = typeof(List<>).MakeGenericType(ghostImportType);
    var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
    for (var i = 0; i < battlePairs.Length; i += 2)
    {
        var battle = Activator.CreateInstance(ghostImportType)!;
        ghostImportType.GetProperty("BattleId")!.SetValue(battle, battlePairs[i]);
        ghostImportType
            .GetProperty("RecordedAtUtc")!
            .SetValue(battle, DateTimeOffset.Parse(battlePairs[i + 1]));
        ghostImportType.GetProperty("CombatKind")!.SetValue(battle, "PVPCombat");
        ghostImportType.GetProperty("PlayerHero")!.SetValue(battle, "Dooley");
        if (includeCounts)
        {
            ghostImportType.GetProperty("PlayerHandItemCount")!.SetValue(battle, 2);
            ghostImportType.GetProperty("PlayerSkillCount")!.SetValue(battle, 1);
        }
        ghostImportType.GetProperty("OpponentName")!.SetValue(battle, "Me");
        if (includeCounts)
        {
            ghostImportType.GetProperty("OpponentHandItemCount")!.SetValue(battle, 7);
            ghostImportType.GetProperty("OpponentSkillCount")!.SetValue(battle, 3);
        }
        ghostImportType.GetProperty("ReplayAvailable")!.SetValue(battle, true);
        ghostImportType.GetProperty("ReplayDownloaded")!.SetValue(battle, false);
        ghostImportType.GetProperty("LastSyncedAtUtc")!.SetValue(battle, DateTimeOffset.UtcNow);
        list.Add(battle);
    }

    return list;
}

static long CountRows(SqliteConnection connection, string table, string whereClause)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {whereClause};";
    return (long)command.ExecuteScalar()!;
}

static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info({tableName});";
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        if (
            string.Equals(
                reader.GetString(reader.GetOrdinal("name")),
                columnName,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return true;
    }

    return false;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
