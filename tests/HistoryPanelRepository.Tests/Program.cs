#nullable enable
using Microsoft.Data.Sqlite;

var schemaType = RequireType("BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite.RunLogSqliteSchema");
var repositoryType = RequireType("BazaarPlusPlus.HistoryPanelRepository");
var ctor = repositoryType.GetConstructor([typeof(string)]);
Assert(ctor != null, "HistoryPanelRepository should expose a constructor taking the database path.");

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
    var records = (System.Collections.IEnumerable)(
        repositoryType.GetMethod("ListBattlesByRun")!.Invoke(repository, ["run-1"])!
    );
    var recordList = records.Cast<object>().ToList();

    Assert(
        recordList.Count == 1,
        "ListBattlesByRun should skip unreadable rows and return remaining valid battles."
    );

    var battleId = (string)(recordList[0].GetType().GetProperty("BattleId")!.GetValue(recordList[0])!);
    var snapshotSummary = (string)(
        recordList[0].GetType().GetProperty("SnapshotSummary")!.GetValue(recordList[0])!
    );

    Assert(
        battleId == "battle-good",
        "ListBattlesByRun should preserve valid rows even when an earlier row is malformed."
    );
    Assert(
        snapshotSummary.Contains("YOU 0 items", StringComparison.Ordinal)
            && snapshotSummary.Contains("OPP 0 items", StringComparison.Ordinal),
        "ListBattlesByRun should still build the snapshot summary from parsed capture payloads."
    );

    var battleIds = ((System.Collections.IEnumerable)(
        repositoryType.GetMethod("ListBattleIdsByRun")!.Invoke(repository, ["run-1"])!
    )).Cast<string>().ToList();
    Assert(
        battleIds.SequenceEqual(["battle-bad", "battle-good"]),
        "ListBattleIdsByRun should return all linked battles ordered from newest to oldest."
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
    return Type.GetType($"{fullName}, BazaarPlusPlus")
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
            status
        ) VALUES (
            $runId,
            1,
            '2026-03-15T11:00:00.0000000+00:00',
            'Vanessa',
            'Ranked',
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
            player_hand_json,
            player_skills_json,
            opponent_hand_json,
            opponent_skills_json
        ) VALUES (
            $battleId,
            $runId,
            $recordedAtUtc,
            'PVPCombat',
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
