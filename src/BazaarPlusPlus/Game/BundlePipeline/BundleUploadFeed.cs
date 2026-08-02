#nullable enable
using System.Security.Cryptography;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Game.BundlePipeline;

internal sealed class BundleUploadFeed : IUploadFeed
{
    internal const int MaximumAttemptBatch = 3;

    public UploadFeedKind Kind => UploadFeedKind.Bundle;

    public IUploadFeedSession Activate(
        IBppServices services,
        UploadFeedLogState logState,
        UploadPumpCadence cadence
    ) => new Session(services, cadence);

    private sealed class Session : IUploadFeedSession
    {
        private static readonly TimeSpan PendingRetention = TimeSpan.FromDays(14);
        private static readonly TimeSpan PermanentFileRetention = TimeSpan.FromDays(7);
        private const long SoftLimitBytes = 512L * 1024 * 1024;
        private readonly IBppServices _services;
        private readonly string _databasePath;
        private readonly string _outboxRoot;
        private readonly BundleUploadClient _client;
        private readonly HttpClient _httpClient;
        private readonly int _retryIntervalSeconds;

        internal Session(IBppServices services, UploadPumpCadence cadence)
        {
            _services = services;
            var root = services.Paths.RequireDataRoot();
            _databasePath = PathConstants.RunLogDatabase(root);
            _outboxRoot = PathConstants.BundleOutbox(root);
            var routes =
                ModApiRoutes.TryCreate(ModApiUploadDefaults.ApiBaseUrl)
                ?? throw new InvalidOperationException("V5 mod API route is invalid.");
            _httpClient = BppHttpClientFactory.Create(
                BppPluginVersion.Current,
                "BundleUpload",
                TimeSpan.FromSeconds(ModApiUploadDefaults.RequestTimeoutSeconds)
            );
            _client = new BundleUploadClient(_httpClient, routes);
            _retryIntervalSeconds = cadence.RetryIntervalSeconds;
        }

        public bool IsEnabled => true;

