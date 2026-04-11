#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Game.RunLogging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.Screenshots.Persistence;

internal sealed class RunScreenshotSqliteStore
{
    private readonly string _databasePath;

    public RunScreenshotSqliteStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        _databasePath = databasePath;
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var connection = OpenConnection();
        EnableWriteAheadLogging(connection);
        RunLogSqliteSchema.EnsureInitialized(connection);
    }

    public void Save(RunScreenshotRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrWhiteSpace(record.ScreenshotId))
            throw new ArgumentException("Screenshot id is required.", nameof(record));
        if (string.IsNullOrWhiteSpace(record.ImageRelativePath))
            throw new ArgumentException("Image path is required.", nameof(record));

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {RunLogSqliteSchema.RunScreenshotsTableName} (
                screenshot_id,
                run_id,
                hero_name,
                battle_id,
                capture_source,
                is_primary,
                image_relative_path,
                captured_at_local,
                captured_at_utc,
                day,
                player_rank,
                player_rating,
                player_position,
                victories_at_capture
            ) VALUES (
                $screenshotId,
                $runId,
                $heroName,
                $battleId,
                $captureSource,
                $isPrimary,
                $imageRelativePath,
                $capturedAtLocal,
                $capturedAtUtc,
                $day,
                $playerRank,
                $playerRating,
                $playerPosition,
                $victoriesAtCapture
            );
            """;
        command.Parameters.AddWithValue("$screenshotId", record.ScreenshotId);
        command.Parameters.AddWithValue("$runId", (object?)record.RunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$heroName", (object?)record.HeroName ?? DBNull.Value);
        command.Parameters.AddWithValue("$battleId", (object?)record.BattleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$captureSource", ToStorageValue(record.CaptureSource));
        command.Parameters.AddWithValue("$isPrimary", record.IsPrimary ? 1 : 0);
        command.Parameters.AddWithValue("$imageRelativePath", record.ImageRelativePath);
        command.Parameters.AddWithValue("$capturedAtLocal", record.CapturedAtLocal.ToString("o"));
        command.Parameters.AddWithValue("$capturedAtUtc", record.CapturedAtUtc.ToString("o"));
        AddNullableInt32(command, "$day", record.Day);
        command.Parameters.AddWithValue("$playerRank", (object?)record.PlayerRank ?? DBNull.Value);
        AddNullableInt32(command, "$playerRating", record.PlayerRating);
        AddNullableInt32(command, "$playerPosition", record.PlayerPosition);
        AddNullableInt32(command, "$victoriesAtCapture", record.VictoriesAtCapture);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static void EnableWriteAheadLogging(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
    }

    private static void AddNullableInt32(SqliteCommand command, string parameterName, int? value)
    {
        command.Parameters.AddWithValue(parameterName, (object?)value ?? DBNull.Value);
    }

    private static string ToStorageValue(RunScreenshotCaptureSource source)
    {
        return source switch
        {
            RunScreenshotCaptureSource.ManualF9 => "manual_f9",
            RunScreenshotCaptureSource.SettingsDockCameraButton => "settings_dock_camera_button",
            RunScreenshotCaptureSource.PvpBattleStart => "pvp_battle_start",
            RunScreenshotCaptureSource.EndOfRunAuto => "end_of_run_auto",
            _ => "unknown",
        };
    }
}
