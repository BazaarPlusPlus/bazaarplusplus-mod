#nullable enable
using System.Collections.Concurrent;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.BundlePipeline;

internal sealed class BundleSealCoordinator : IBppFeature, IDisposable
{
    private static readonly TimeSpan InputConvergenceWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FallbackScanInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OrphanFileRetention = TimeSpan.FromHours(24);
    private readonly IBppServices _services;
    private readonly string _databasePath;
    private readonly string _replayRoot;
    private readonly string _screenshotRoot;
    private readonly string _outboxRoot;
    private readonly RunPayloadComposer _composer;
    private readonly BundleScreenshotEncoder _screenshotEncoder = new();
    private readonly UlidV5Generator _ulid = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly ConcurrentQueue<ScreenshotCaptureTerminal> _screenshotTerminals = new();
    private readonly CancellationTokenSource _shutdown = new();
    private IDisposable? _runInitialized;
    private IDisposable? _runLifecycle;
    private IDisposable? _replayDrained;
    private IDisposable? _screenshotTerminal;
    private Task? _worker;
    private int _wakePending;
    private bool _started;

    internal BundleSealCoordinator(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        var dataRoot = services.Paths.RequireDataRoot();
        _databasePath = PathConstants.RunLogDatabase(dataRoot);
        _replayRoot = PathConstants.CombatReplays(dataRoot);
        _screenshotRoot = PathConstants.Screenshots(dataRoot);
        _outboxRoot = PathConstants.BundleOutbox(dataRoot);
        _composer = new RunPayloadComposer(_databasePath, _replayRoot);
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        Directory.CreateDirectory(_outboxRoot);
        EnsureSchema();
        _runInitialized = _services.EventBus.Subscribe<RunInitializedObserved>(_ => Signal());
        _runLifecycle = _services.EventBus.Subscribe<RunLifecycleChanged>(_ => Signal());
        _replayDrained = _services.EventBus.Subscribe<CombatReplayPersistenceDrained>(_ =>
            Signal()
        );
        _screenshotTerminal = _services.EventBus.Subscribe<ScreenshotCaptureTerminal>(terminal =>
        {
            _screenshotTerminals.Enqueue(terminal);
            Signal();
        });
        _worker = Task.Run(() => WorkerAsync(_shutdown.Token));
        Signal();
    }

    public void Stop() => Dispose();

