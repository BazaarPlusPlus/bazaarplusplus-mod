#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.RunLogging.Persistence;

public sealed class SqliteRunLogStore : IRunLogStore
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy(),
        },
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffK",
    };

    private readonly string _databasePath;

    public SqliteRunLogStore(string databasePath)
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
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunCheckpointsTableName,
            "pending_selection_json",
            "TEXT NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunCheckpointsTableName,
            "max_health",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunCheckpointsTableName,
            "prestige",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunCheckpointsTableName,
            "level",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunCheckpointsTableName,
            "income",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunCheckpointsTableName,
            "gold",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunStatusTableName,
            "max_health",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunStatusTableName,
            "prestige",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunStatusTableName,
            "level",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunStatusTableName,
            "income",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunStatusTableName,
            "gold",
            "INTEGER NULL"
        );
    }

    public RunLogSessionState? TryResumeActiveRun()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT
                r.run_id,
                r.schema_version,
                r.started_at_utc,
                r.day AS run_day,
                r.hour AS run_hour,
                cp.last_seq,
                cp.last_seen_at_utc,
                cp.day AS checkpoint_day,
                cp.hour AS checkpoint_hour,
                cp.max_health,
                cp.prestige,
                cp.level,
                cp.income,
                cp.gold,
                cp.state,
                cp.current_encounter_id,
                cp.last_state_fingerprint,
                cp.last_selection_fingerprint,
                cp.pending_selection_seq,
                cp.pending_selection_json,
                cp.completed
            FROM {RunLogSqliteSchema.RunsTableName} AS r
            LEFT JOIN {RunLogSqliteSchema.RunCheckpointsTableName} AS cp
                ON cp.run_id = r.run_id
            LEFT JOIN {RunLogSqliteSchema.RunStatusTableName} AS rs
                ON rs.run_id = r.run_id
            WHERE rs.run_id IS NULL
            ORDER BY COALESCE(cp.last_seen_at_utc, r.started_at_utc) DESC
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var checkpointCompleted = GetNullableInt64(reader, "completed");
        if (checkpointCompleted == 1)
            return null;

        var startedAtUtc = DateTimeOffset.Parse(
            reader.GetString(reader.GetOrdinal("started_at_utc"))
        );
        var lastSeenAtUtcText = GetNullableString(reader, "last_seen_at_utc");
        var lastSeenAtUtc = string.IsNullOrWhiteSpace(lastSeenAtUtcText)
            ? startedAtUtc
            : DateTimeOffset.Parse(lastSeenAtUtcText);

        return new RunLogSessionState
        {
            RunId = reader.GetString(reader.GetOrdinal("run_id")),
            SchemaVersion = reader.GetInt32(reader.GetOrdinal("schema_version")),
            StartedAtUtc = startedAtUtc,
            LastSeenAtUtc = lastSeenAtUtc,
            LastSeq = GetNullableInt64(reader, "last_seq") ?? 0,
            Day = GetNullableInt32(reader, "checkpoint_day") ?? GetNullableInt32(reader, "run_day"),
            Hour =
                GetNullableInt32(reader, "checkpoint_hour") ?? GetNullableInt32(reader, "run_hour"),
            MaxHealth = GetNullableInt32(reader, "max_health"),
            Prestige = GetNullableInt32(reader, "prestige"),
            Level = GetNullableInt32(reader, "level"),
            Income = GetNullableInt32(reader, "income"),
            Gold = GetNullableInt32(reader, "gold"),
            State = GetNullableString(reader, "state"),
            CurrentEncounterId = GetNullableString(reader, "current_encounter_id"),
            LastStateFingerprint = GetNullableString(reader, "last_state_fingerprint"),
            LastSelectionFingerprint = GetNullableString(reader, "last_selection_fingerprint"),
            PendingSelectionSeq = GetNullableInt64(reader, "pending_selection_seq"),
            PendingSelection = DeserializePendingSelection(
                GetNullableString(reader, "pending_selection_json")
            ),
            Completed = false,
        };
    }

    public RunLogSessionState CreateRun(RunLogCreateRequest request)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.RunsTableName} (
                run_id,
                schema_version,
                started_at_utc,
                hero,
                game_mode,
                day,
                hour,
                seed,
                status
            ) VALUES (
                $runId,
                $schemaVersion,
                $startedAtUtc,
                $hero,
                $gameMode,
                $day,
                $hour,
                $seed,
                $status
            );
            """;
        command.Parameters.AddWithValue("$runId", request.RunId);
        command.Parameters.AddWithValue("$schemaVersion", request.SchemaVersion);
        command.Parameters.AddWithValue("$startedAtUtc", request.StartedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$hero", request.Hero);
        command.Parameters.AddWithValue("$gameMode", request.GameMode);
        AddNullableInt32(command, "$day", request.Day);
        AddNullableInt32(command, "$hour", request.Hour);
        AddNullableInt32(command, "$seed", request.Seed);
        command.Parameters.AddWithValue("$status", request.Status);
        command.ExecuteNonQuery();

        transaction.Commit();

        return new RunLogSessionState
        {
            RunId = request.RunId,
            SchemaVersion = request.SchemaVersion,
            StartedAtUtc = request.StartedAtUtc,
            LastSeenAtUtc = request.StartedAtUtc,
            LastSeq = 0,
            Day = request.Day,
            Hour = request.Hour,
            Completed = false,
        };
    }

    public void AppendEvent(string runId, RunLogEvent entry)
    {
        var payloadJson = JsonConvert.SerializeObject(entry, SerializerSettings);
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.RunEventsTableName} (
                run_id,
                seq,
                ts_utc,
                kind,
                payload_json
            ) VALUES (
                $runId,
                $seq,
                $tsUtc,
                $kind,
                $payloadJson
            );
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$seq", entry.Seq);
        command.Parameters.AddWithValue("$tsUtc", entry.Ts.ToString("o"));
        command.Parameters.AddWithValue("$kind", entry.Kind);
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        command.ExecuteNonQuery();
    }

    public void SaveCheckpoint(string runId, RunLogCheckpoint checkpoint)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.RunCheckpointsTableName} (
                run_id,
                schema_version,
                last_seq,
                last_seen_at_utc,
                day,
                hour,
                max_health,
                prestige,
                level,
                income,
                gold,
                state,
                current_encounter_id,
                last_state_fingerprint,
                last_selection_fingerprint,
                pending_selection_seq,
                pending_selection_json,
                completed
            ) VALUES (
                $runId,
                $schemaVersion,
                $lastSeq,
                $lastSeenAtUtc,
                $day,
                $hour,
                $maxHealth,
                $prestige,
                $level,
                $income,
                $gold,
                $state,
                $currentEncounterId,
                $lastStateFingerprint,
                $lastSelectionFingerprint,
                $pendingSelectionSeq,
                $pendingSelectionJson,
                $completed
            )
            ON CONFLICT(run_id) DO UPDATE SET
                schema_version = excluded.schema_version,
                last_seq = excluded.last_seq,
                last_seen_at_utc = excluded.last_seen_at_utc,
                day = excluded.day,
                hour = excluded.hour,
                max_health = excluded.max_health,
                prestige = excluded.prestige,
                level = excluded.level,
                income = excluded.income,
                gold = excluded.gold,
                state = excluded.state,
                current_encounter_id = excluded.current_encounter_id,
                last_state_fingerprint = excluded.last_state_fingerprint,
                last_selection_fingerprint = excluded.last_selection_fingerprint,
                pending_selection_seq = excluded.pending_selection_seq,
                pending_selection_json = excluded.pending_selection_json,
                completed = excluded.completed;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$schemaVersion", checkpoint.SchemaVersion);
        command.Parameters.AddWithValue("$lastSeq", checkpoint.LastSeq);
        command.Parameters.AddWithValue("$lastSeenAtUtc", checkpoint.LastSeenAtUtc.ToString("o"));
        AddNullableInt32(command, "$day", checkpoint.Day);
        AddNullableInt32(command, "$hour", checkpoint.Hour);
        AddNullableInt32(command, "$maxHealth", checkpoint.MaxHealth);
        AddNullableInt32(command, "$prestige", checkpoint.Prestige);
        AddNullableInt32(command, "$level", checkpoint.Level);
        AddNullableInt32(command, "$income", checkpoint.Income);
        AddNullableInt32(command, "$gold", checkpoint.Gold);
        AddNullableString(command, "$state", checkpoint.State);
        AddNullableString(command, "$currentEncounterId", checkpoint.CurrentEncounterId);
        AddNullableString(command, "$lastStateFingerprint", checkpoint.LastStateFingerprint);
        AddNullableString(
            command,
            "$lastSelectionFingerprint",
            checkpoint.LastSelectionFingerprint
        );
        AddNullableInt64(command, "$pendingSelectionSeq", checkpoint.PendingSelectionSeq);
        AddNullableString(
            command,
            "$pendingSelectionJson",
            SerializePendingSelection(checkpoint.PendingSelection)
        );
        command.Parameters.AddWithValue("$completed", checkpoint.Completed ? 1 : 0);
        command.ExecuteNonQuery();

        using var updateRun = CreateCommand(connection, transaction);
        updateRun.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunsTableName}
            SET day = $day,
                hour = $hour
            WHERE run_id = $runId;
            """;
        updateRun.Parameters.AddWithValue("$runId", runId);
        AddNullableInt32(updateRun, "$day", checkpoint.Day);
        AddNullableInt32(updateRun, "$hour", checkpoint.Hour);
        updateRun.ExecuteNonQuery();

        transaction.Commit();
    }

    public void CompleteRun(string runId, RunLogCompletion completion)
    {
        WriteTerminalStatus(
            runId,
            completion.SchemaVersion,
            completion.Status,
            completion.EndedAtUtc,
            completion.FinalDay,
            completion.FinalHour,
            completion.MaxHealth,
            completion.Prestige,
            completion.Level,
            completion.Income,
            completion.Gold,
            completion.Victories,
            completion.Losses,
            completion.Reason
        );
    }

    public void MarkRunAbandoned(string runId, RunLogAbandonment abandonment)
    {
        WriteTerminalStatus(
            runId,
            abandonment.SchemaVersion,
            abandonment.Status,
            abandonment.EndedAtUtc,
            abandonment.FinalDay,
            abandonment.FinalHour,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            abandonment.Reason
        );
    }

    private void WriteTerminalStatus(
        string runId,
        int schemaVersion,
        string status,
        DateTimeOffset endedAtUtc,
        int? finalDay,
        int? finalHour,
        int? maxHealth,
        int? prestige,
        int? level,
        int? income,
        int? gold,
        int? victories,
        int? losses,
        string? reason
    )
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.RunStatusTableName} (
                run_id,
                schema_version,
                status,
                ended_at_utc,
                final_day,
                final_hour,
                max_health,
                prestige,
                level,
                income,
                gold,
                victories,
                losses,
                reason
            ) VALUES (
                $runId,
                $schemaVersion,
                $status,
                $endedAtUtc,
                $finalDay,
                $finalHour,
                $maxHealth,
                $prestige,
                $level,
                $income,
                $gold,
                $victories,
                $losses,
                $reason
            )
            ON CONFLICT(run_id) DO UPDATE SET
                schema_version = excluded.schema_version,
                status = excluded.status,
                ended_at_utc = excluded.ended_at_utc,
                final_day = excluded.final_day,
                final_hour = excluded.final_hour,
                max_health = excluded.max_health,
                prestige = excluded.prestige,
                level = excluded.level,
                income = excluded.income,
                gold = excluded.gold,
                victories = excluded.victories,
                losses = excluded.losses,
                reason = excluded.reason;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$schemaVersion", schemaVersion);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$endedAtUtc", endedAtUtc.ToString("o"));
        AddNullableInt32(command, "$finalDay", finalDay);
        AddNullableInt32(command, "$finalHour", finalHour);
        AddNullableInt32(command, "$maxHealth", maxHealth);
        AddNullableInt32(command, "$prestige", prestige);
        AddNullableInt32(command, "$level", level);
        AddNullableInt32(command, "$income", income);
        AddNullableInt32(command, "$gold", gold);
        AddNullableInt32(command, "$victories", victories);
        AddNullableInt32(command, "$losses", losses);
        AddNullableString(command, "$reason", reason);
        command.ExecuteNonQuery();

        using var updateRun = CreateCommand(connection, transaction);
        updateRun.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunsTableName}
            SET status = $status,
                day = COALESCE($finalDay, day),
                hour = COALESCE($finalHour, hour)
            WHERE run_id = $runId;
            """;
        updateRun.Parameters.AddWithValue("$runId", runId);
        updateRun.Parameters.AddWithValue("$status", status);
        AddNullableInt32(updateRun, "$finalDay", finalDay);
        AddNullableInt32(updateRun, "$finalHour", finalHour);
        updateRun.ExecuteNonQuery();

        using var completeCheckpoint = CreateCommand(connection, transaction);
        completeCheckpoint.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunCheckpointsTableName}
            SET completed = 1
            WHERE run_id = $runId;
            """;
        completeCheckpoint.Parameters.AddWithValue("$runId", runId);
        completeCheckpoint.ExecuteNonQuery();

        transaction.Commit();
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

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction? transaction = null
    )
    {
        var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.Transaction = transaction;
        return command;
    }

    private static void EnsureColumnExists(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition
    )
    {
        using var command = CreateCommand(connection);
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (
                string.Equals(
                    reader.GetString(reader.GetOrdinal("name")),
                    columnName,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }
        }

        using var alter = CreateCommand(connection);
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }

    private static string? SerializePendingSelection(RunLogPendingSelectionState? pendingSelection)
    {
        if (pendingSelection == null)
            return null;

        return JsonConvert.SerializeObject(pendingSelection, SerializerSettings);
    }

    private static RunLogPendingSelectionState? DeserializePendingSelection(string? pendingSelectionJson)
    {
        if (string.IsNullOrWhiteSpace(pendingSelectionJson))
            return null;

        return JsonConvert.DeserializeObject<RunLogPendingSelectionState>(
            pendingSelectionJson,
            SerializerSettings
        );
    }

    private static void AddNullableInt32(SqliteCommand command, string name, int? value)
    {
        command.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);
    }

    private static void AddNullableInt64(SqliteCommand command, string name, long? value)
    {
        command.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);
    }

    private static void AddNullableString(SqliteCommand command, string name, string? value)
    {
        command.Parameters.AddWithValue(name, value ?? (object)DBNull.Value);
    }

    private static int? GetNullableInt32(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static long? GetNullableInt64(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static string? GetNullableString(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
