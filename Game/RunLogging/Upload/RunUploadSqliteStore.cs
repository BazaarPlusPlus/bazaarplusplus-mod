#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadSqliteStore
{
    private readonly string _databasePath;

    public RunUploadSqliteStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        _databasePath = databasePath;
        EnsureSchema();
    }

    public void MarkRunDirty(string runId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.RunSyncStateTableName} (
                run_id,
                dirty,
                retry_count
            ) VALUES (
                $runId,
                1,
                0
            )
            ON CONFLICT(run_id) DO UPDATE SET
                dirty = 1,
                last_error = NULL;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetPendingCompletedRunIds(int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT s.run_id
            FROM {RunLogSqliteSchema.RunSyncStateTableName} AS s
            INNER JOIN {RunLogSqliteSchema.RunsTableName} AS r
                ON r.run_id = s.run_id
            INNER JOIN {RunLogSqliteSchema.RunStatusTableName} AS rs
                ON rs.run_id = s.run_id
            WHERE s.dirty = 1
            ORDER BY COALESCE(s.last_attempt_at_utc, r.started_at_utc) ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var runIds = new List<string>();
        while (reader.Read())
            runIds.Add(reader.GetString(0));

        return runIds;
    }

    public bool HasMorePendingCompletedRuns()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT 1
            FROM {RunLogSqliteSchema.RunSyncStateTableName} AS s
            INNER JOIN {RunLogSqliteSchema.RunStatusTableName} AS rs
                ON rs.run_id = s.run_id
            WHERE s.dirty = 1
            LIMIT 1;
            """;
        return command.ExecuteScalar() != null;
    }

    public void MarkRunUploadFailed(string runId, DateTimeOffset attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunSyncStateTableName}
            SET last_attempt_at_utc = $attemptedAtUtc,
                retry_count = retry_count + 1,
                last_error = $error
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$attemptedAtUtc", attemptedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$error", error);
        command.ExecuteNonQuery();
    }

    public void MarkRunUploaded(
        string runId,
        long uploadedSeq,
        string? uploadedStatus,
        DateTimeOffset uploadedAtUtc
    )
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.RunSyncStateTableName}
            SET dirty = 0,
                uploaded_seq = $uploadedSeq,
                uploaded_status = $uploadedStatus,
                last_attempt_at_utc = $uploadedAtUtc,
                last_uploaded_at_utc = $uploadedAtUtc,
                retry_count = 0,
                last_error = NULL
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$uploadedSeq", uploadedSeq);
        command.Parameters.AddWithValue("$uploadedStatus", uploadedStatus ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$uploadedAtUtc", uploadedAtUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    public RunUploadSnapshot? TryBuildSnapshot(
        string runId,
        string installId,
        string? clientId = null
    )
    {
        using var connection = OpenConnection();

        using var metaCommand = connection.CreateCommand();
        metaCommand.CommandTimeout = 2;
        metaCommand.CommandText = $"""
            SELECT
                schema_version,
                run_id,
                started_at_utc,
                hero,
                game_mode,
                player_rank,
                player_rating,
                day,
                hour,
                seed,
                status
            FROM {RunLogSqliteSchema.RunsTableName}
            WHERE run_id = $runId;
            """;
        metaCommand.Parameters.AddWithValue("$runId", runId);

        JObject meta;
        using (var metaReader = metaCommand.ExecuteReader())
        {
            if (!metaReader.Read())
                return null;

            meta = ReadObject(
                metaReader,
                "schema_version",
                "run_id",
                "started_at_utc",
                "hero",
                "game_mode",
                "player_rank",
                "player_rating",
                "day",
                "hour",
                "seed",
                "status"
            );
        }

        using var eventsCommand = connection.CreateCommand();
        eventsCommand.CommandTimeout = 2;
        eventsCommand.CommandText = $"""
            SELECT seq, payload_json
            FROM {RunLogSqliteSchema.RunEventsTableName}
            WHERE run_id = $runId
            ORDER BY seq ASC;
            """;
        eventsCommand.Parameters.AddWithValue("$runId", runId);
        var events = new JArray();
        long lastSeq = 0;
        using (var eventsReader = eventsCommand.ExecuteReader())
        {
            while (eventsReader.Read())
            {
                lastSeq = eventsReader.GetInt64(0);
                events.Add(JToken.Parse(eventsReader.GetString(1)));
            }
        }

        var checkpoint = ReadSingleObject(
            connection,
            $"""
            SELECT
                schema_version,
                run_id,
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
            FROM {RunLogSqliteSchema.RunCheckpointsTableName}
            WHERE run_id = $runId;
            """,
            runId
        );
        if (
            checkpoint != null
            && checkpoint.TryGetValue("pending_selection_json", out var pendingJson)
        )
        {
            checkpoint.Remove("pending_selection_json");
            if (pendingJson.Type == JTokenType.String)
                checkpoint["pending_selection"] = JToken.Parse(pendingJson.Value<string>()!);
        }

        var status = ReadSingleObject(
            connection,
            $"""
            SELECT
                schema_version,
                run_id,
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
            FROM {RunLogSqliteSchema.RunStatusTableName}
            WHERE run_id = $runId;
            """,
            runId
        );

        var pvpBattles = new JArray();
        using var pvpCommand = connection.CreateCommand();
        pvpCommand.CommandTimeout = 2;
        pvpCommand.CommandText = $"""
            SELECT
                battle_id,
                run_id,
                recorded_at_utc,
                day,
                hour,
                encounter_id,
                player_name,
                player_account_id,
                player_hero,
                player_rank,
                player_rating,
                player_level,
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
            FROM {RunLogSqliteSchema.PvpBattlesTableName}
            WHERE run_id = $runId
            ORDER BY recorded_at_utc ASC, battle_id ASC;
            """;
        pvpCommand.Parameters.AddWithValue("$runId", runId);
        using var pvpReader = pvpCommand.ExecuteReader();
        while (pvpReader.Read())
        {
            var battle = ReadObject(
                pvpReader,
                "battle_id",
                "run_id",
                "recorded_at_utc",
                "day",
                "hour",
                "encounter_id",
                "player_name",
                "player_account_id",
                "player_hero",
                "player_rank",
                "player_rating",
                "player_level",
                "opponent_name",
                "opponent_hero",
                "opponent_rank",
                "opponent_rating",
                "opponent_level",
                "opponent_account_id",
                "combat_kind",
                "result",
                "winner_combatant_id",
                "loser_combatant_id"
            );
            battle["player_hand"] = JToken.Parse(
                pvpReader.GetString(pvpReader.GetOrdinal("player_hand_json"))
            );
            battle["player_skills"] = JToken.Parse(
                pvpReader.GetString(pvpReader.GetOrdinal("player_skills_json"))
            );
            battle["opponent_hand"] = JToken.Parse(
                pvpReader.GetString(pvpReader.GetOrdinal("opponent_hand_json"))
            );
            battle["opponent_skills"] = JToken.Parse(
                pvpReader.GetString(pvpReader.GetOrdinal("opponent_skills_json"))
            );
            pvpBattles.Add(battle);
        }

        return new RunUploadSnapshot
        {
            LastSeq = lastSeq,
            UploadedStatus = status?["status"]?.Value<string>(),
            Payload = new RunUploadPayload
            {
                SchemaVersion = RunLogSqliteSchema.CurrentSchemaVersion,
                InstallId = installId,
                ClientId = clientId,
                PluginVersion = BppPluginVersion.Current,
                SubmittedAtUtc = DateTimeOffset.UtcNow,
                RunId = runId,
                Meta = meta,
                Events = events,
                Checkpoint = checkpoint,
                Status = status,
                PvpBattles = pvpBattles,
            },
        };
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = RunLogSqliteSchema.BootstrapSql;
        command.ExecuteNonQuery();
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunsTableName,
            "player_rank",
            "TEXT NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.RunsTableName,
            "player_rating",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.PvpBattlesTableName,
            "player_hero",
            "TEXT NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.PvpBattlesTableName,
            "player_rank",
            "TEXT NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.PvpBattlesTableName,
            "player_rating",
            "INTEGER NULL"
        );
        EnsureColumnExists(
            connection,
            RunLogSqliteSchema.PvpBattlesTableName,
            "player_level",
            "INTEGER NULL"
        );
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandTimeout = 2;
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 2000;
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static JObject? ReadSingleObject(SqliteConnection connection, string sql, string runId)
    {
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var payload = new JObject();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.IsDBNull(i))
                continue;

            payload[reader.GetName(i)] = JToken.FromObject(reader.GetValue(i));
        }

        return payload;
    }

    private static JObject ReadObject(SqliteDataReader reader, params string[] columns)
    {
        var payload = new JObject();
        foreach (var column in columns)
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
                continue;

            payload[column] = JToken.FromObject(reader.GetValue(ordinal));
        }

        return payload;
    }

    private static void EnsureColumnExists(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition
    )
    {
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
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

        using var alter = connection.CreateCommand();
        alter.CommandTimeout = 2;
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }
}
