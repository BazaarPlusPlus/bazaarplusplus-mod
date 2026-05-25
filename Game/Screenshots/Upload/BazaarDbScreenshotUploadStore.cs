#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.ModApi.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbScreenshotUploadStore : SqlitePersistenceStoreBase
{
    private const int UploadPayloadSchemaVersion = 1;

    private readonly string _screenshotsDirectoryPath;

    public BazaarDbScreenshotUploadStore(string databasePath, string screenshotsDirectoryPath)
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
            INSERT OR IGNORE INTO {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName}
                (screenshot_id, status, attempts, last_attempted_at_utc, last_error, uploaded_at_utc)
            SELECT s.screenshot_id, 'pending', 0, NULL, NULL, NULL
            FROM {RunLogSqliteSchema.RunScreenshotsTableName} AS s
            WHERE s.capture_source = $captureSource
              AND NOT EXISTS (
                  SELECT 1
                  FROM {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName} AS u
                  WHERE u.screenshot_id = s.screenshot_id
              );
            """;
        command.Parameters.AddWithValue(
            "$captureSource",
            RunLogSqliteSchema.CaptureSourceEndOfRunAuto
        );
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetPendingScreenshotIds(int limit)
    {
        if (limit <= 0)
            return Array.Empty<string>();

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT u.screenshot_id
            FROM {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName} AS u
            INNER JOIN {RunLogSqliteSchema.RunScreenshotsTableName} AS s
                ON s.screenshot_id = u.screenshot_id
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
            SELECT 1 FROM {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName}
            WHERE status = 'pending'
            LIMIT 1;
            """;
        return command.ExecuteScalar() != null;
    }

    public BazaarDbScreenshotUploadSnapshot? TryBuildSnapshot(
        string screenshotId,
        string playerAccountId
    )
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
            FROM {RunLogSqliteSchema.RunScreenshotsTableName}
            WHERE screenshot_id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", screenshotId);
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

        return new BazaarDbScreenshotUploadSnapshot
        {
            ScreenshotId = screenshotId,
            Payload = new BazaarDbScreenshotUploadRequest
            {
                SchemaVersion = UploadPayloadSchemaVersion,
                SubmittedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                PlayerAccountId = playerAccountId,
                ScreenshotId = screenshotId,
                RunId = GetNullableString(reader, "run_id"),
                HeroName = GetNullableString(reader, "hero_name"),
                FinalDays = GetNullableInt32(reader, "day"),
                FinalVictories = GetNullableInt32(reader, "victories_at_capture"),
                PlayerName = playerName,
                PlayerRank = GetNullableString(reader, "player_rank"),
                PlayerRating = GetNullableInt32(reader, "player_rating"),
                PlayerPosition = GetNullableInt32(reader, "player_position"),
                CapturedAtUtc = reader.GetString(reader.GetOrdinal("captured_at_utc")),
                ImageFormat = "png",
                ImageBytes = bytes,
            },
        };
    }

    public void MarkUploaded(string screenshotId, DateTime uploadedAtUtc)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName}
            SET status = 'uploaded',
                uploaded_at_utc = $uploadedAtUtc,
                last_attempted_at_utc = $uploadedAtUtc,
                last_error = NULL
            WHERE screenshot_id = $id;
            """;
        command.Parameters.AddWithValue("$id", screenshotId);
        command.Parameters.AddWithValue("$uploadedAtUtc", uploadedAtUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void MarkTransientFailure(string screenshotId, DateTime attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName}
            SET attempts = attempts + 1,
                last_attempted_at_utc = $attemptedAtUtc,
                last_error = $error
            WHERE screenshot_id = $id;
            """;
        command.Parameters.AddWithValue("$id", screenshotId);
        command.Parameters.AddWithValue("$attemptedAtUtc", attemptedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$error", error ?? string.Empty);
        command.ExecuteNonQuery();
    }

    public void MarkPermanentFailure(string screenshotId, DateTime attemptedAtUtc, string error)
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            UPDATE {RunLogSqliteSchema.BazaarDbScreenshotUploadsTableName}
            SET status = 'permanent_failure',
                attempts = attempts + 1,
                last_attempted_at_utc = $attemptedAtUtc,
                last_error = $error
            WHERE screenshot_id = $id;
            """;
        command.Parameters.AddWithValue("$id", screenshotId);
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
