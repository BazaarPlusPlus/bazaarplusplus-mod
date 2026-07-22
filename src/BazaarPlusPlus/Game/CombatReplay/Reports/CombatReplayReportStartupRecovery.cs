#nullable enable
using System.Runtime.InteropServices;
using BazaarPlusPlus.Game.CombatReplay.Video;

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
    private readonly StaticReportPaths _paths;
    private readonly string _videoRootDirectoryPath;
    private readonly Func<
        int,
        int,
        IReadOnlyList<CompletedVideoReportRecoveryCandidate>
    > _listCompletedCandidates;
    private readonly ICombatReplayReportRecoveryPublisher _publisher;
    private int _nextCandidateOffset;
    private bool _completed;

    internal CombatReplayReportStartupRecovery(
        string dataRootDirectoryPath,
        string videoRootDirectoryPath,
        Func<int, IReadOnlyList<CompletedVideoReportRecoveryCandidate>> listCompletedCandidates,
        ICombatReplayReportRecoveryPublisher publisher
    )
        : this(
            dataRootDirectoryPath,
            videoRootDirectoryPath,
            (limit, offset) =>
            {
                var candidates =
                    listCompletedCandidates(limit + offset)
                    ?? Array.Empty<CompletedVideoReportRecoveryCandidate>();
                if (offset >= candidates.Count)
                    return Array.Empty<CompletedVideoReportRecoveryCandidate>();
                var count = Math.Min(limit, candidates.Count - offset);
                var page = new CompletedVideoReportRecoveryCandidate[count];
                for (var index = 0; index < count; index++)
                    page[index] = candidates[offset + index];
                return page;
            },
            publisher
        ) { }

    internal CombatReplayReportStartupRecovery(
        string dataRootDirectoryPath,
        string videoRootDirectoryPath,
        Func<
            int,
            int,
            IReadOnlyList<CompletedVideoReportRecoveryCandidate>
        > listCompletedCandidates,
        ICombatReplayReportRecoveryPublisher publisher
    )
    {
        if (string.IsNullOrWhiteSpace(videoRootDirectoryPath))
            throw new ArgumentException(
                "A combat replay video root is required.",
                nameof(videoRootDirectoryPath)
            );

        _paths = new StaticReportPaths(dataRootDirectoryPath);
        _videoRootDirectoryPath = TrimEndingSeparators(Path.GetFullPath(videoRootDirectoryPath));
        if (
            !string.Equals(
                _videoRootDirectoryPath,
                TrimEndingSeparators(Path.GetFullPath(_paths.VideoRootDirectoryPath)),
                PathComparison
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
    }

    internal bool IsCompleted => _completed;

    /// <summary>
    /// Reconciles one bounded database page. Runtime calls this at most once per frame, avoiding
    /// an unbounded startup query plus per-row sync-anchor reads while still advancing a durable
    /// offset until every completed recording has been examined in this process.
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

        IReadOnlyList<CompletedVideoReportRecoveryCandidate> candidates;
        try
        {
            candidates =
                _listCompletedCandidates(batchSize, _nextCandidateOffset)
                ?? Array.Empty<CompletedVideoReportRecoveryCandidate>();
        }
        catch (Exception ex)
        {
            _completed = true;
            return new CombatReplayReportRecoverySummary(
                0,
                0,
                0,
                0,
                0,
                DescribeFailure(ex),
                Array.Empty<CombatReplayReportRecoveryFailure>()
            );
        }

        _nextCandidateOffset += candidates.Count;
        _completed = candidates.Count < batchSize;
        return RecoverCandidates(candidates, batchSize, locale, useTraditionalChinese);
    }

    internal CombatReplayReportRecoverySummary Recover(
        int candidateLimit,
        int maximumQueuedCount,
        string locale,
        bool useTraditionalChinese = false
    )
    {
        if (candidateLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(candidateLimit));
        if (maximumQueuedCount <= 0 || maximumQueuedCount > candidateLimit)
            throw new ArgumentOutOfRangeException(nameof(maximumQueuedCount));

        IReadOnlyList<CompletedVideoReportRecoveryCandidate> candidates;
        try
        {
            candidates =
                _listCompletedCandidates(candidateLimit, 0)
                ?? Array.Empty<CompletedVideoReportRecoveryCandidate>();
        }
        catch (Exception ex)
        {
            return new CombatReplayReportRecoverySummary(
                0,
                0,
                0,
                0,
                0,
                DescribeFailure(ex),
                Array.Empty<CombatReplayReportRecoveryFailure>()
            );
        }

        return RecoverCandidates(candidates, maximumQueuedCount, locale, useTraditionalChinese);
    }

    private CombatReplayReportRecoverySummary RecoverCandidates(
        IReadOnlyList<CompletedVideoReportRecoveryCandidate> candidates,
        int admissionBatchSize,
        string locale,
        bool useTraditionalChinese
    )
    {
        var examinedCount = 0;
        var existingReportCount = 0;
        var queuedCount = 0;
        var deferredCount = 0;
        var failures = new List<CombatReplayReportRecoveryFailure>();
        // admissionBatchSize is a sequential admission-batch size, not a total startup cap. The
        // previous total cap stranded every candidate after the first batch until another game
        // restart. TryQueue completes its admission synchronously, so it is safe to continue with
        // the next bounded batch in the same startup reconciliation.
        for (var batchStart = 0; batchStart < candidates.Count; batchStart += admissionBatchSize)
        {
            var batchEnd = Math.Min(candidates.Count, batchStart + admissionBatchSize);
            for (var index = batchStart; index < batchEnd; index++)
            {
                var candidate = candidates[index];
                examinedCount++;
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
                        existingReportCount++;
                        continue;
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
                            queuedCount++;
                            break;
                        case CombatReplayReportRecoveryQueueOutcome.Deferred:
                            deferredCount++;
                            break;
                        case CombatReplayReportRecoveryQueueOutcome.Failed:
                            throw new InvalidOperationException(
                                string.IsNullOrWhiteSpace(reason)
                                    ? "The completed recording could not be queued for report recovery."
                                    : reason
                            );
                        default:
                            throw new InvalidOperationException(
                                "The report recovery publisher returned an unsupported outcome."
                            );
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(
                        new CombatReplayReportRecoveryFailure(
                            candidate?.RecordingId ?? string.Empty,
                            candidate?.BattleId ?? string.Empty,
                            DescribeFailure(ex)
                        )
                    );
                }
            }
        }

        return new CombatReplayReportRecoverySummary(
            examinedCount,
            existingReportCount,
            queuedCount,
            deferredCount,
            failures.Count,
            null,
            failures
        );
    }

    private static CombatReplayReportRecoverySummary EmptySummary() =>
        new(0, 0, 0, 0, 0, null, Array.Empty<CombatReplayReportRecoveryFailure>());

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
        if (candidate == null)
            throw new InvalidDataException("A completed video recovery candidate is required.");

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

    private static string TrimEndingSeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        while (
            path.Length > (root?.Length ?? 0)
            && (
                path[path.Length - 1] == Path.DirectorySeparatorChar
                || path[path.Length - 1] == Path.AltDirectorySeparatorChar
            )
        )
        {
            path = path.Substring(0, path.Length - 1);
        }
        return path;
    }

    private static StringComparison PathComparison =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
