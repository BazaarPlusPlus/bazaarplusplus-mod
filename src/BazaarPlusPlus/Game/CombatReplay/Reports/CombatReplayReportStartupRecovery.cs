#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Infrastructure.Files;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal interface ICombatReplayReportRecoveryPublisher
{
    CombatReplayReportRecoveryQueueOutcome TryQueue(
        CompletedVideoReportRecoveryCandidate candidate,
        string physicalVideoFilePath,
        string locale,
        bool useTraditionalChinese,
        out string reason
    );
}

internal enum CombatReplayReportRecoveryQueueOutcome
{
    Queued,
    Deferred,
    RetryableFailure,
    Failed,
}

internal sealed record CombatReplayReportRecoveryFailure(
    string RecordingId,
    string BattleId,
    string Reason
);

internal sealed record CombatReplayReportRecoverySummary(
    int ExaminedCount,
    int ExistingReportCount,
    int QueuedCount,
    int DeferredCount,
    int FailedCount,
    string? SourceFailure,
    IReadOnlyList<CombatReplayReportRecoveryFailure> Failures
);

/// <summary>
/// Reconciles durable completed-video rows with immutable static report artifacts at startup.
/// Candidate discovery and publication are injected so the filesystem/security policy remains
/// independently testable without loading Unity.
/// </summary>
internal sealed class CombatReplayReportStartupRecovery
{
    internal const long InitialRetryDelayMilliseconds = 250;
    internal const long MaximumRetryDelayMilliseconds = 30_000;

    private readonly StaticReportPaths _paths;
    private readonly string _videoRootDirectoryPath;
    private readonly Func<
        int,
        CompletedVideoReportRecoveryCursor?,
        CompletedVideoReportRecoveryPage
    > _listCompletedCandidates;
    private readonly ICombatReplayReportRecoveryPublisher _publisher;
    private readonly Func<string?> _ensureViewerInstalled;
    private readonly Func<long> _monotonicMilliseconds;
    private readonly Dictionary<string, CandidateRetryState> _pendingCandidateRetries = new(
        StringComparer.Ordinal
    );
    private string? _currentViewerBundleId;
    private CompletedVideoReportRecoveryCursor? _nextCandidateCursor;
    private int _consecutiveSourceFailureCount;
    private long _nextSourceAttemptAtMilliseconds;
    private bool _viewerInstalled;
    private bool _sourceExhausted;
    private bool _completed;

    internal CombatReplayReportStartupRecovery(
        string dataRootDirectoryPath,
        string videoRootDirectoryPath,
        Func<
            int,
            CompletedVideoReportRecoveryCursor?,
            CompletedVideoReportRecoveryPage
        > listCompletedCandidates,
        ICombatReplayReportRecoveryPublisher publisher,
        Func<string?>? ensureViewerInstalled = null,
        Func<long>? monotonicMilliseconds = null
    )
    {
        if (string.IsNullOrWhiteSpace(videoRootDirectoryPath))
            throw new ArgumentException(
                "A combat replay video root is required.",
                nameof(videoRootDirectoryPath)
            );

        _paths = new StaticReportPaths(dataRootDirectoryPath);
        _videoRootDirectoryPath = PhysicalPathPolicy.TrimEndingSeparators(
            Path.GetFullPath(videoRootDirectoryPath)
        );
        if (
            !string.Equals(
                _videoRootDirectoryPath,
                PhysicalPathPolicy.TrimEndingSeparators(
                    Path.GetFullPath(_paths.VideoRootDirectoryPath)
                ),
                PhysicalPathPolicy.PathComparison
            )
        )
        {
            throw new ArgumentException(
                "The combat replay video root must be the static report data root's CombatReplayVideos directory.",
                nameof(videoRootDirectoryPath)
            );
        }

        _listCompletedCandidates =
            listCompletedCandidates
            ?? throw new ArgumentNullException(nameof(listCompletedCandidates));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _ensureViewerInstalled = ensureViewerInstalled ?? (() => null);
        _monotonicMilliseconds = monotonicMilliseconds ?? MonotonicMilliseconds;
    }

    internal bool IsCompleted => _completed;

    /// <summary>
    /// Reconciles at most one bounded candidate batch. Runtime calls this at most once per frame.
    /// Discovery advances through a stable keyset cursor, while transient per-candidate and source
    /// failures remain pending behind capped backoff instead of becoming permanently skipped.
    /// </summary>
    internal CombatReplayReportRecoverySummary RecoverNextBatch(
        int batchSize,
        string locale,
        bool useTraditionalChinese = false
    )
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (_completed)
            return EmptySummary();

