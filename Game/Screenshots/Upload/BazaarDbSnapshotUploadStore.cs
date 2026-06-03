#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.ModApi.Models;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadStore : SqliteStoreBase
{
    private const int UploadPayloadSchemaVersion = 2;

    private readonly string _screenshotsDirectoryPath;

    public BazaarDbSnapshotUploadStore(string databasePath, string screenshotsDirectoryPath)
        : base(databasePath)
    {
        if (string.IsNullOrWhiteSpace(screenshotsDirectoryPath))
            throw new ArgumentException(
                "Screenshots directory is required.",
                nameof(screenshotsDirectoryPath)
            );
        _screenshotsDirectoryPath = screenshotsDirectoryPath;
    }

    public void EnsureBackfilled()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            INSERT OR IGNORE INTO {RunLogSchema.BazaarDbSnapshotUploadsTableName}
                (snapshot_id, status, attempts, last_attempted_at_utc, last_error, uploaded_at_utc)
            SELECT s.screenshot_id, 'pending', 0, NULL, NULL, NULL
            FROM {RunLogSchema.RunScreenshotsTableName} AS s
            WHERE s.capture_source = $captureSource
              AND NOT EXISTS (
                  SELECT 1
                  FROM {RunLogSchema.BazaarDbSnapshotUploadsTableName} AS u
                  WHERE u.snapshot_id = s.screenshot_id
              );
            """;
        command.Parameters.AddWithValue("$captureSource", RunLogSchema.CaptureSourceEndOfRunAuto);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetPendingSnapshotIds(int limit)
    {
        if (limit <= 0)
            return Array.Empty<string>();

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT u.snapshot_id
            FROM {RunLogSchema.BazaarDbSnapshotUploadsTableName} AS u
            INNER JOIN {RunLogSchema.RunScreenshotsTableName} AS s
                ON s.screenshot_id = u.snapshot_id
            WHERE u.status = 'pending'
            ORDER BY s.captured_at_utc ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    public bool HasMorePending()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT 1 FROM {RunLogSchema.BazaarDbSnapshotUploadsTableName}
            WHERE status = 'pending'
            LIMIT 1;
            """;
        return command.ExecuteScalar() != null;
    }

    public BazaarDbSnapshotUploadRecord? TryBuildSnapshot(string snapshotId, string playerAccountId)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT
                screenshot_id,
                run_id,
                hero_name,
                image_relative_path,
                captured_at_utc,
                day,
                player_rank,
                player_rating,
                player_position,
                victories_at_capture
            FROM {RunLogSchema.RunScreenshotsTableName}
            WHERE screenshot_id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", snapshotId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var imageRelativePath = reader.GetString(reader.GetOrdinal("image_relative_path"));
        var absolutePath = Path.Combine(_screenshotsDirectoryPath, imageRelativePath);
        if (!File.Exists(absolutePath))
            return null;

        var bytes = File.ReadAllBytes(absolutePath);
        if (bytes.Length == 0)
            return null;

        var playerName = TryResolvePlayerName();

        var capturedAtUtc = reader.GetString(reader.GetOrdinal("captured_at_utc"));
        return new BazaarDbSnapshotUploadRecord
        {
            SnapshotId = snapshotId,
            Payload = new BazaarDbSnapshotUploadRequest
            {
                SchemaVersion = UploadPayloadSchemaVersion,
                Snapshot = new BazaarDbSnapshotMetadata
                {
                    Id = snapshotId,
                    Source = RunLogSchema.CaptureSourceEndOfRunAuto,
                    CapturedAtUtc = capturedAtUtc,
                },
                Player = new BazaarDbSnapshotPlayer
                {
                    AccountId = playerAccountId,
                    DisplayName = playerName,
                    Rank = GetNullableString(reader, "player_rank"),
                    Rating = GetNullableInt32(reader, "player_rating"),
                    LeaderboardPosition = GetNullableInt32(reader, "player_position"),
                },
                Run = new BazaarDbSnapshotRun
                {
                    Id = GetNullableString(reader, "run_id"),
                    Day = GetNullableInt32(reader, "day"),
                    Wins = GetNullableInt32(reader, "victories_at_capture"),
                    Losses = null,
                    Hero = new BazaarDbSnapshotHero
                    {
                        Id = null,
                        Name = GetNullableString(reader, "hero_name"),
                    },
                },
                Image = new BazaarDbSnapshotImage
                {
                    ContentType = "image/png",
                    Encoding = "base64",
                    DataBase64 = Convert.ToBase64String(bytes),
                },
                Client = new BazaarDbSnapshotClientInfo
                {
                    SubmittedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                },
            },
        };
    }

    public void MarkUploaded(string snapshotId, DateTime uploadedAtUtc)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.BazaarDbSnapshotUploadsTableName}
            SET status = 'uploaded',
                uploaded_at_utc = $uploadedAtUtc,
                last_attempted_at_utc = $uploadedAtUtc,
                last_error = NULL
            WHERE snapshot_id = $id;
            """;
        command.Parameters.AddWithValue("$id", snapshotId);
        command.Parameters.AddWithValue("$uploadedAtUtc", uploadedAtUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void MarkTransientFailure(string snapshotId, DateTime attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.BazaarDbSnapshotUploadsTableName}
            SET attempts = attempts + 1,
                last_attempted_at_utc = $attemptedAtUtc,
                last_error = $error
            WHERE snapshot_id = $id;
            """;
        command.Parameters.AddWithValue("$id", snapshotId);
        command.Parameters.AddWithValue("$attemptedAtUtc", attemptedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$error", error ?? string.Empty);
        command.ExecuteNonQuery();
    }

    public void MarkPermanentFailure(string snapshotId, DateTime attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSchema.BazaarDbSnapshotUploadsTableName}
            SET status = 'permanent_failure',
                attempts = attempts + 1,
                last_attempted_at_utc = $attemptedAtUtc,
                last_error = $error
            WHERE snapshot_id = $id;
            """;
        command.Parameters.AddWithValue("$id", snapshotId);
        command.Parameters.AddWithValue("$attemptedAtUtc", attemptedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$error", error ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static string? TryResolvePlayerName()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileDisplayUsername()
                ?? BppClientCacheBridge.TryGetProfileUsername();
        }
        catch
        {
            return null;
        }
    }
}
