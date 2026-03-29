#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using BazaarPlusPlus.Game.RunLogging.Upload;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.CombatReplay.Upload;

internal sealed class BattleUploadSqliteStore
{
    private readonly string _databasePath;
    private readonly CombatReplayPayloadStore _payloadStore;
    private readonly PvpBattleCatalog _battleCatalog;

    public BattleUploadSqliteStore(string databasePath, string replayRootPath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        if (string.IsNullOrWhiteSpace(replayRootPath))
            throw new ArgumentException("Replay root path is required.", nameof(replayRootPath));

        _databasePath = databasePath;
        _payloadStore = new CombatReplayPayloadStore(replayRootPath);
        _battleCatalog = new PvpBattleCatalog(databasePath);
        EnsureSchema();
    }

    public void MarkReplayDirty(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.ReplaySyncStateTableName} (
                battle_id,
                dirty,
                retry_count
            ) VALUES (
                $battleId,
                1,
                0
            )
            ON CONFLICT(battle_id) DO UPDATE SET
                dirty = 1,
                last_error = NULL;
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetPendingBattleIds(int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT s.battle_id
            FROM {RunLogSqliteSchema.ReplaySyncStateTableName} AS s
            INNER JOIN {RunLogSqliteSchema.PvpBattlesTableName} AS b
                ON b.battle_id = s.battle_id
            WHERE s.dirty = 1
            ORDER BY COALESCE(s.last_attempt_at_utc, b.recorded_at_utc) ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        using var reader = command.ExecuteReader();
        var battleIds = new List<string>();
        while (reader.Read())
            battleIds.Add(reader.GetString(0));

        return battleIds;
    }

    public bool HasMorePendingReplays()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            SELECT 1
            FROM {RunLogSqliteSchema.ReplaySyncStateTableName}
            WHERE dirty = 1
            LIMIT 1;
            """;
        return command.ExecuteScalar() != null;
    }

    public void MarkReplayUploadFailed(string battleId, DateTimeOffset attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.ReplaySyncStateTableName}
            SET last_attempt_at_utc = $attemptedAtUtc,
                retry_count = retry_count + 1,
                last_error = $error
            WHERE battle_id = $battleId;
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.Parameters.AddWithValue("$attemptedAtUtc", attemptedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$error", error);
        command.ExecuteNonQuery();
    }

    public void MarkReplayUploadTerminalFailure(
        string battleId,
        DateTimeOffset attemptedAtUtc,
        string error
    )
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.ReplaySyncStateTableName}
            SET dirty = 0,
                last_attempt_at_utc = $attemptedAtUtc,
                retry_count = retry_count + 1,
                last_error = $error
            WHERE battle_id = $battleId;
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.Parameters.AddWithValue("$attemptedAtUtc", attemptedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$error", error);
        command.ExecuteNonQuery();
    }

    public void MarkReplayUploaded(
        string battleId,
        DateTimeOffset uploadedAtUtc
    )
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.ReplaySyncStateTableName}
            SET dirty = 0,
                last_attempt_at_utc = $uploadedAtUtc,
                last_uploaded_at_utc = $uploadedAtUtc,
                retry_count = 0,
                last_error = NULL
            WHERE battle_id = $battleId;
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.Parameters.AddWithValue("$uploadedAtUtc", uploadedAtUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    public BattleUploadSnapshot? TryBuildSnapshot(
        string battleId,
        string installId,
        string? clientId = null
    )
    {
        var manifest = _battleCatalog.TryLoad(battleId);
        if (manifest == null)
            return null;

        var replayPayload = _payloadStore.Load(battleId);
        if (replayPayload == null)
            return null;

        var payload = new BattleUploadPayload
        {
            SchemaVersion = RunLogSqliteSchema.UploadPayloadSchemaVersion,
            InstallId = installId,
            ClientId = clientId,
            PluginVersion = BppPluginVersion.Current,
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            BattleId = battleId,
            RunId = manifest.RunId,
            BattleManifest = manifest,
            ReplayPayload = replayPayload,
        };
        var json = JsonConvert.SerializeObject(payload, RunUploadSerialization.SerializerSettings);

        return new BattleUploadSnapshot
        {
            Payload = payload,
            Json = json,
            PayloadSha256 = RunUploadRequestSigner.ComputeBodyHash(json),
        };
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = RunLogSqliteSchema.BootstrapSql;
        command.ExecuteNonQuery();
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
}
