#nullable enable
using Microsoft.Data.Sqlite;

var schemaType = RequireType(
    "BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite.RunLogSqliteSchema"
);
var repositoryType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelRepository");
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
        InsertCheckpoint(connection, "run-1");
        InsertCheckpoint(connection, "run-2");
        InsertStatus(connection, "run-1");
        InsertStatus(connection, "run-2");

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
    var recentPlayerRank = (string?)(
        recentRuns[0].GetType().GetProperty("PlayerRank")!.GetValue(recentRuns[0])
    );
    var recentPlayerRating = (int?)(
        recentRuns[0].GetType().GetProperty("PlayerRating")!.GetValue(recentRuns[0])
    );
    var recentGameMode = (string?)(
        recentRuns[0].GetType().GetProperty("GameMode")!.GetValue(recentRuns[0])
    );
    Assert(
        recentPlayerRank == "Gold 2" && recentPlayerRating == 1420,
        "ListRecentRuns should surface the persisted player rank and rating snapshot."
    );
    Assert(recentGameMode == "Ranked", "ListRecentRuns should surface the persisted game mode.");

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
    var snapshotSummary = (string)(
        recordList[0].GetType().GetProperty("SnapshotSummary")!.GetValue(recordList[0])!
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
        snapshotSummary.Contains("YOU 0 items", StringComparison.Ordinal)
            && snapshotSummary.Contains("OPP 0 items", StringComparison.Ordinal),
        "ListBattlesByRun should still build the snapshot summary from parsed capture payloads."
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

    var ghostImportType = RequireType("BazaarPlusPlus.Game.HistoryPanel.GhostBattleImportRecord");
    var replaceGhostBattles = repositoryType.GetMethod(
        "ReplaceGhostBattles",
        [typeof(string), typeof(IReadOnlyList<>).MakeGenericType(ghostImportType)]
    )!;
    var markGhostReplayDownloaded = repositoryType.GetMethod(
        "MarkGhostReplayDownloaded",
        [typeof(string), typeof(string)]
    )!;
    var listRecentGhostBattles = repositoryType.GetMethod(
        "ListRecentGhostBattles",
        [typeof(string), typeof(int)]
    )!;
    var getGhostSyncCheckpoint = repositoryType.GetMethod(
        "TryGetGhostSyncCheckpointUtc",
        [typeof(string)]
    )!;
    var saveGhostSyncCheckpoint = repositoryType.GetMethod(
        "SaveGhostSyncCheckpointUtc",
        [typeof(string), typeof(DateTimeOffset)]
    )!;

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
            CreateGhostImports(ghostImportType, "ghost-1", "2026-03-16T10:00:00.0000000+00:00"),
        ]
    );
    markGhostReplayDownloaded.Invoke(repository, ["player-account-a", "ghost-1"]);

    replaceGhostBattles.Invoke(
        repository,
        [
            "player-account-a",
            CreateGhostImports(
                ghostImportType,
                "ghost-1",
                "2026-03-16T10:05:00.0000000+00:00",
                "ghost-2",
                "2026-03-16T10:10:00.0000000+00:00"
            ),
        ]
    );

    var ghostRecords =
        (
            (System.Collections.IEnumerable)
                listRecentGhostBattles.Invoke(repository, ["player-account-a", 10])!
        )
        .Cast<object>()
        .ToList();
    Assert(
        ghostRecords.Count == 2,
        "ReplaceGhostBattles should upsert current ghost rows without requiring a full table reset."
    );
    var downloadedGhost = ghostRecords.Single(
        record => (string)record.GetType().GetProperty("BattleId")!.GetValue(record)! == "ghost-1"
    );
    Assert(
        (bool)downloadedGhost.GetType().GetProperty("ReplayDownloaded")!.GetValue(downloadedGhost)!,
        "ReplaceGhostBattles should preserve replay_downloaded for ghost battles already fetched locally."
    );

    replaceGhostBattles.Invoke(
        repository,
        [
            "player-account-a",
            CreateGhostImports(ghostImportType, "ghost-2", "2026-03-16T10:10:00.0000000+00:00"),
        ]
    );
    ghostRecords =
        (
            (System.Collections.IEnumerable)
                listRecentGhostBattles.Invoke(repository, ["player-account-a", 10])!
        )
        .Cast<object>()
        .ToList();
    Assert(
        ghostRecords.Count == 2,
        "ReplaceGhostBattles should preserve previously imported ghost rows when the server only returns a sync window."
    );
    Assert(
        ghostRecords.Any(
            record =>
                (string)record.GetType().GetProperty("BattleId")!.GetValue(record)! == "ghost-1"
        )
            && ghostRecords.Any(
                record =>
                    (string)record.GetType().GetProperty("BattleId")!.GetValue(record)! == "ghost-2"
            ),
        "ReplaceGhostBattles should keep both existing and newly imported ghost rows."
    );

    replaceGhostBattles.Invoke(
        repository,
        [
            "player-account-b",
            CreateGhostImports(
                ghostImportType,
                "ghost-3",
                "2026-03-16T11:00:00.0000000+00:00"
            ),
        ]
    );
    var playerBGhostRecords =
        (
            (System.Collections.IEnumerable)
                listRecentGhostBattles.Invoke(repository, ["player-account-b", 10])!
        )
        .Cast<object>()
        .ToList();
    Assert(
        playerBGhostRecords.Count == 1
            && (string)playerBGhostRecords[0].GetType().GetProperty("BattleId")!.GetValue(playerBGhostRecords[0])!
                == "ghost-3",
        "Ghost battle rows should be isolated per local player account."
    );

    replaceGhostBattles.Invoke(
        repository,
        [
            "player-account-a",
            CreateGhostImports(
                ghostImportType,
                "ghost-stale-undownloaded",
                "2026-03-01T08:00:00.0000000+00:00",
                "ghost-stale-downloaded",
                "2026-03-01T09:00:00.0000000+00:00"
            ),
        ]
    );
    markGhostReplayDownloaded.Invoke(repository, ["player-account-a", "ghost-stale-downloaded"]);
    repositoryType.GetMethod("MarkOldUndownloadedGhostBattlesDeleted")!.Invoke(
        repository,
        ["player-account-a", new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero)]
    );
    ghostRecords =
        (
            (System.Collections.IEnumerable)
                listRecentGhostBattles.Invoke(repository, ["player-account-a", 20])!
        )
        .Cast<object>()
        .ToList();
    Assert(
        !ghostRecords.Any(
            record =>
                (string)record.GetType().GetProperty("BattleId")!.GetValue(record)!
                == "ghost-stale-undownloaded"
        ),
        "Undownloaded ghost battles older than two weeks should be hidden after local deletion."
    );
    Assert(
        ghostRecords.Any(
            record =>
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
        CountRows(verificationConnection, "run_checkpoints", "run_id = 'run-1'") == 0,
        "DeleteRun should cascade checkpoint rows."
    );
    Assert(
        CountRows(verificationConnection, "run_status", "run_id = 'run-1'") == 0,
        "DeleteRun should cascade terminal status rows."
    );
    Assert(
        CountRows(verificationConnection, "pvp_battles", "run_id = 'run-1'") == 0,
        "DeleteRun should explicitly remove linked PVP battle rows."
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

static void InsertRun(SqliteConnection connection, string runId, string status)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO runs (
            run_id,
            schema_version,
            started_at_utc,
            hero,
            game_mode,
            player_rank,
            player_rating,
            status
        ) VALUES (
            $runId,
            1,
            '2026-03-15T11:00:00.0000000+00:00',
            'Vanessa',
            'Ranked',
            'Gold 2',
            1420,
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
            'run_progress',
            '{}'
        );
        """;
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$seq", seq);
    command.ExecuteNonQuery();
}

static void InsertCheckpoint(SqliteConnection connection, string runId)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO run_checkpoints (
            run_id,
            schema_version,
            last_seq,
            last_seen_at_utc,
            completed
        ) VALUES (
            $runId,
            1,
            1,
            '2026-03-15T11:05:00.0000000+00:00',
            1
        );
        """;
    command.Parameters.AddWithValue("$runId", runId);
    command.ExecuteNonQuery();
}

static void InsertStatus(SqliteConnection connection, string runId)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO run_status (
            run_id,
            schema_version,
            status,
            ended_at_utc
        ) VALUES (
            $runId,
            1,
            'completed',
            '2026-03-15T11:10:00.0000000+00:00'
        );
        """;
    command.Parameters.AddWithValue("$runId", runId);
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
        INSERT INTO pvp_battles (
            battle_id,
            run_id,
            recorded_at_utc,
            combat_kind,
            player_rank,
            player_rating,
            player_hand_json,
            player_skills_json,
            opponent_hand_json,
            opponent_skills_json
        ) VALUES (
            $battleId,
            $runId,
            $recordedAtUtc,
            'PVPCombat',
            'Diamond 1',
            1777,
            $playerHandJson,
            $playerSkillsJson,
            $opponentHandJson,
            $opponentSkillsJson
        );
        """;
    command.Parameters.AddWithValue("$battleId", battleId);
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$recordedAtUtc", recordedAtUtc);
    command.Parameters.AddWithValue("$playerHandJson", playerHandJson);
    command.Parameters.AddWithValue("$playerSkillsJson", playerSkillsJson);
    command.Parameters.AddWithValue("$opponentHandJson", opponentHandJson);
    command.Parameters.AddWithValue("$opponentSkillsJson", opponentSkillsJson);
    command.ExecuteNonQuery();
}

static object CreateGhostImports(Type ghostImportType, params string[] battlePairs)
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
        ghostImportType.GetProperty("OpponentName")!.SetValue(battle, "Me");
        ghostImportType.GetProperty("PlayerHandJson")!.SetValue(battle, "{\"items\":[]}");
        ghostImportType.GetProperty("PlayerSkillsJson")!.SetValue(battle, "{\"items\":[]}");
        ghostImportType.GetProperty("OpponentHandJson")!.SetValue(battle, "{\"items\":[]}");
        ghostImportType.GetProperty("OpponentSkillsJson")!.SetValue(battle, "{\"items\":[]}");
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

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
