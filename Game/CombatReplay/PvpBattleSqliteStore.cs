#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class PvpBattleSqliteStore
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
    };

    private readonly string _databasePath;

    public PvpBattleSqliteStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        _databasePath = databasePath;

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = RunLogSqliteSchema.BootstrapSql;
        command.ExecuteNonQuery();
    }

    public void Save(CombatReplayRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrWhiteSpace(record.ReplayId))
            throw new ArgumentException("Replay id is required.", nameof(record));
        if (!string.Equals(record.CombatKind, "PVPCombat", StringComparison.Ordinal))
            return;

        var playerHandJson = JsonConvert.SerializeObject(record.PlayerHandCards, SerializerSettings);
        var playerSkillsJson = JsonConvert.SerializeObject(record.PlayerSkills, SerializerSettings);
        var opponentHandJson = JsonConvert.SerializeObject(
            record.OpponentHandCards,
            SerializerSettings
        );
        var opponentSkillsJson = JsonConvert.SerializeObject(
            record.OpponentSkills,
            SerializerSettings
        );

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.PvpBattlesTableName} (
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
            )
            ON CONFLICT(battle_id) DO UPDATE SET
                replay_id = excluded.replay_id,
                run_id = excluded.run_id,
                recorded_at_utc = excluded.recorded_at_utc,
                day = excluded.day,
                hour = excluded.hour,
                encounter_id = excluded.encounter_id,
                player_name = excluded.player_name,
                player_account_id = excluded.player_account_id,
                opponent_name = excluded.opponent_name,
                opponent_account_id = excluded.opponent_account_id,
                combat_kind = excluded.combat_kind,
                result = excluded.result,
                winner_combatant_id = excluded.winner_combatant_id,
                loser_combatant_id = excluded.loser_combatant_id,
                player_hand_json = excluded.player_hand_json,
                player_skills_json = excluded.player_skills_json,
                opponent_hand_json = excluded.opponent_hand_json,
                opponent_skills_json = excluded.opponent_skills_json;
            """;
        command.Parameters.AddWithValue("$battleId", record.ReplayId);
        command.Parameters.AddWithValue("$replayId", record.ReplayId);
        command.Parameters.AddWithValue("$runId", (object?)record.RunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$recordedAtUtc", record.SavedAtUtc.ToString("o"));
        AddNullableInt32(command, "$day", record.Day);
        AddNullableInt32(command, "$hour", record.Hour);
        command.Parameters.AddWithValue("$encounterId", (object?)record.EncounterId ?? DBNull.Value);
        command.Parameters.AddWithValue("$playerName", (object?)record.PlayerName ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$playerAccountId",
            (object?)record.PlayerAccountId ?? DBNull.Value
        );
        command.Parameters.AddWithValue("$opponentName", (object?)record.OpponentName ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$opponentAccountId",
            (object?)record.OpponentAccountId ?? DBNull.Value
        );
        command.Parameters.AddWithValue("$combatKind", record.CombatKind);
        command.Parameters.AddWithValue("$result", (object?)record.Result ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$winnerCombatantId",
            (object?)record.WinnerCombatantId ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$loserCombatantId",
            (object?)record.LoserCombatantId ?? DBNull.Value
        );
        command.Parameters.AddWithValue("$playerHandJson", playerHandJson);
        command.Parameters.AddWithValue("$playerSkillsJson", playerSkillsJson);
        command.Parameters.AddWithValue("$opponentHandJson", opponentHandJson);
        command.Parameters.AddWithValue("$opponentSkillsJson", opponentSkillsJson);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        try
        {
            connection.Open();

            using var command = CreateCommand(connection);
            command.CommandText = "PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static SqliteCommand CreateCommand(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        return command;
    }

    private static void AddNullableInt32(SqliteCommand command, string name, int? value)
    {
        command.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);
    }
}
