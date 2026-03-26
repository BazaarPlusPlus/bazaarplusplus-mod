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
        snapshotSummary == "YOU 0 items · 0 skills  |  OPP 0 items · 0 skills",
        "ListBattlesByRun should still build the snapshot summary from parsed capture payloads."
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

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
