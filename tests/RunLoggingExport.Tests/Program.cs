#nullable enable
using System.Diagnostics;
using System.Text.Json;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;
using Microsoft.Data.Sqlite;

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-run-log-export-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);

var dbPath = Path.Combine(tempRoot, "run-logs.db");
var singleOutDir = Path.Combine(tempRoot, "single-export");
var allOutDir = Path.Combine(tempRoot, "all-export");
var scriptPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../scripts/export_run_log.py")
);

try
{
    var startedAt = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
    const string runId1 = "run_20260315t121530z_vanessa_ranked_002a_deadbeef";
    const string runId2 = "run_20260315t131530z_dooly_ranked_002b_feedface";

    var store = new SqliteRunLogStore(dbPath);
    WriteCompletedRun(store, runId1, startedAt, "Vanessa", 42);
    WriteCompletedRun(store, runId2, startedAt.AddHours(1), "Dooly", 43);
    WritePvpBattle(
        dbPath,
        runId1,
        startedAt.AddMinutes(7),
        "battle-001",
        "replay-001",
        "Test Rival"
    );

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
    Assert(
        File.Exists(Path.Combine(singleRunDir, "pvp_battles.ndjson")),
        "pvp_battles.ndjson should exist when the run has recorded PVP battles."
    );
    var pvpBattleLines = File.ReadAllLines(Path.Combine(singleRunDir, "pvp_battles.ndjson"));
    Assert(
        pvpBattleLines.Length == 1,
        "pvp_battles.ndjson should contain one line per exported PVP battle."
    );
    using (var pvpBattleDocument = JsonDocument.Parse(pvpBattleLines[0]))
    {
        var root = pvpBattleDocument.RootElement;
        Assert(
            root.GetProperty("battle_id").GetString() == "battle-001",
            "pvp_battles.ndjson should preserve the battle id."
        );
        Assert(
            root.GetProperty("player_name").GetString() == "Local Player"
                && root.GetProperty("player_account_id").GetString() == "player-account-001"
                && root.GetProperty("opponent_name").GetString() == "Test Rival"
                && root.GetProperty("opponent_account_id").GetString() == "opponent-account-001",
            "pvp_battles.ndjson should preserve both player names and account ids."
        );
        Assert(
            root.GetProperty("result").GetString() == "win"
                && root.GetProperty("winner_combatant_id").GetString() == "Player"
                && root.GetProperty("loser_combatant_id").GetString() == "Opponent",
            "pvp_battles.ndjson should preserve combat outcome metadata."
        );
        Assert(
            root.GetProperty("player_hand")[0].GetProperty("name").GetString() == "Sparkblade",
            "pvp_battles.ndjson should expand player_hand JSON content."
        );
        Assert(
            root.GetProperty("player_hand")[0].GetProperty("enchant").GetString() == "Radiant",
            "pvp_battles.ndjson should preserve hand-card enchantments."
        );
        Assert(
            root.GetProperty("player_hand")[0]
                .GetProperty("attributes")
                .GetProperty("Damage")
                .GetInt32() == 42,
            "pvp_battles.ndjson should preserve hand-card attributes."
        );
    }

    var exportedRunDirectories = Directory.GetDirectories(
        allOutDir,
        "run_*",
        SearchOption.AllDirectories
    );
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
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        catch (IOException)
        {
            try
            {
                System.Threading.Thread.Sleep(200);
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup on Windows when SQLite releases the file handle late.
            }
        }
    }
}

static void WritePvpBattle(
    string dbPath,
    string runId,
    DateTimeOffset recordedAtUtc,
    string battleId,
    string replayId,
    string opponentName
)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO pvp_battles (
            battle_id,
            replay_id,
            run_id,
            recorded_at_utc,
            day,
            hour,
            encounter_id,
            player_name,
            player_account_id,
            opponent_name,
            opponent_account_id,
            combat_kind,
            result,
            winner_combatant_id,
            loser_combatant_id,
            player_hand_json,
            player_skills_json,
            opponent_hand_json,
            opponent_skills_json
        ) VALUES (
            $battleId,
            $replayId,
            $runId,
            $recordedAtUtc,
            $day,
            $hour,
            $encounterId,
            $playerName,
            $playerAccountId,
            $opponentName,
            $opponentAccountId,
            $combatKind,
            $result,
            $winnerCombatantId,
            $loserCombatantId,
            $playerHandJson,
            $playerSkillsJson,
            $opponentHandJson,
            $opponentSkillsJson
        );
        """;
    command.Parameters.AddWithValue("$battleId", battleId);
    command.Parameters.AddWithValue("$replayId", replayId);
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$recordedAtUtc", recordedAtUtc.ToString("o"));
    command.Parameters.AddWithValue("$day", 1);
    command.Parameters.AddWithValue("$hour", 2);
    command.Parameters.AddWithValue("$encounterId", "encounter-pvp-1");
    command.Parameters.AddWithValue("$playerName", "Local Player");
    command.Parameters.AddWithValue("$playerAccountId", "player-account-001");
    command.Parameters.AddWithValue("$opponentName", opponentName);
    command.Parameters.AddWithValue("$opponentAccountId", "opponent-account-001");
    command.Parameters.AddWithValue("$combatKind", "PVPCombat");
    command.Parameters.AddWithValue("$result", "win");
    command.Parameters.AddWithValue("$winnerCombatantId", "Player");
    command.Parameters.AddWithValue("$loserCombatantId", "Opponent");
    command.Parameters.AddWithValue(
        "$playerHandJson",
        """[{"instance_id":"hand-1","name":"Sparkblade","enchant":"Radiant","attributes":{"Damage":42}}]"""
    );
    command.Parameters.AddWithValue(
        "$playerSkillsJson",
        """[{"instance_id":"skill-1","name":"Arcane Mastery","attributes":{"Cooldown":3}}]"""
    );
    command.Parameters.AddWithValue(
        "$opponentHandJson",
        """[{"instance_id":"opp-hand-1","name":"Frostbite","enchant":"None","attributes":{"Damage":30}}]"""
    );
    command.Parameters.AddWithValue(
        "$opponentSkillsJson",
        """[{"instance_id":"opp-skill-1","name":"Ice Wall","attributes":{"Shield":20}}]"""
    );
    command.ExecuteNonQuery();
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

    using var process =
        Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start python3.");
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