        var now = _monotonicMilliseconds();
        if (!_viewerInstalled && now < _nextSourceAttemptAtMilliseconds)
            return EmptySummary();
        if (!TryEnsureViewerInstalled(out var viewerFailure, out var viewerFailureIsRetryable))
        {
            var viewerSummary = new RecoverySummaryBuilder { SourceFailure = viewerFailure };
            if (!viewerFailureIsRetryable)
            {
                _completed = true;
                return viewerSummary.Build();
            }

            return RecordSourceFailure(viewerSummary, viewerFailure, now);
        }

        var summary = new RecoverySummaryBuilder();
        RecoverDueCandidateRetries(batchSize, now, locale, useTraditionalChinese, summary);

        var remainingCapacity = batchSize - summary.ExaminedCount;
        if (remainingCapacity > 0 && !_sourceExhausted && now >= _nextSourceAttemptAtMilliseconds)
        {
            CompletedVideoReportRecoveryPage page;
            try
            {
                page =
                    _listCompletedCandidates(remainingCapacity, _nextCandidateCursor)
                    ?? throw new InvalidDataException(
                        "The completed-recording source returned no recovery page."
                    );
                ValidateSourcePage(page, remainingCapacity, _nextCandidateCursor);
            }
            catch (Exception ex)
            {
                return RecordSourceFailure(summary, DescribeFailure(ex), now);
            }

            ResetSourceFailureState();
            if (page.Candidates.Count > 0)
                _nextCandidateCursor = page.NextCursor;
            _sourceExhausted = page.Candidates.Count < remainingCapacity;

            for (var index = 0; index < page.Candidates.Count; index++)
            {
                var candidate = page.Candidates[index];
                if (
                    candidate != null
                    && _pendingCandidateRetries.ContainsKey(candidate.RecordingId)
                )
                {
                    continue;
                }

                RecoverCandidate(
                    candidate,
                    previousAttemptCount: 0,
                    now,
                    locale,
                    useTraditionalChinese,
                    summary
                );
            }
        }

