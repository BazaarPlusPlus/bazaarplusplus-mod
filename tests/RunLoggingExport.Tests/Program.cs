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
    WritePvpBattle(dbPath, runId1, startedAt.AddMinutes(7), "battle-001", "Test Rival");

    RunPython(scriptPath, ["--db", dbPath, "--run-id", runId1, "--out", singleOutDir]);
    RunPython(scriptPath, ["--db", dbPath, "--all", "--out", allOutDir]);

    var singleRunDir = Path.Combine(singleOutDir, "2026-03-15", runId1);
    Assert(Directory.Exists(singleOutDir), "Single-run output directory should exist.");
    Assert(File.Exists(Path.Combine(singleRunDir, "meta.json")), "meta.json should exist.");
    Assert(
        File.ReadAllLines(Path.Combine(singleRunDir, "events.ndjson")).Length == 3,
        "events.ndjson should contain the expected number of lines."
    );
    Assert(
        File.Exists(Path.Combine(singleRunDir, "decision_chain.ndjson")),
        "decision_chain.ndjson should exist when the run contains player choices."
    );
    var decisionLines = File.ReadAllLines(Path.Combine(singleRunDir, "decision_chain.ndjson"));
    Assert(
        decisionLines.Length == 1,
        "decision_chain.ndjson should contain one line per exported choice."
    );
    using (var decisionDocument = JsonDocument.Parse(decisionLines[0]))
    {
        var root = decisionDocument.RootElement;
        Assert(
            root.GetProperty("selection_kind").GetString() == "encounter_options_seen",
            "decision_chain.ndjson should preserve the selection event kind."
        );
        Assert(
            root.GetProperty("options").GetArrayLength() == 2,
            "decision_chain.ndjson should preserve the visible options."
        );
        Assert(
            root.GetProperty("choice").GetProperty("kind").GetString() == "encounter_selected",
            "decision_chain.ndjson should preserve the matching choice event."
        );
        Assert(
            root.GetProperty("choice").GetProperty("selected_encounter_id").GetString()
                == "template-a",
            "decision_chain.ndjson should keep selected_encounter_id for encounter choices."
        );
    }
    Assert(
        ReadJsonString(Path.Combine(singleRunDir, "checkpoint.json"), "run_id") == runId1,
        "checkpoint.json should match the requested run."
    );
    using (
        var checkpointDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(singleRunDir, "checkpoint.json"))
        )
    )
    {
        var pendingSelection = checkpointDocument.RootElement.GetProperty("pending_selection");
        Assert(
            pendingSelection.GetProperty("selection_seq").GetInt64() == 2,
            "checkpoint.json should expand pending_selection_json into a structured object."
        );
    }
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
                && root.GetProperty("opponent_hero").GetString() == "Vanessa"
                && root.GetProperty("opponent_rank").GetString() == "Gold"
                && root.GetProperty("opponent_rating").GetInt32() == 1337
                && root.GetProperty("opponent_level").GetInt32() == 9
                && root.GetProperty("opponent_account_id").GetString() == "opponent-account-001",
            "pvp_battles.ndjson should preserve both player and opponent identity metadata."
        );
        Assert(
            root.GetProperty("result").GetString() == "win"
                && root.GetProperty("winner_combatant_id").GetString() == "Player"
                && root.GetProperty("loser_combatant_id").GetString() == "Opponent",
            "pvp_battles.ndjson should preserve combat outcome metadata."
        );
        Assert(
            root.GetProperty("player_hand").GetProperty("status").GetString() == "Captured"
                && root.GetProperty("player_hand").GetProperty("source").GetString()
                    == "OpeningMessage",
            "pvp_battles.ndjson should export the full player_hand capture object."
        );
        Assert(
            root.GetProperty("player_hand").GetProperty("items")[0].GetProperty("name").GetString()
                == "Sparkblade",
            "pvp_battles.ndjson should expand player_hand.items JSON content."
        );
        Assert(
            root.GetProperty("player_hand")
                .GetProperty("items")[0]
                .GetProperty("enchant")
                .GetString() == "Radiant",
            "pvp_battles.ndjson should preserve hand-card enchantments."
        );
        Assert(
            root.GetProperty("player_hand")
                .GetProperty("items")[0]
                .GetProperty("attributes")
                .GetProperty("Damage")
                .GetInt32() == 42,
            "pvp_battles.ndjson should preserve hand-card attributes."
        );
        Assert(
            root.GetProperty("player_skills").GetProperty("source").GetString() == "LiveRetry",
            "pvp_battles.ndjson should preserve snapshot capture source metadata."
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
    string opponentName
)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO pvp_battles (
            battle_id,
            run_id,
            recorded_at_utc,
            day,
            hour,
            encounter_id,
            player_name,
            player_account_id,
            opponent_name,
            opponent_hero,
            opponent_rank,
            opponent_rating,
            opponent_level,
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
            $runId,
            $recordedAtUtc,
            $day,
            $hour,
            $encounterId,
            $playerName,
            $playerAccountId,
            $opponentName,
            $opponentHero,
            $opponentRank,
            $opponentRating,
            $opponentLevel,
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
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$recordedAtUtc", recordedAtUtc.ToString("o"));
    command.Parameters.AddWithValue("$day", 1);
    command.Parameters.AddWithValue("$hour", 2);
    command.Parameters.AddWithValue("$encounterId", "encounter-pvp-1");
    command.Parameters.AddWithValue("$playerName", "Local Player");
    command.Parameters.AddWithValue("$playerAccountId", "player-account-001");
    command.Parameters.AddWithValue("$opponentName", opponentName);
    command.Parameters.AddWithValue("$opponentHero", "Vanessa");
    command.Parameters.AddWithValue("$opponentRank", "Gold");
    command.Parameters.AddWithValue("$opponentRating", 1337);
    command.Parameters.AddWithValue("$opponentLevel", 9);
    command.Parameters.AddWithValue("$opponentAccountId", "opponent-account-001");
    command.Parameters.AddWithValue("$combatKind", "PVPCombat");
    command.Parameters.AddWithValue("$result", "win");
    command.Parameters.AddWithValue("$winnerCombatantId", "Player");
    command.Parameters.AddWithValue("$loserCombatantId", "Opponent");
    command.Parameters.AddWithValue(
        "$playerHandJson",
        """{"status":"Captured","source":"OpeningMessage","items":[{"instance_id":"hand-1","name":"Sparkblade","enchant":"Radiant","attributes":{"Damage":42}}]}"""
    );
    command.Parameters.AddWithValue(
        "$playerSkillsJson",
        """{"status":"Captured","source":"LiveRetry","items":[{"instance_id":"skill-1","name":"Arcane Mastery","attributes":{"Cooldown":3}}]}"""
    );
    command.Parameters.AddWithValue(
        "$opponentHandJson",
        """{"status":"Captured","source":"OpeningMessage","items":[{"instance_id":"opp-hand-1","name":"Frostbite","enchant":"None","attributes":{"Damage":30}}]}"""
    );
    command.Parameters.AddWithValue(
        "$opponentSkillsJson",
        """{"status":"CapturedEmpty","source":"OpeningMessage","items":[{"instance_id":"opp-skill-1","name":"Ice Wall","attributes":{"Shield":20}}]}"""
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
            Kind = "encounter_options_seen",
            Day = 1,
            Hour = 2,
            State = "Encounter",
            Options =
            [
                new RunLogOptionSnapshot
                {
                    Index = 0,
                    InstanceId = "instance-a",
                    TemplateId = "template-a",
                    Name = "Frost Street",
                },
                new RunLogOptionSnapshot
                {
                    Index = 1,
                    InstanceId = "instance-b",
                    TemplateId = "template-b",
                    Name = "Amber Cove",
                },
            ],
        }
    );
    store.AppendEvent(
        runId,
        new RunLogEvent
        {
            SchemaVersion = 1,
            RunId = runId,
            Seq = 3,
            Ts = startedAt.AddSeconds(7),
            Kind = "encounter_selected",
            Day = 1,
            Hour = 2,
            State = "Encounter",
            SelectionSeq = 2,
            EncounterId = "map-node-encounter",
            SelectedInstanceId = "instance-a",
            SelectedTemplateId = "template-a",
            SelectedEncounterId = "template-a",
            SelectedName = "Frost Street",
        }
    );

    store.SaveCheckpoint(
        runId,
        new RunLogCheckpoint
        {
            SchemaVersion = 1,
            RunId = runId,
            LastSeq = 3,
            LastSeenAtUtc = startedAt.AddSeconds(7),
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
                        Index = 0,
                        InstanceId = "instance-a",
                        TemplateId = "template-a",
                        Name = "Frost Street",
                    },
                    new RunLogOptionSnapshot
                    {
                        Index = 1,
                        InstanceId = "instance-b",
                        TemplateId = "template-b",
                        Name = "Amber Cove",
                    },
                ],
            },
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