        public async Task<UploadAttemptResult> RunAttemptAsync(CancellationToken cancellationToken)
        {
            Cleanup();
            var rows = ListDue();
            if (rows.Count == 0)
                return UploadAttemptResult.NoWork();
            var observations = new List<UploadAttemptObservation>();
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(_outboxRoot, row.FileName);
                try
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read
                    );
                    if (stream.Length != row.TotalBytes)
                        throw new InvalidDataException("bundle_length_mismatch");
                    string digest;
                    using (var sha256 = SHA256.Create())
                        digest = BitConverter
                            .ToString(sha256.ComputeHash(stream))
                            .Replace("-", string.Empty)
                            .ToLowerInvariant();
                    if (!string.Equals(digest, row.Sha256Hex, StringComparison.Ordinal))
                        throw new InvalidDataException("bundle_digest_mismatch");
                    stream.Seek(0, SeekOrigin.Begin);
                    var response = await _client
                        .UploadAsync(
                            stream,
                            row.TotalBytes,
                            row.ContentDigest,
                            row.BundleId,
                            row.RunId,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    ApplyResponse(row, response);
                    if (
                        response.Disposition
                        is BundleUploadDisposition.Uploaded
                            or BundleUploadDisposition.Equivalent
                    )
                    {
                        File.Delete(path);
                        BundlePipelineLog.Info(
                            BundlePipelineLogEvents.UploadSucceeded,
                            row.RunId,
                            row.BundleId
                        );
                        observations.Add(UploadAttemptObservation.Succeeded(row.RunId));
                    }
                    else if (response.Disposition == BundleUploadDisposition.Transient)
                    {
                        BundlePipelineLog.Warn(
                            BundlePipelineLogEvents.UploadDegraded,
                            response.Code,
                            runId: row.RunId
                        );
                        observations.Add(
                            UploadAttemptObservation.Degraded(
                                row.RunId,
                                UploadLogReasonCode.RemoteUploadFailed
                            )
                        );
                        _services.EventBus.Publish(new UploadArmRequested());
                    }
                    else
                    {
                        BundlePipelineLog.Warn(
                            BundlePipelineLogEvents.UploadDegraded,
                            response.Code,
                            runId: row.RunId
                        );
                        observations.Add(
                            UploadAttemptObservation.Degraded(
                                row.RunId,
                                UploadLogReasonCode.PayloadInvalid
                            )
                        );
                    }
                }
                catch (FileNotFoundException)
                {
                    MarkInvalidAndReseal(row, "pending_file_missing");
                    BundlePipelineLog.Warn(
                        BundlePipelineLogEvents.UploadDegraded,
                        "pending_file_missing",
                        runId: row.RunId
                    );
                    observations.Add(
                        UploadAttemptObservation.Degraded(
                            row.RunId,
                            UploadLogReasonCode.PayloadUnreadable
                        )
                    );
                }
                catch (InvalidDataException ex)
                {
                    MarkInvalidAndReseal(row, ex.Message);
                    BundlePipelineLog.Warn(
                        BundlePipelineLogEvents.UploadDegraded,
                        ex.Message,
                        ex,
                        row.RunId
                    );
                    observations.Add(
                        UploadAttemptObservation.Degraded(
                            row.RunId,
                            UploadLogReasonCode.PayloadInvalid,
                            ex
                        )
                    );
                }
                catch (IOException ex)
                {
                    MarkTransient(row, "file_io_error", ex.Message, null);
                    BundlePipelineLog.Warn(
                        BundlePipelineLogEvents.UploadDegraded,
                        "file_io_error",
                        ex,
                        row.RunId
                    );
                    observations.Add(
                        UploadAttemptObservation.Degraded(
                            row.RunId,
                            UploadLogReasonCode.PayloadUnreadable,
                            ex
                        )
                    );
                }
            }
            return UploadAttemptResult.From(observations);
        }

        public IDisposable? SubscribeArmSignals(Action arm) => null;

        public void Dispose() => _httpClient.Dispose();

        private List<OutboxRow> ListDue()
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT bundle_id, run_id, file_name, content_sha256_hex, content_digest,
                       total_bytes
                FROM {RunLogSchema.BundleOutboxTableName}
                WHERE status = 'pending'
                  AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= $now)
                ORDER BY attempts ASC, sealed_at_utc ASC
                LIMIT {MaximumAttemptBatch};
                """;
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
            using var reader = command.ExecuteReader();
            var rows = new List<OutboxRow>();
            while (reader.Read())
                rows.Add(
                    new OutboxRow(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetInt64(5)
                    )
                );
            return rows;
        }

        private void ApplyResponse(OutboxRow row, BundleUploadResponse response)
        {
            if (response.Disposition == BundleUploadDisposition.Transient)
            {
                MarkTransient(row, response.Code, response.Detail, response.RetryAfterSeconds);
                return;
            }
            using var connection = Open();
            using var command = connection.CreateCommand();
            var uploaded =
                response.Disposition
                is BundleUploadDisposition.Uploaded
                    or BundleUploadDisposition.Equivalent;
            command.CommandText = $"""
                UPDATE {RunLogSchema.BundleOutboxTableName}
                SET status = $status,
                    attempts = attempts + 1,
                    last_attempt_at_utc = $now,
                    failed_at_utc = CASE WHEN $uploaded = 1 THEN NULL ELSE $now END,
                    last_error_code = CASE WHEN $uploaded = 1 THEN NULL ELSE $code END,
                    last_error_detail = CASE WHEN $uploaded = 1 THEN NULL ELSE $detail END,
                    server_request_id = $requestId,
                    server_outcome = $outcome,
                    uploaded_at_utc = CASE WHEN $uploaded = 1 THEN $now ELSE NULL END
                WHERE bundle_id = $bundleId AND status = 'pending';
                """;
            command.Parameters.AddWithValue("$bundleId", row.BundleId);
            command.Parameters.AddWithValue("$status", uploaded ? "uploaded" : "permanent_failure");
            command.Parameters.AddWithValue("$uploaded", uploaded ? 1 : 0);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("$code", response.Code);
            command.Parameters.AddWithValue("$detail", (object?)response.Detail ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$requestId",
                (object?)response.RequestId ?? DBNull.Value
            );
            command.Parameters.AddWithValue("$outcome", (object?)response.Outcome ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        private void MarkTransient(
            OutboxRow row,
            string code,
            string? detail,
            int? retryAfterSeconds
        )
        {
            var delay = Math.Max(_retryIntervalSeconds, retryAfterSeconds ?? 0);
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE {RunLogSchema.BundleOutboxTableName}
                SET attempts = attempts + 1, last_attempt_at_utc = $now,
                    next_attempt_at_utc = $next, last_error_code = $code,
                    last_error_detail = $detail
                WHERE bundle_id = $bundleId AND status = 'pending';
                """;
            command.Parameters.AddWithValue("$bundleId", row.BundleId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
            command.Parameters.AddWithValue(
                "$next",
                DateTimeOffset.UtcNow.AddSeconds(delay).ToString("o")
            );
            command.Parameters.AddWithValue("$code", code);
            command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        private void MarkInvalidAndReseal(OutboxRow row, string reason)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using (var fail = connection.CreateCommand())
            {
                fail.Transaction = transaction;
                fail.CommandText =
                    $"UPDATE {RunLogSchema.BundleOutboxTableName} SET status = 'permanent_failure', failed_at_utc = $now, last_error_code = $reason WHERE bundle_id = $bundleId;";
                fail.Parameters.AddWithValue("$bundleId", row.BundleId);
                fail.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
                fail.Parameters.AddWithValue("$reason", reason);
                fail.ExecuteNonQuery();
            }
            using (var reseal = connection.CreateCommand())
            {
                reseal.Transaction = transaction;
                reseal.CommandText = $"""
                    INSERT OR IGNORE INTO {RunLogSchema.BundleSealJobsTableName} (
                        run_id, state, player_account_id, screenshot_requested,
                        screenshot_state, input_deadline_at_utc
                    )
                    SELECT run_id, 'waiting', player_account_id, bundle_screenshot_requested,
                           CASE WHEN bundle_screenshot_requested = 1 THEN 'waiting' ELSE 'not_requested' END,
                           $now
                    FROM {RunLogSchema.RunsTableName} WHERE run_id = $runId;
                    """;
                reseal.Parameters.AddWithValue("$runId", row.RunId);
                reseal.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
                reseal.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        private void Cleanup()
        {
            Directory.CreateDirectory(_outboxRoot);
            var now = DateTimeOffset.UtcNow;
            using var connection = Open();
            using (var expire = connection.CreateCommand())
            {
                expire.CommandText = $"""
                    UPDATE {RunLogSchema.BundleOutboxTableName}
                    SET status = 'permanent_failure', failed_at_utc = $now,
                        last_error_code = 'local_retention_expired'
                    WHERE status = 'pending' AND sealed_at_utc < $cutoff;
                    """;
                expire.Parameters.AddWithValue("$now", now.ToString("o"));
                expire.Parameters.AddWithValue(
                    "$cutoff",
                    now.Subtract(PendingRetention).ToString("o")
                );
                expire.ExecuteNonQuery();
            }
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT file_name FROM {RunLogSchema.BundleOutboxTableName}
                WHERE (status = 'uploaded')
                   OR (status = 'permanent_failure' AND failed_at_utc < $cutoff)
                ORDER BY COALESCE(failed_at_utc, uploaded_at_utc, sealed_at_utc);
                """;
            command.Parameters.AddWithValue(
                "$cutoff",
                now.Subtract(PermanentFileRetention).ToString("o")
            );
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var path = Path.Combine(_outboxRoot, reader.GetString(0));
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
            EnforceSoftLimit(connection, now);
        }

        private void EnforceSoftLimit(SqliteConnection connection, DateTimeOffset now)
        {
            var files = Directory
                .EnumerateFiles(_outboxRoot, "*.bundle")
                .Select(path => new FileInfo(path))
                .ToList();
            var total = files.Sum(file => file.Length);
            if (total <= SoftLimitBytes)
                return;
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT file_name FROM {RunLogSchema.BundleOutboxTableName}
                WHERE (status = 'permanent_failure')
                   OR (status = 'pending' AND sealed_at_utc < $pendingCutoff)
                ORDER BY sealed_at_utc ASC;
                """;
            command.Parameters.AddWithValue(
                "$pendingCutoff",
                now.Subtract(PendingRetention).ToString("o")
            );
            using var reader = command.ExecuteReader();
            while (reader.Read() && total > SoftLimitBytes)
            {
                var path = Path.Combine(_outboxRoot, reader.GetString(0));
                if (!File.Exists(path))
                    continue;
                var length = new FileInfo(path).Length;
                File.Delete(path);
                total -= length;
            }
        }

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection($"Data Source={_databasePath}");
            connection.Open();
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA busy_timeout = 2000;";
            pragma.ExecuteNonQuery();
            return connection;
        }

        private sealed record OutboxRow(
            string BundleId,
            string RunId,
            string FileName,
            string Sha256Hex,
            string ContentDigest,
            long TotalBytes
        );
    }
}

internal sealed class UploadPumpMount : IBppMountable
{
    private BackgroundUploadPump? _pump;

    public void Mount(UnityEngine.GameObject host, IBppServices services)
    {
        _pump = host.AddComponent<BackgroundUploadPump>();
        _pump.Initialize(services, new BundleUploadFeed());
    }

    public void Unmount(UnityEngine.GameObject host)
    {
        if (_pump == null)
            return;
        UnityEngine.Object.DestroyImmediate(_pump);
        _pump = null;
    }
}