        RefreshCompletionState();
        return summary.Build();
    }

    private static CombatReplayReportRecoverySummary EmptySummary() =>
        new(0, 0, 0, 0, 0, null, Array.Empty<CombatReplayReportRecoveryFailure>());

    private void RecoverDueCandidateRetries(
        int batchSize,
        long now,
        string locale,
        bool useTraditionalChinese,
        RecoverySummaryBuilder summary
    )
    {
        var dueRetries = _pendingCandidateRetries
            .Values.Where(retry => retry.NextAttemptAtMilliseconds <= now)
            .OrderBy(retry => retry.NextAttemptAtMilliseconds)
            .ThenBy(retry => retry.Candidate.RecordingId, StringComparer.Ordinal)
            .Take(batchSize)
            .ToArray();

        for (var index = 0; index < dueRetries.Length; index++)
        {
            var retry = dueRetries[index];
            _pendingCandidateRetries.Remove(retry.Candidate.RecordingId);
            RecoverCandidate(
                retry.Candidate,
                retry.AttemptCount,
                now,
                locale,
                useTraditionalChinese,
                summary
            );
        }
    }

    private void RecoverCandidate(
        CompletedVideoReportRecoveryCandidate? candidate,
        int previousAttemptCount,
        long now,
        string locale,
        bool useTraditionalChinese,
        RecoverySummaryBuilder summary
    )
    {
        summary.ExaminedCount++;
        if (candidate == null)
        {
            summary.AddFailure(null, "A completed video recovery candidate is required.");
            return;
        }

        try
        {
            ValidateCandidateIdentity(candidate);
            var reportFilePath = _paths.GetReportHtmlFilePath(candidate.RecordingId);
            if (File.Exists(reportFilePath))
            {
                ReportPhysicalFile.RequireBelowRoot(
                    _paths.ReportsDirectoryPath,
                    reportFilePath,
                    "Existing combat report"
                );
                if (
                    StaticReportPaths.TryValidateViewerReport(
                        reportFilePath,
                        out _,
                        out _,
                        out _,
                        out var reportReason
                    )
                )
                {
                    summary.ExistingReportCount++;
                    return;
                }

                summary.AddFailure(
                    candidate,
                    string.IsNullOrWhiteSpace(reportReason)
                        ? "The immutable battle report is incomplete or invalid."
                        : reportReason
                );
                return;
            }

            var videoFilePath = ResolvePhysicalVideoFile(candidate.VideoRelativePath);
            var outcome = _publisher.TryQueue(
                candidate,
                videoFilePath,
                locale,
                useTraditionalChinese,
                out var reason
            );
            switch (outcome)
            {
                case CombatReplayReportRecoveryQueueOutcome.Queued:
                    summary.QueuedCount++;
                    return;
                case CombatReplayReportRecoveryQueueOutcome.Deferred:
                    summary.DeferredCount++;
                    QueueCandidateRetry(candidate, previousAttemptCount, now);
                    return;
                case CombatReplayReportRecoveryQueueOutcome.RetryableFailure:
                    var failureReason = string.IsNullOrWhiteSpace(reason)
                        ? "The completed recording could not be queued for report recovery."
                        : reason;
                    summary.AddFailure(candidate, failureReason);
                    QueueCandidateRetry(candidate, previousAttemptCount, now);
                    return;
                case CombatReplayReportRecoveryQueueOutcome.Failed:
                    summary.AddFailure(
                        candidate,
                        string.IsNullOrWhiteSpace(reason)
                            ? "The completed recording cannot produce a battle report."
                            : reason
                    );
                    return;
                default:
                    throw new InvalidDataException(
                        "The report recovery publisher returned an unsupported outcome."
                    );
            }
        }
        catch (Exception ex)
        {
            summary.AddFailure(candidate, DescribeFailure(ex));
            if (IsRetryableCandidateFailure(ex))
                QueueCandidateRetry(candidate, previousAttemptCount, now);
        }
    }

    private void QueueCandidateRetry(
        CompletedVideoReportRecoveryCandidate candidate,
        int previousAttemptCount,
        long now
    )
    {
        var attemptCount = SaturatingIncrement(previousAttemptCount);
        _pendingCandidateRetries[candidate.RecordingId] = new CandidateRetryState(
            candidate,
            attemptCount,
            now + RetryDelayMilliseconds(attemptCount)
        );
    }

    private static bool IsRetryableCandidateFailure(Exception exception)
    {
        exception = exception.GetBaseException();
        return exception
            is not (
                ArtifactPublicationException
                or ArgumentException
                or InvalidDataException
                or NotSupportedException
                or UnauthorizedAccessException
            );
    }

    private static void ValidateSourcePage(
        CompletedVideoReportRecoveryPage page,
        int requestedCount,
        CompletedVideoReportRecoveryCursor? previousCursor
    )
    {
        if (page.Candidates == null)
            throw new InvalidDataException(
                "The completed-recording source returned no candidate list."
            );
        if (page.Candidates.Count > requestedCount)
            throw new InvalidDataException(
                "The completed-recording source exceeded the requested page size."
            );
        if (page.Candidates.Count == 0)
            return;
        if (page.NextCursor == null)
            throw new InvalidDataException(
                "A non-empty completed-recording page requires a continuation cursor."
            );
        if (Equals(page.NextCursor, previousCursor))
            throw new InvalidDataException(
                "The completed-recording source did not advance its continuation cursor."
            );
    }

    private CombatReplayReportRecoverySummary RecordSourceFailure(
        RecoverySummaryBuilder summary,
        string? failure,
        long now
    )
    {
        _consecutiveSourceFailureCount = SaturatingIncrement(_consecutiveSourceFailureCount);
        _nextSourceAttemptAtMilliseconds =
            now + RetryDelayMilliseconds(_consecutiveSourceFailureCount);
        summary.SourceFailure = failure;
        return summary.Build();
    }

    private void ResetSourceFailureState()
    {
        _consecutiveSourceFailureCount = 0;
        _nextSourceAttemptAtMilliseconds = 0;
    }

    private bool TryEnsureViewerInstalled(out string? failure, out bool retryable)
    {
        if (_viewerInstalled)
        {
            failure = null;
            retryable = false;
            return true;
        }

        try
        {
            _currentViewerBundleId = _ensureViewerInstalled();
            if (!string.IsNullOrWhiteSpace(_currentViewerBundleId))
            {
                _currentViewerBundleId = StaticReportPaths.ParseSha256(
                    _currentViewerBundleId,
                    nameof(_currentViewerBundleId)
                );
            }
            _viewerInstalled = true;
            failure = null;
            retryable = false;
            return true;
        }
        catch (Exception ex)
        {
            failure = DescribeFailure(ex);
            retryable = ex.GetBaseException() is not ArtifactPublicationException;
            return false;
        }
    }

    private static long RetryDelayMilliseconds(int attemptCount)
    {
        var exponent = Math.Min(Math.Max(attemptCount - 1, 0), 7);
        return Math.Min(
            InitialRetryDelayMilliseconds * (1L << exponent),
            MaximumRetryDelayMilliseconds
        );
    }

    private static int SaturatingIncrement(int value) =>
        value == int.MaxValue ? int.MaxValue : value + 1;

    private void RefreshCompletionState()
    {
        _completed = _sourceExhausted && _pendingCandidateRetries.Count == 0;
    }

    private sealed record CandidateRetryState(
        CompletedVideoReportRecoveryCandidate Candidate,
        int AttemptCount,
        long NextAttemptAtMilliseconds
    );

    private sealed class RecoverySummaryBuilder
    {
        private readonly List<CombatReplayReportRecoveryFailure> _failures = new();

        internal int ExaminedCount { get; set; }
        internal int ExistingReportCount { get; set; }
        internal int QueuedCount { get; set; }
        internal int DeferredCount { get; set; }
        internal string? SourceFailure { get; set; }

        internal void AddFailure(CompletedVideoReportRecoveryCandidate? candidate, string reason)
        {
            _failures.Add(
                new CombatReplayReportRecoveryFailure(
                    candidate?.RecordingId ?? string.Empty,
                    candidate?.BattleId ?? string.Empty,
                    reason
                )
            );
        }

        internal CombatReplayReportRecoverySummary Build() =>
            new(
                ExaminedCount,
                ExistingReportCount,
                QueuedCount,
                DeferredCount,
                _failures.Count,
                SourceFailure,
                _failures
            );
    }

    private static long MonotonicMilliseconds() =>
        (long)(Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency));

    private string ResolvePhysicalVideoFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("The completed video path must be relative.");

        var segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.None);
        if (segments.Length == 0)
            throw new InvalidDataException("The completed video path is empty.");

        var candidatePath = _videoRootDirectoryPath;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (!IsSafePathSegment(segment))
                throw new InvalidDataException(
                    "The completed video path contains an unsafe segment."
                );
            candidatePath = Path.Combine(candidatePath, segment);
        }

        candidatePath = Path.GetFullPath(candidatePath);
        if (
            !string.Equals(
                Path.GetExtension(candidatePath),
                ".mp4",
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new InvalidDataException("The completed video must be an MP4.");

        ReportPhysicalFile.RequireBelowRoot(
            _videoRootDirectoryPath,
            candidatePath,
            "Completed combat replay video"
        );
        if (new FileInfo(candidatePath).Length <= 0)
            throw new InvalidDataException("The completed combat replay video is empty.");

        // Keep startup recovery's physical-path acceptance exactly within the report envelope's
        // typed file:// URL contract.
        _ = TypedReportSiblingUrl.Parse(_paths.BuildVideoRelativeUrl(candidatePath));
        return candidatePath;
    }

    private static void ValidateCandidateIdentity(CompletedVideoReportRecoveryCandidate candidate)
    {
        _ = StaticReportPaths.ParseRecordingId(candidate.RecordingId);
        if (!IsLowerHexIdentity(candidate.BattleId))
        {
            throw new InvalidDataException(
                "A recovery battle ID must be 32 lowercase hexadecimal characters."
            );
        }
    }

    private static bool IsSafePathSegment(string segment)
    {
        if (
            string.IsNullOrEmpty(segment)
            || segment == "."
            || segment == ".."
            || segment.IndexOf(':') >= 0
            || segment.IndexOf('%') >= 0
            || segment.IndexOf('\0') >= 0
        )
        {
            return false;
        }

        for (var index = 0; index < segment.Length; index++)
        {
            if (segment[index] <= '\u001f')
                return false;
        }

        return true;
    }

    private static bool IsLowerHexIdentity(string? value)
    {
        if (value == null || value.Length != 32)
            return false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
                return false;
        }
        return true;
    }

    private static string DescribeFailure(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return string.IsNullOrWhiteSpace(message)
            ? exception.GetBaseException().GetType().Name
            : message;
    }
}
