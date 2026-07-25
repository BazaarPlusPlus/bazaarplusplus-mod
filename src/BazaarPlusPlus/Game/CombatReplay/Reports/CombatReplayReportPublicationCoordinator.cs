#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Game.CombatReplay.ReportAssets;
using BazaarPlusPlus.Game.CombatReplay.ReportData;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure.Files;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal enum CombatReplayReportPublicationFailureKind
{
    None,
    Retryable,
    NonRetryable,
}

internal sealed record CombatReplayReportPublicationTerminal(
    string RecordingId,
    string BattleId,
    bool Succeeded,
    string? ReportHtmlFilePath,
    string? Reason,
    CombatReplayReportPublicationFailureKind FailureKind =
        CombatReplayReportPublicationFailureKind.None
);

/// <summary>
/// Joins a projected battle draft to one concrete recording generation. Battle IDs are used only
/// to stage the pre-recording draft; after <see cref="ObserveVideoStarted"/> every mutable input is
/// owned by the recording ID so recording the same battle twice cannot reuse the first recording's
/// assets, video, retry state, or report path.
/// </summary>
internal sealed class CombatReplayReportPublicationCoordinator
{
    private const int MaxRetainedDrafts = 16;
    private const int MaxPendingGenerations = 16;
    private const int MaxRecentRecordingIds = 256;
    private const long InitialRetryDelayMilliseconds = 250;
    private const long MaximumRetryDelayMilliseconds = 30_000;

    private readonly object _gate = new();
    private readonly CombatReportProjector _projector = new();
    private readonly CombatReplayReportArtifactBuilder _artifactBuilder;
    private readonly Action _ensureViewerInstalled;
    private readonly Action<string, byte[]> _commitReport;
    private readonly Action<Action> _schedule;
    private readonly Func<long> _monotonicMilliseconds;
    private readonly Dictionary<string, CombatReportDocumentV1> _draftsByBattle = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, long> _draftTouchOrder = new(StringComparer.Ordinal);
    private readonly Dictionary<
        string,
        CombatReplayReportRecordingGeneration
    > _generationsByRecording = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inFlightRecordingIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _recentRecordingIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _recentRecordingOrder = new();
    private readonly ConcurrentQueue<CombatReplayReportPublicationTerminal> _terminals = new();
    private long _sequence;

    internal CombatReplayReportPublicationCoordinator(string dataRootDirectoryPath)
        : this(
            dataRootDirectoryPath,
            ViewerArtifactBundle.CreateDefault(),
            null,
            null,
            action => _ = Task.Run(action),
            GetMonotonicMilliseconds
        ) { }