    public void Dispose()
    {
        if (!_started)
            return;
        _started = false;
        _runInitialized?.Dispose();
        _runLifecycle?.Dispose();
        _replayDrained?.Dispose();
        _screenshotTerminal?.Dispose();
        _shutdown.Cancel();
        _wake.Release();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Shutdown is best effort; jobs are durable and reconcile at next startup.
        }
        _shutdown.Dispose();
        _wake.Dispose();
    }

    internal void Signal()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 0)
            _wake.Release();
    }

    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyScreenshotTerminals();
        RecoverFiles();
        EnsureSealJobs();
        foreach (var runId in ListWaitingRunIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await TrySealAsync(runId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _wake
                    .WaitAsync(FallbackScanInterval, cancellationToken)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _wakePending, 0);
                await ReconcileAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                BundlePipelineLog.Warn(
                    BundlePipelineLogEvents.ReconcileFailed,
                    "reconcile_exception",
                    ex
                );
            }
        }
    }

    private async Task TrySealAsync(string runId, CancellationToken cancellationToken)
    {
        var job = ReadJob(runId);
        if (job == null || job.State == "terminal_failure")
            return;
        var now = DateTimeOffset.UtcNow;
        var deadlineReached = now >= job.InputDeadlineAtUtc;
        if (!deadlineReached && ReplayPersistenceStateTracker.HasPending(runId))
            return;

        string playerAccountId;
        try
        {
            playerAccountId = _composer.ResolvePlayerAccountId(runId, job.PlayerAccountId);
            FreezeResolvedAccountId(runId, playerAccountId);
        }
        catch (BundleCompositionException ex)
        {
            if (!deadlineReached && ex.Code == "player_account_id_missing")
                return;
            MarkJobTerminal(runId, ex.Code, null);
            return;
        }

        BundleScreenshotBuildInputV5? screenshot = null;
        if (job.ScreenshotRequested && job.ScreenshotState != "unavailable")
        {
            var source = TryReadScreenshot(runId);
            if (source == null)
            {
                if (!deadlineReached)
                    return;
                UpdateScreenshotState(runId, "timed_out");
            }
            else
            {
                screenshot = await _screenshotEncoder
                    .EncodeAsync(source.AbsolutePath, source.CapturedAtMs, cancellationToken)
                    .ConfigureAwait(false);
                if (screenshot == null)
                    UpdateScreenshotState(runId, "unavailable");
                else
                    UpdateScreenshotState(runId, "available");
            }
        }

        RunPayloadComposition composition;
        try
        {
            composition = _composer.Compose(runId, playerAccountId);
        }
        catch (BundleCompositionException ex)
        {
            MarkJobTerminal(runId, ex.Code, null);
            return;
        }
        catch (Exception ex)
        {
            MarkJobWaiting(runId, "payload_compose_failed", ex.Message);
            return;
        }

        if (!deadlineReached && composition.Payload.Degradation.ReplayOmittedBattleIds.Count > 0)
            return;
        composition.Payload.Degradation.ScreenshotOmitted =
            job.ScreenshotRequested && screenshot == null;
        var encodedPayload = RunPayloadV5Codec.Encode(composition.Payload);
        if (encodedPayload.Length > BundleLimitsV5.MaxRunBytes)
        {
            MarkJobTerminal(runId, "minimal_run_payload_too_large", null);
            return;
        }

        var allocation = EnsureAllocation(job);
        BundleBuildResultV5 built;
        try
        {
            built = BundleV5Codec.Build(
                new BundleBuildInputV5
                {
                    BundleId = allocation.BundleId,
                    CreatedAtMs = allocation.CreatedAtMs,
                    RunId = runId,
                    PlayerAccountId = playerAccountId,
                    Battles = composition.Projections,
                    RunPayload = encodedPayload,
                    Screenshot = screenshot,
                }
            );
            _ = BundleV5Codec.Open(built.Bytes);
        }
        catch (Exception ex)
        {
            MarkJobTerminal(runId, "bundle_build_failed", ex.Message);
            return;
        }

        var fileName = allocation.BundleId + ".bundle";
        var finalPath = Path.Combine(_outboxRoot, fileName);
        var tempPath = finalPath + ".tmp";
        try
        {
            WriteAtomically(tempPath, finalPath, built.Bytes);
            if (!PublishOutbox(job.RunId, allocation, fileName, built, screenshot != null))
            {
                if (!OutboxContains(allocation.BundleId))
                    File.Delete(finalPath);
                return;
            }
            _services.EventBus.Publish(new UploadArmRequested());
            BundlePipelineLog.Info(
                BundlePipelineLogEvents.SealSucceeded,
                runId,
                allocation.BundleId
            );
        }
        catch (Exception ex)
        {
            MarkJobWaiting(runId, "seal_publish_failed", ex.Message);
        }
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        RunLogSchema.EnsureInitialized(connection);
        using var reset = connection.CreateCommand();
        reset.CommandText = $"""
            UPDATE {RunLogSchema.BundleSealJobsTableName}
            SET state = 'waiting'
            WHERE state = 'sealing';
            """;
        reset.ExecuteNonQuery();
    }

    private void EnsureSealJobs()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT OR IGNORE INTO {RunLogSchema.BundleSealJobsTableName} (
                run_id, state, player_account_id, screenshot_requested, screenshot_state,
                input_deadline_at_utc
            )
            SELECT r.run_id, 'waiting', r.player_account_id, r.bundle_screenshot_requested,
                   CASE WHEN r.bundle_screenshot_requested = 1 THEN 'waiting' ELSE 'not_requested' END,
                   datetime(COALESCE(r.ended_at_utc, r.last_seen_at_utc), '+2 minutes')
            FROM {RunLogSchema.RunsTableName} AS r
            WHERE r.completed = 1
              AND r.status = 'completed'
              AND lower(r.game_mode) = 'ranked'
              AND lower(COALESCE(r.build_channel, 'unknown')) <> 'ptr'
              AND NOT EXISTS (
                  SELECT 1 FROM {RunLogSchema.BundleOutboxTableName} AS o
                  WHERE o.run_id = r.run_id AND o.status IN ('pending', 'uploaded')
              );
            """;
        command.ExecuteNonQuery();
    }

    private void ApplyScreenshotTerminals()
    {
        while (_screenshotTerminals.TryDequeue(out var terminal))
        {
            if (string.IsNullOrWhiteSpace(terminal.RunId))
                continue;
            if (terminal.MetadataPersisted)
                UpdateScreenshotState(terminal.RunId!, "available");
            else if (terminal.ArtifactStatus == ScreenshotArtifactStatus.Unavailable)
                UpdateScreenshotState(terminal.RunId!, "unavailable");
        }
    }

    private void RecoverFiles()
    {
        Directory.CreateDirectory(_outboxRoot);
        foreach (var temp in Directory.EnumerateFiles(_outboxRoot, "*.bundle.tmp"))
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(temp) >= OrphanFileRetention)
                File.Delete(temp);
        }

        using var connection = Open();
        foreach (var file in Directory.EnumerateFiles(_outboxRoot, "*.bundle"))
        {
            var bundleId = Path.GetFileNameWithoutExtension(file);
            if (OutboxContains(connection, bundleId))
                continue;
            try
            {
                var bytes = File.ReadAllBytes(file);
                var opened = BundleV5Codec.Open(bytes);
                var job = ReadJob(connection, opened.Manifest.Run.RunId);
                if (
                    job?.BundleId != opened.Manifest.BundleId
                    || job.CreatedAtMs != opened.Manifest.CreatedAtMs
                    || !string.Equals(
                        job.PlayerAccountId,
                        opened.Manifest.Run.PlayerAccountId,
                        StringComparison.Ordinal
                    )
                )
                {
                    DeleteExpiredOrphan(file);
                    continue;
                }
                using var transaction = connection.BeginTransaction();
                InsertOutbox(
                    connection,
                    transaction,
                    opened.Manifest.BundleId,
                    opened.Manifest.Run.RunId,
                    Path.GetFileName(file),
                    opened.Sha256Hex,
                    opened.ContentDigest,
                    bytes.Length,
                    opened.Screenshot != null
                );
                DeleteJob(connection, transaction, opened.Manifest.Run.RunId);
                transaction.Commit();
            }
            catch
            {
                DeleteExpiredOrphan(file);
            }
        }

        using var pending = connection.CreateCommand();
        pending.CommandText = $"""
            SELECT bundle_id, run_id, file_name
            FROM {RunLogSchema.BundleOutboxTableName}
            WHERE status = 'pending';
            """;
        using var reader = pending.ExecuteReader();
        var invalid = new List<(string BundleId, string RunId, string Reason)>();
        while (reader.Read())
        {
            var path = Path.Combine(_outboxRoot, reader.GetString(2));
            try
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException();
                var opened = BundleV5Codec.Open(File.ReadAllBytes(path));
                if (opened.Manifest.BundleId != reader.GetString(0))
                    throw new InvalidDataException();
            }
            catch
            {
                invalid.Add((reader.GetString(0), reader.GetString(1), "pending_file_invalid"));
            }
        }
        reader.Close();
        foreach (var row in invalid)
            FailOutboxAndScheduleReseal(connection, row.BundleId, row.RunId, row.Reason);
    }

    private static void DeleteExpiredOrphan(string path)
    {
        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= OrphanFileRetention)
            File.Delete(path);
    }

    private List<string> ListWaitingRunIds()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT run_id FROM {RunLogSchema.BundleSealJobsTableName}
            WHERE state = 'waiting'
            ORDER BY input_deadline_at_utc, run_id;
            """;
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    private SealJob? ReadJob(string runId)
    {
        using var connection = Open();
        return ReadJob(connection, runId);
    }

    private static SealJob? ReadJob(SqliteConnection connection, string runId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT run_id, state, player_account_id, screenshot_requested, screenshot_state,
                   input_deadline_at_utc, bundle_id, created_at_ms
            FROM {RunLogSchema.BundleSealJobsTableName}
            WHERE run_id = $runId LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new SealJob(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3) == 1,
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7)
        );
    }

    private BundleAllocation EnsureAllocation(SealJob job)
    {
        if (job.BundleId != null && job.CreatedAtMs.HasValue)
            return new BundleAllocation(job.BundleId, job.CreatedAtMs.Value);
        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var bundleId = _ulid.Next();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {RunLogSchema.BundleSealJobsTableName}
            SET bundle_id = COALESCE(bundle_id, $bundleId),
                created_at_ms = COALESCE(created_at_ms, $createdAtMs),
                state = 'sealing', attempts = attempts + 1,
                last_attempt_at_utc = $now
            WHERE run_id = $runId;
            SELECT bundle_id, created_at_ms
            FROM {RunLogSchema.BundleSealJobsTableName} WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", job.RunId);
        command.Parameters.AddWithValue("$bundleId", bundleId);
        command.Parameters.AddWithValue("$createdAtMs", createdAtMs);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("Seal job disappeared during allocation.");
        return new BundleAllocation(reader.GetString(0), reader.GetInt64(1));
    }

    private ScreenshotSource? TryReadScreenshot(string runId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT image_relative_path, captured_at_utc
            FROM {RunLogSchema.RunScreenshotsTableName}
            WHERE run_id = $runId AND is_primary = 1
            ORDER BY captured_at_utc DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        var relative = reader.GetString(0);
        var absolute = Path.GetFullPath(Path.Combine(_screenshotRoot, relative));
        var root = Path.GetFullPath(_screenshotRoot) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(root, StringComparison.Ordinal) || !File.Exists(absolute))
            return null;
        return new ScreenshotSource(
            absolute,
            DateTimeOffset.Parse(reader.GetString(1)).ToUnixTimeMilliseconds()
        );
    }

    private void FreezeResolvedAccountId(string runId, string accountId)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var run = connection.CreateCommand())
        {
            run.Transaction = transaction;
            run.CommandText =
                $"UPDATE {RunLogSchema.RunsTableName} SET player_account_id = COALESCE(player_account_id, $accountId) WHERE run_id = $runId;";
            run.Parameters.AddWithValue("$runId", runId);
            run.Parameters.AddWithValue("$accountId", accountId);
            run.ExecuteNonQuery();
        }
        using (var job = connection.CreateCommand())
        {
            job.Transaction = transaction;
            job.CommandText =
                $"UPDATE {RunLogSchema.BundleSealJobsTableName} SET player_account_id = COALESCE(player_account_id, $accountId) WHERE run_id = $runId;";
            job.Parameters.AddWithValue("$runId", runId);
            job.Parameters.AddWithValue("$accountId", accountId);
            job.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private bool PublishOutbox(
        string runId,
        BundleAllocation allocation,
        string fileName,
        BundleBuildResultV5 built,
        bool hasScreenshot
    )
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var job = ReadJob(connection, runId);
        if (job?.BundleId != allocation.BundleId || job.CreatedAtMs != allocation.CreatedAtMs)
            return false;
        InsertOutbox(
            connection,
            transaction,
            allocation.BundleId,
            runId,
            fileName,
            built.Sha256Hex,
            built.ContentDigest,
            built.Bytes.Length,
            hasScreenshot
        );
        DeleteJob(connection, transaction, runId);
        transaction.Commit();
        return true;
    }

    private static void InsertOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string bundleId,
        string runId,
        string fileName,
        string sha256,
        string contentDigest,
        long totalBytes,
        bool hasScreenshot
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT OR IGNORE INTO {RunLogSchema.BundleOutboxTableName} (
                bundle_id, run_id, file_name, content_sha256_hex, content_digest,
                total_bytes, has_screenshot, sealed_at_utc, status, next_attempt_at_utc
            ) VALUES (
                $bundleId, $runId, $fileName, $sha256, $contentDigest,
                $totalBytes, $hasScreenshot, $sealedAt, 'pending', $sealedAt
            );
            """;
        command.Parameters.AddWithValue("$bundleId", bundleId);
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$fileName", fileName);
        command.Parameters.AddWithValue("$sha256", sha256);
        command.Parameters.AddWithValue("$contentDigest", contentDigest);
        command.Parameters.AddWithValue("$totalBytes", totalBytes);
        command.Parameters.AddWithValue("$hasScreenshot", hasScreenshot ? 1 : 0);
        command.Parameters.AddWithValue("$sealedAt", DateTimeOffset.UtcNow.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static void DeleteJob(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"DELETE FROM {RunLogSchema.BundleSealJobsTableName} WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);
        command.ExecuteNonQuery();
    }

    private void UpdateScreenshotState(string runId, string state)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"UPDATE {RunLogSchema.BundleSealJobsTableName} SET screenshot_state = $state WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$state", state);
        command.ExecuteNonQuery();
    }

    private void MarkJobWaiting(string runId, string code, string? detail)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"UPDATE {RunLogSchema.BundleSealJobsTableName} SET state = 'waiting', last_error_code = $code, last_error_detail = $detail WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private void MarkJobTerminal(string runId, string code, string? detail)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"UPDATE {RunLogSchema.BundleSealJobsTableName} SET state = 'terminal_failure', last_error_code = $code, last_error_detail = $detail WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
        command.ExecuteNonQuery();
        BundlePipelineLog.Warn(BundlePipelineLogEvents.SealTerminal, code, null, runId);
    }

    private static void WriteAtomically(string tempPath, string finalPath, byte[] bytes)
    {
        using (
            var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)
        )
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, finalPath);
    }

    private bool OutboxContains(string bundleId)
    {
        using var connection = Open();
        return OutboxContains(connection, bundleId);
    }

    private static bool OutboxContains(SqliteConnection connection, string bundleId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT 1 FROM {RunLogSchema.BundleOutboxTableName} WHERE bundle_id = $bundleId LIMIT 1;";
        command.Parameters.AddWithValue("$bundleId", bundleId);
        return command.ExecuteScalar() != null;
    }

    private static void FailOutboxAndScheduleReseal(
        SqliteConnection connection,
        string bundleId,
        string runId,
        string reason
    )
    {
        using var transaction = connection.BeginTransaction();
        using (var fail = connection.CreateCommand())
        {
            fail.Transaction = transaction;
            fail.CommandText =
                $"UPDATE {RunLogSchema.BundleOutboxTableName} SET status = 'permanent_failure', failed_at_utc = $now, last_error_code = $reason WHERE bundle_id = $bundleId;";
            fail.Parameters.AddWithValue("$bundleId", bundleId);
            fail.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
            fail.Parameters.AddWithValue("$reason", reason);
            fail.ExecuteNonQuery();
        }
        using (var reseal = connection.CreateCommand())
        {
            reseal.Transaction = transaction;
            reseal.CommandText = $"""
                INSERT OR IGNORE INTO {RunLogSchema.BundleSealJobsTableName} (
                    run_id, state, player_account_id, screenshot_requested, screenshot_state,
                    input_deadline_at_utc
                )
                SELECT run_id, 'waiting', player_account_id, bundle_screenshot_requested,
                       CASE WHEN bundle_screenshot_requested = 1 THEN 'waiting' ELSE 'not_requested' END,
                       $now
                FROM {RunLogSchema.RunsTableName} WHERE run_id = $runId;
                """;
            reseal.Parameters.AddWithValue("$runId", runId);
            reseal.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
            reseal.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 2000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private sealed record SealJob(
        string RunId,
        string State,
        string? PlayerAccountId,
        bool ScreenshotRequested,
        string ScreenshotState,
        DateTimeOffset InputDeadlineAtUtc,
        string? BundleId,
        long? CreatedAtMs
    );

    private sealed record BundleAllocation(string BundleId, long CreatedAtMs);

    private sealed record ScreenshotSource(string AbsolutePath, long CapturedAtMs);
}
