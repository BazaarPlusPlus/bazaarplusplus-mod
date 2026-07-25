#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class CombatReplayVideoMetadataStore : SqliteStoreBase
{
    public CombatReplayVideoMetadataStore(string databasePath)
        : base(databasePath) { }

    public void SaveStart(VideoRecordingStarted record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            INSERT INTO {RunLogSchema.CombatReplayVideosTableName} (
                video_id,
                battle_id,
                source,
                video_relative_path,
                width,
                height,
                fps,
                codec,
                crf,
                preset,
                started_at_utc,
                captured_frames,
                dropped_frames,
                status
            ) VALUES (
                $videoId,
                $battleId,
                $source,
                $videoRelativePath,
                $width,
                $height,
                $fps,
                $codec,
                $crf,
                $preset,
                $startedAtUtc,
                0,
                0,
                'RECORDING'
            );
            """;
        command.Parameters.AddWithValue("$videoId", record.VideoId);
        command.Parameters.AddWithValue("$battleId", record.BattleId);
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$videoRelativePath", record.VideoRelativePath);
        command.Parameters.AddWithValue("$width", record.Width);
        command.Parameters.AddWithValue("$height", record.Height);
        command.Parameters.AddWithValue("$fps", record.Fps);
        command.Parameters.AddWithValue("$codec", record.Codec);
        AddNullableInt32(command, "$crf", record.Crf);
        AddNullableString(command, "$preset", record.Preset);
        command.Parameters.AddWithValue("$startedAtUtc", record.StartedAtUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void SaveFinish(VideoRecordingFinished record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = CreateCommand(connection);
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {RunLogSchema.CombatReplayVideosTableName}
            SET
                video_relative_path = $videoRelativePath,
                ended_at_utc = $endedAtUtc,
                duration_ms = $durationMs,
                captured_frames = $capturedFrames,
                dropped_frames = $droppedFrames,
                file_size_bytes = $fileSizeBytes,
                status = $status,
                error = $error
            WHERE video_id = $videoId AND battle_id = $battleId;
            """;
        command.Parameters.AddWithValue("$videoId", record.VideoId);
        command.Parameters.AddWithValue("$battleId", record.BattleId);
        command.Parameters.AddWithValue("$videoRelativePath", record.VideoRelativePath);
        command.Parameters.AddWithValue("$endedAtUtc", record.EndedAtUtc.ToString("o"));
        command.Parameters.AddWithValue("$durationMs", record.DurationMs);
        command.Parameters.AddWithValue("$capturedFrames", record.CapturedFrames);
        command.Parameters.AddWithValue("$droppedFrames", record.DroppedFrames);
        AddNullableInt64(command, "$fileSizeBytes", record.FileSizeBytes);
        command.Parameters.AddWithValue("$status", record.Status);
        AddNullableString(command, "$error", record.Error);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Video recording identity was not found.");

        AppendSyncAnchors(connection, transaction, record);
        transaction.Commit();
    }

    public CompletedVideoReportRecoveryPage ListCompletedForReportRecovery(
        int limit,
        CompletedVideoReportRecoveryCursor? cursor = null
    )
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));

        using var connection = OpenConnection();
        using var command = CreateCommand(connection);
        command.CommandText = $"""
            SELECT
                video_id,
                battle_id,
                source,
                video_relative_path,
                COALESCE(ended_at_utc, started_at_utc) AS recovery_completed_at_utc,
                started_at_utc AS recovery_started_at_utc
            FROM {RunLogSchema.CombatReplayVideosTableName}
            WHERE status = 'COMPLETED'
                AND (
                    $cursorCompletedAtUtc IS NULL
                    OR COALESCE(ended_at_utc, started_at_utc) < $cursorCompletedAtUtc
                    OR (
                        COALESCE(ended_at_utc, started_at_utc) = $cursorCompletedAtUtc
                        AND started_at_utc < $cursorStartedAtUtc
                    )
                    OR (
                        COALESCE(ended_at_utc, started_at_utc) = $cursorCompletedAtUtc
                        AND started_at_utc = $cursorStartedAtUtc
                        AND video_id < $cursorRecordingId
                    )
                )
            ORDER BY
                COALESCE(ended_at_utc, started_at_utc) DESC,
                started_at_utc DESC,
                video_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue(
            "$cursorCompletedAtUtc",
            (object?)cursor?.CompletedAtUtc ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$cursorStartedAtUtc",
            (object?)cursor?.StartedAtUtc ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$cursorRecordingId",
            (object?)cursor?.RecordingId ?? DBNull.Value
        );

        var result = new List<CompletedVideoReportRecoveryCandidate>();
        CompletedVideoReportRecoveryCursor? nextCursor = null;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var recordingId = reader.GetString(reader.GetOrdinal("video_id"));
                result.Add(
                    new CompletedVideoReportRecoveryCandidate(
                        recordingId,
                        reader.GetString(reader.GetOrdinal("battle_id")),
                        reader.GetString(reader.GetOrdinal("source")),
                        reader.GetString(reader.GetOrdinal("video_relative_path"))
                    )
                );
                nextCursor = new CompletedVideoReportRecoveryCursor(
                    reader.GetString(reader.GetOrdinal("recovery_completed_at_utc")),
                    reader.GetString(reader.GetOrdinal("recovery_started_at_utc")),
                    recordingId
                );
            }
        }

        for (var index = 0; index < result.Count; index++)
        {
            var candidate = result[index];
            result[index] = candidate with
            {
                SyncAnchors = ListSyncAnchors(connection, candidate.RecordingId),
            };
        }
        return new CompletedVideoReportRecoveryPage(result, nextCursor);
    }

    private static void AppendSyncAnchors(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        VideoRecordingFinished record
    )
    {
        var anchors = record.SyncAnchors ?? Array.Empty<ReplayVideoSyncAnchor>();
        if (
            !ReplayVideoSyncMetadata.IsValidAnchorSequence(record.VideoId, record.BattleId, anchors)
        )
            throw new InvalidOperationException(
                "Video sync anchors must start at output ordinal zero and remain contiguous."
            );

        var persisted = ListSyncAnchors(connection, record.VideoId, transaction);
        if (persisted.Count > 0)
        {
            if (!AnchorSequencesEqual(persisted, anchors))
                throw new InvalidOperationException(
                    "Persisted video sync anchors conflict with the completed recording."
                );
            return;
        }

        for (var index = 0; index < anchors.Count; index++)
        {
            var anchor = anchors[index];
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO {RunLogSchema.CombatReplayVideoSyncAnchorsTableName} (
                    video_id,
                    battle_id,
                    output_ordinal,
                    combat_frame,
                    combat_ms,
                    media_pts_ms
                ) VALUES (
                    $videoId,
                    $battleId,
                    $outputOrdinal,
                    $combatFrame,
                    $combatMs,
                    $mediaPtsMs
                );
                """;
            insert.Parameters.AddWithValue("$videoId", anchor.RecordingId);
            insert.Parameters.AddWithValue("$battleId", anchor.BattleId);
            insert.Parameters.AddWithValue("$outputOrdinal", anchor.OutputOrdinal);
            insert.Parameters.AddWithValue("$combatFrame", anchor.CombatFrame);
            insert.Parameters.AddWithValue("$combatMs", anchor.CombatMs);
            insert.Parameters.AddWithValue("$mediaPtsMs", anchor.MediaPtsMs);
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Video sync anchor was not persisted.");
        }
    }

    private static bool AnchorSequencesEqual(
        IReadOnlyList<ReplayVideoSyncAnchor> persisted,
        IReadOnlyList<ReplayVideoSyncAnchor> completed
    )
    {
        if (persisted.Count != completed.Count)
            return false;

        for (var index = 0; index < persisted.Count; index++)
        {
            if (!Equals(persisted[index], completed[index]))
                return false;
        }

        return true;
    }

    private static IReadOnlyList<ReplayVideoSyncAnchor> ListSyncAnchors(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string recordingId,
        Microsoft.Data.Sqlite.SqliteTransaction? transaction = null
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT battle_id, output_ordinal, combat_frame, combat_ms, media_pts_ms
            FROM {RunLogSchema.CombatReplayVideoSyncAnchorsTableName}
            WHERE video_id = $videoId
            ORDER BY output_ordinal ASC;
            """;
        command.Parameters.AddWithValue("$videoId", recordingId);
        using var reader = command.ExecuteReader();
        var anchors = new List<ReplayVideoSyncAnchor>();
        while (reader.Read())
        {
            anchors.Add(
                new ReplayVideoSyncAnchor(
                    recordingId,
                    reader.GetString(reader.GetOrdinal("battle_id")),
                    reader.GetInt32(reader.GetOrdinal("combat_frame")),
                    reader.GetInt32(reader.GetOrdinal("combat_ms")),
                    reader.GetInt64(reader.GetOrdinal("media_pts_ms")),
                    reader.GetInt64(reader.GetOrdinal("output_ordinal"))
                )
            );
        }
        return anchors;
    }
}

internal sealed record CompletedVideoReportRecoveryCandidate(
    string RecordingId,
    string BattleId,
    string Source,
    string VideoRelativePath
)
{
    internal IReadOnlyList<ReplayVideoSyncAnchor> SyncAnchors { get; init; } =
        Array.Empty<ReplayVideoSyncAnchor>();
}

internal sealed record CompletedVideoReportRecoveryCursor(
    string CompletedAtUtc,
    string StartedAtUtc,
    string RecordingId
);

internal sealed record CompletedVideoReportRecoveryPage(
    IReadOnlyList<CompletedVideoReportRecoveryCandidate> Candidates,
    CompletedVideoReportRecoveryCursor? NextCursor
);

internal sealed class VideoRecordingStarted
{
    public string VideoId { get; init; } = string.Empty;
    public string BattleId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string VideoRelativePath { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int Fps { get; init; }
    public string Codec { get; init; } = "libx264";
    public int? Crf { get; init; }
    public string? Preset { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
}

internal sealed class VideoRecordingFinished
{
    public string VideoId { get; init; } = string.Empty;
    public string BattleId { get; init; } = string.Empty;
    public string VideoRelativePath { get; init; } = string.Empty;
    public DateTimeOffset EndedAtUtc { get; init; }
    public long DurationMs { get; init; }
    public int CapturedFrames { get; init; }
    public int DroppedFrames { get; init; }
    public long? FileSizeBytes { get; init; }
    public string Status { get; init; } = "COMPLETED";
    public string? Error { get; init; }
    public IReadOnlyList<ReplayVideoSyncAnchor> SyncAnchors { get; init; } =
        Array.Empty<ReplayVideoSyncAnchor>();
}