    private static long GetMonotonicMilliseconds() =>
        (long)(Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency));

    /// <summary>Test seam for deterministic scheduling and publication failure injection.</summary>
    internal CombatReplayReportPublicationCoordinator(
        string dataRootDirectoryPath,
        ViewerArtifactBundle viewerArtifacts,
        Action? ensureViewerInstalled,
        Action<string, byte[]>? commitReport,
        Action<Action> schedule,
        Func<long> monotonicMilliseconds
    )
    {
        var paths = new StaticReportPaths(dataRootDirectoryPath);
        var artifacts = viewerArtifacts ?? throw new ArgumentNullException(nameof(viewerArtifacts));
        _artifactBuilder = new CombatReplayReportArtifactBuilder(paths, artifacts);
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _monotonicMilliseconds =
            monotonicMilliseconds ?? throw new ArgumentNullException(nameof(monotonicMilliseconds));

        var installer = new ViewerInstaller(paths, artifacts, new ImmutableArtifactCommitter());
        var reportCommitter = new ImmutableArtifactCommitter();
        _ensureViewerInstalled = ensureViewerInstalled ?? (() => installer.EnsureInstalled());
        _commitReport =
            commitReport
            ?? (
                (destination, bytes) =>
                    reportCommitter.CommitBelowRoot(paths.DataRootDirectoryPath, destination, bytes)
            );
    }

    /// <summary>
    /// Projects a reusable pre-recording draft. A later recording-start event snapshots it into
    /// that recording generation; existing generations are never overwritten by a newer replay.
    /// </summary>
    internal bool TryCaptureDraft(
        PvpBattleManifest manifest,
        NetMessageCombatSim combatMessage,
        out string reason
    )
    {
        try
        {
            var document = _projector.Project(manifest, combatMessage);
            lock (_gate)
            {
                _draftsByBattle[manifest.BattleId] = document;
                _draftTouchOrder[manifest.BattleId] = ++_sequence;

                // This closes the rare ordering race where recording-start is observed before
                // projection. Only an empty generation is filled; a previous snapshot is immutable.
                foreach (var generation in _generationsByRecording.Values)
                {
                    if (
                        generation.Document == null
                        && string.Equals(
                            generation.BattleId,
                            manifest.BattleId,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        // Projected documents are immutable after capture. Keep the reference here
                        // and clone only on the publication worker immediately before applying
                        // recording-specific assets.
                        generation.Document = document;
                    }
                }

                PruneDraftsNoLock();
            }
            reason = string.Empty;
            TryAdvance();
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    internal Task<bool> CaptureDraftAsync(
        PvpBattleManifest manifest,
        NetMessageCombatSim combatMessage
    )
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        try
        {
            _schedule(() =>
            {
                var succeeded = TryCaptureDraft(manifest, combatMessage, out _);
                completion.TrySetResult(succeeded);
            });
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        return completion.Task;
    }

    internal void ObserveVideoStarted(CombatReplayVideoRecordingStarted started)
    {
        if (started == null)
            return;

        if (!TryNormalizeIdentity(started.RecordingId, started.BattleId, out var identityReason))
        {
            CompleteInvalidIdentity(started.RecordingId, started.BattleId, identityReason);
            return;
        }

        lock (_gate)
        {
            if (_recentRecordingIds.Contains(started.RecordingId))
                return;

            var generation = GetOrCreateGenerationNoLock(started.RecordingId, started.BattleId);
            if (generation == null)
                return;
            if (
                generation.Document == null
                && _draftsByBattle.TryGetValue(started.BattleId, out var draft)
            )
            {
                generation.Document = draft;
            }
        }

        TryAdvance();
    }

    internal bool TryGetDraftSnapshot(string battleId, out CombatReportDocumentV1? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(battleId))
            return false;

        lock (_gate)
        {
            if (!_draftsByBattle.TryGetValue(battleId, out var draft))
                return false;
            document = CombatReplayReportArtifactBuilder.CloneDocument(draft);
            return true;
        }
    }

    internal Task<CombatReportDocumentV1?> GetDraftSnapshotAsync(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return Task.FromResult<CombatReportDocumentV1?>(null);

        CombatReportDocumentV1? draft;
        lock (_gate)
            _draftsByBattle.TryGetValue(battleId, out draft);
        if (draft == null)
            return Task.FromResult<CombatReportDocumentV1?>(null);

        var completion = new TaskCompletionSource<CombatReportDocumentV1?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        try
        {
            _schedule(() =>
            {
                try
                {
                    completion.TrySetResult(CombatReplayReportArtifactBuilder.CloneDocument(draft));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        return completion.Task;
    }

    internal void MarkAssetsReady(
        string recordingId,
        string battleId,
        IReadOnlyList<PostCombatReportAssetFile> assetFiles
    )
    {
        if (!TryNormalizeIdentity(recordingId, battleId, out var identityReason))
        {
            CompleteInvalidIdentity(recordingId, battleId, identityReason);
            return;
        }

        var files = (assetFiles ?? Array.Empty<PostCombatReportAssetFile>()).ToArray();
        lock (_gate)
        {
            if (_recentRecordingIds.Contains(recordingId))
                return;
            var generation = GetOrCreateGenerationNoLock(recordingId, battleId);
            if (
                generation == null
                || generation.Assets != null
                || generation.AssetsResolutionStarted
            )
                return;
            generation.AssetsResolutionStarted = true;
        }

        try
        {
            _schedule(() => ResolveAssetsAndMarkReady(recordingId, battleId, files));
        }
        catch (Exception ex)
        {
            CompleteNonRetryable(
                recordingId,
                battleId,
                "Report asset validation could not start: " + DescribeFailure(ex)
            );
        }
    }

    private void ResolveAssetsAndMarkReady(
        string recordingId,
        string battleId,
        IReadOnlyList<PostCombatReportAssetFile> assetFiles
    )
    {
        CombatReplayReportResolvedAssetSet resolved;
        try
        {
            // Each asset degrades independently. Hashing and full PNG decode are deliberately
            // worker work; one corrupt cache object must not erase valid art for the other entities.
            resolved = _artifactBuilder.ResolveAssets(assetFiles);
        }
        catch (Exception ex)
        {
            CompleteNonRetryable(
                recordingId,
                battleId,
                "Report asset validation failed: " + DescribeFailure(ex)
            );
            return;
        }

        lock (_gate)
        {
            if (
                _recentRecordingIds.Contains(recordingId)
                || !_generationsByRecording.TryGetValue(recordingId, out var generation)
                || !string.Equals(generation.BattleId, battleId, StringComparison.Ordinal)
                || generation.Assets != null
            )
            {
                return;
            }
            generation.Assets = resolved;
        }

        TryAdvance();
    }

    internal void ObserveVideoTerminal(
        CombatReplayVideoRecordingCompleted completed,
        string locale,
        bool useTraditionalChinese = false
    )
    {
        if (completed == null || !completed.ArtifactUsable)
            return;

        if (
            !TryNormalizeIdentity(completed.RecordingId, completed.BattleId, out var identityReason)
        )
        {
            CompleteInvalidIdentity(completed.RecordingId, completed.BattleId, identityReason);
            return;
        }

        CombatReplayReportPendingVideoArtifact video;
        try
        {
            if (string.IsNullOrWhiteSpace(completed.FinalFilePath))
                throw new ArgumentException("The completed video path is required.");
            video = new CombatReplayReportPendingVideoArtifact(
                completed.RecordingId,
                completed.BattleId,
                Path.GetFullPath(completed.FinalFilePath),
                NormalizeLocale(locale, useTraditionalChinese),
                completed.SyncAnchors ?? Array.Empty<ReplayVideoSyncAnchor>()
            );
        }
        catch (Exception ex)
        {
            CompleteNonRetryable(
                completed.RecordingId,
                completed.BattleId,
                "The completed video identity or path is invalid: " + DescribeFailure(ex)
            );
            return;
        }

        if (!TryReserveVideo(video, out var attemptCount))
            return;

        try
        {
            _artifactBuilder.ValidateVideo(video);
        }
        catch (Exception ex)
        {
            HandlePublicationFailure(
                new CombatReplayReportPublicationWorkItem(
                    video.RecordingId,
                    video.BattleId,
                    string.Empty,
                    Array.Empty<byte>(),
                    attemptCount
                ),
                ex,
                "The completed video could not be admitted: "
            );
            return;
        }

        lock (_gate)
            _inFlightRecordingIds.Remove(video.RecordingId);

        TryAdvance();
    }

    private bool TryReserveVideo(CombatReplayReportPendingVideoArtifact video, out int attemptCount)
    {
        lock (_gate)
        {
            attemptCount = 0;
            if (_recentRecordingIds.Contains(video.RecordingId))
                return false;

            var generation = GetOrCreateGenerationNoLock(video.RecordingId, video.BattleId);
            if (generation == null || generation.Video != null)
                return false;

            generation.Video = video;
            generation.Locale = video.Locale;
            attemptCount = generation.AttemptCount;
            _inFlightRecordingIds.Add(video.RecordingId);
            return true;
        }
    }

    internal void DrainTerminals(Action<CombatReplayReportPublicationTerminal> observe)
    {
        if (observe == null)
            throw new ArgumentNullException(nameof(observe));

        // Runtime already calls this once per Update, so retrying here self-heals without a timer
        // that can outlive the plugin.
        TryAdvance();
        while (_terminals.TryDequeue(out var terminal))
            observe(terminal);
    }

    private void TryAdvance()
    {
        List<CombatReplayReportPublicationCandidate> ready;
        var now = _monotonicMilliseconds();
        lock (_gate)
        {
            ready = new List<CombatReplayReportPublicationCandidate>();
            foreach (var generation in _generationsByRecording.Values)
            {
                if (
                    _recentRecordingIds.Contains(generation.RecordingId)
                    || _inFlightRecordingIds.Contains(generation.RecordingId)
                    || generation.Document == null
                    || generation.Assets == null
                    || generation.Video == null
                    || generation.NextAttemptAtMilliseconds > now
                )
                {
                    continue;
                }

                _inFlightRecordingIds.Add(generation.RecordingId);
                ready.Add(
                    new CombatReplayReportPublicationCandidate(
                        generation.RecordingId,
                        generation.BattleId,
                        generation.Document,
                        generation.Assets,
                        generation.Video,
                        generation.AttemptCount
                    )
                );
            }
        }

        for (var index = 0; index < ready.Count; index++)
            StartPublication(ready[index]);
    }

    private void StartPublication(CombatReplayReportPublicationCandidate candidate)
    {
        try
        {
            _schedule(() => BuildAndPublish(candidate));
        }
        catch (Exception ex)
        {
            HandlePublicationFailure(
                new CombatReplayReportPublicationWorkItem(
                    candidate.RecordingId,
                    candidate.BattleId,
                    string.Empty,
                    Array.Empty<byte>(),
                    candidate.AttemptCount
                ),
                ex,
                "Static report publication could not start: "
            );
        }
    }

    private void BuildAndPublish(CombatReplayReportPublicationCandidate candidate)
    {
        CombatReplayReportPublicationWorkItem work;
        try
        {
            work = _artifactBuilder.Build(candidate);
        }
        catch (Exception ex)
        {
            HandlePublicationFailure(
                new CombatReplayReportPublicationWorkItem(
                    candidate.RecordingId,
                    candidate.BattleId,
                    string.Empty,
                    Array.Empty<byte>(),
                    candidate.AttemptCount
                ),
                ex,
                "Static report artifact build failed: "
            );
            return;
        }

        Publish(work);
    }

    private void Publish(CombatReplayReportPublicationWorkItem work)
    {
        try
        {
            _ensureViewerInstalled();
            _commitReport(work.ReportHtmlFilePath, work.ReportHtmlBytes);
            CompleteSuccess(work);
        }
        catch (Exception ex)
        {
            HandlePublicationFailure(work, ex, "Static report publication failed: ");
        }
    }

    private void HandlePublicationFailure(
        CombatReplayReportPublicationWorkItem work,
        Exception exception,
        string prefix
    )
    {
        if (IsNonRetryablePublicationFailure(exception))
        {
            CompleteNonRetryable(
                work.RecordingId,
                work.BattleId,
                prefix + DescribeFailure(exception)
            );
            return;
        }

        var shouldEnqueue = false;
        lock (_gate)
        {
            _inFlightRecordingIds.Remove(work.RecordingId);
            if (
                !_recentRecordingIds.Contains(work.RecordingId)
                && _generationsByRecording.TryGetValue(work.RecordingId, out var generation)
            )
            {
                generation.AttemptCount = checked(work.AttemptCount + 1);
                generation.NextAttemptAtMilliseconds = checked(
                    _monotonicMilliseconds() + RetryDelayMilliseconds(generation.AttemptCount)
                );
                shouldEnqueue = true;
            }
        }

        if (shouldEnqueue)
        {
            _terminals.Enqueue(
                new CombatReplayReportPublicationTerminal(
                    work.RecordingId,
                    work.BattleId,
                    false,
                    null,
                    prefix + DescribeFailure(exception),
                    CombatReplayReportPublicationFailureKind.Retryable
                )
            );
        }
    }

    private void CompleteSuccess(CombatReplayReportPublicationWorkItem work)
    {
        var shouldEnqueue = false;
        lock (_gate)
        {
            _inFlightRecordingIds.Remove(work.RecordingId);
            if (!_recentRecordingIds.Contains(work.RecordingId))
            {
                _generationsByRecording.Remove(work.RecordingId);
                RememberRecentRecordingNoLock(work.RecordingId);
                shouldEnqueue = true;
            }
        }

        if (shouldEnqueue)
        {
            _terminals.Enqueue(
                new CombatReplayReportPublicationTerminal(
                    work.RecordingId,
                    work.BattleId,
                    true,
                    work.ReportHtmlFilePath,
                    null
                )
            );
        }
    }

    private CombatReplayReportRecordingGeneration? GetOrCreateGenerationNoLock(
        string recordingId,
        string battleId
    )
    {
        if (_generationsByRecording.TryGetValue(recordingId, out var existing))
        {
            if (!string.Equals(existing.BattleId, battleId, StringComparison.Ordinal))
            {
                var terminal = CompleteNonRetryableNoLock(
                    recordingId,
                    battleId,
                    "A recording ID was observed with conflicting battle identities."
                );
                if (terminal != null)
                    _terminals.Enqueue(terminal);
                return null;
            }
            return existing;
        }

        var generation = new CombatReplayReportRecordingGeneration(
            recordingId,
            battleId,
            ++_sequence
        );
        if (_draftsByBattle.TryGetValue(battleId, out var draft))
            generation.Document = draft;
        _generationsByRecording.Add(recordingId, generation);
        PruneGenerationsNoLock();
        return _generationsByRecording.TryGetValue(recordingId, out var retained) ? retained : null;
    }

    private void CompleteInvalidIdentity(string? recordingId, string? battleId, string reason)
    {
        _terminals.Enqueue(
            new CombatReplayReportPublicationTerminal(
                recordingId ?? string.Empty,
                battleId ?? string.Empty,
                false,
                null,
                reason,
                CombatReplayReportPublicationFailureKind.NonRetryable
            )
        );
    }

    private void CompleteNonRetryable(string recordingId, string battleId, string reason)
    {
        var terminal = default(CombatReplayReportPublicationTerminal);
        lock (_gate)
            terminal = CompleteNonRetryableNoLock(recordingId, battleId, reason);
        if (terminal != null)
            _terminals.Enqueue(terminal);
    }

    private CombatReplayReportPublicationTerminal? CompleteNonRetryableNoLock(
        string recordingId,
        string battleId,
        string reason
    )
    {
        _inFlightRecordingIds.Remove(recordingId);
        _generationsByRecording.Remove(recordingId);
        if (_recentRecordingIds.Contains(recordingId))
            return null;
        RememberRecentRecordingNoLock(recordingId);
        return new CombatReplayReportPublicationTerminal(
            recordingId,
            battleId,
            false,
            null,
            reason,
            CombatReplayReportPublicationFailureKind.NonRetryable
        );
    }

    private void PruneDraftsNoLock()
    {
        while (_draftTouchOrder.Count > MaxRetainedDrafts)
        {
            var oldest = _draftTouchOrder.OrderBy(pair => pair.Value).First().Key;
            _draftTouchOrder.Remove(oldest);
            _draftsByBattle.Remove(oldest);
        }
    }

    private void PruneGenerationsNoLock()
    {
        while (_generationsByRecording.Count > MaxPendingGenerations)
        {
            var oldest = _generationsByRecording
                .Values.Where(generation => !_inFlightRecordingIds.Contains(generation.RecordingId))
                .OrderBy(generation => generation.ArrivalSequence)
                .FirstOrDefault();
            if (oldest == null)
                return;
            var terminal = CompleteNonRetryableNoLock(
                oldest.RecordingId,
                oldest.BattleId,
                "Report inputs did not arrive before the coordinator retention limit."
            );
            if (terminal != null)
                _terminals.Enqueue(terminal);
        }
    }

    private void RememberRecentRecordingNoLock(string recordingId)
    {
        if (!_recentRecordingIds.Add(recordingId))
            return;
        _recentRecordingOrder.Enqueue(recordingId);
        while (_recentRecordingOrder.Count > MaxRecentRecordingIds)
            _recentRecordingIds.Remove(_recentRecordingOrder.Dequeue());
    }

    private static bool TryNormalizeIdentity(
        string? recordingId,
        string? battleId,
        out string reason
    )
    {
        try
        {
            _ = StaticReportPaths.ParseRecordingId(recordingId ?? string.Empty);
            if (string.IsNullOrWhiteSpace(battleId))
                throw new ArgumentException("A battle ID is required.");
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = "The recording-generation identity is invalid: " + DescribeFailure(ex);
            return false;
        }
    }

    private static bool IsNonRetryablePublicationFailure(Exception exception)
    {
        exception = exception.GetBaseException();
        if (
            exception is ArtifactPublicationException
            || exception is ArgumentException
            || exception is InvalidDataException
            || exception is InvalidOperationException
            || exception is NotSupportedException
            || exception is UnauthorizedAccessException
        )
        {
            return true;
        }

        return false;
    }

    private static long RetryDelayMilliseconds(int attemptCount)
    {
        var exponent = Math.Min(Math.Max(attemptCount - 1, 0), 7);
        return Math.Min(
            InitialRetryDelayMilliseconds * (1L << exponent),
            MaximumRetryDelayMilliseconds
        );
    }

    private static string NormalizeLocale(string? locale, bool useTraditionalChinese)
    {
        if (
            string.IsNullOrWhiteSpace(locale)
            || !locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "en";
        }

        return
            useTraditionalChinese
            || locale.IndexOf("hant", StringComparison.OrdinalIgnoreCase) >= 0
            || locale.IndexOf("tw", StringComparison.OrdinalIgnoreCase) >= 0
            || locale.IndexOf("hk", StringComparison.OrdinalIgnoreCase) >= 0
            || locale.IndexOf("mo", StringComparison.OrdinalIgnoreCase) >= 0
            ? "zh-Hant"
            : "zh-CN";
    }

    private static string DescribeFailure(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
}
