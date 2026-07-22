#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Game.CombatReplay.ReportAssets;
using BazaarPlusPlus.Game.CombatReplay.ReportData;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure.Files;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
    private const string ViewerVersion = ViewerReleaseRegistry.CurrentVersion;
    private const int MaxRetainedDrafts = 16;
    private const int MaxPendingGenerations = 16;
    private const int MaxRecentRecordingIds = 256;
    private const long InitialRetryDelayMilliseconds = 250;
    private const long MaximumRetryDelayMilliseconds = 30_000;

    private readonly object _gate = new();
    private readonly CombatReportProjector _projector = new();
    private readonly StaticReportPaths _paths;
    private readonly ViewerReleaseRegistry _viewerRegistry;
    private readonly ReportHtmlEmitter _htmlEmitter;
    private readonly Action<string> _ensureViewerInstalled;
    private readonly Action<string, byte[]> _commitReport;
    private readonly Action<Action> _schedule;
    private readonly Func<long> _monotonicMilliseconds;
    private readonly Dictionary<string, CombatReportDocumentV1> _draftsByBattle = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, long> _draftTouchOrder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RecordingGeneration> _generationsByRecording = new(
        StringComparer.Ordinal
    );
    private readonly HashSet<string> _inFlightRecordingIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _recentRecordingIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _recentRecordingOrder = new();
    private readonly ConcurrentQueue<CombatReplayReportPublicationTerminal> _terminals = new();
    private long _sequence;

    internal CombatReplayReportPublicationCoordinator(string dataRootDirectoryPath)
        : this(
            dataRootDirectoryPath,
            ViewerReleaseRegistry.CreateDefault(),
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
        ViewerReleaseRegistry viewerRegistry,
        Action<string>? ensureViewerInstalled,
        Action<string, byte[]>? commitReport,
        Action<Action> schedule,
        Func<long> monotonicMilliseconds
    )
    {
        _paths = new StaticReportPaths(dataRootDirectoryPath);
        _viewerRegistry = viewerRegistry ?? throw new ArgumentNullException(nameof(viewerRegistry));
        _htmlEmitter = new ReportHtmlEmitter();
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _monotonicMilliseconds =
            monotonicMilliseconds ?? throw new ArgumentNullException(nameof(monotonicMilliseconds));

        var installer = new ViewerInstaller(
            _paths,
            _viewerRegistry,
            new ImmutableArtifactCommitter()
        );
        var committer = new ImmutableArtifactCommitter();
        _ensureViewerInstalled =
            ensureViewerInstalled ?? (version => installer.EnsureInstalled(version));
        _commitReport =
            commitReport
            ?? (
                (destination, bytes) =>
                    committer.CommitBelowRoot(_paths.DataRootDirectoryPath, destination, bytes)
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
                        generation.Document = CloneDocument(document);
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
                generation.Document = CloneDocument(draft);
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
            document = CloneDocument(draft);
            return true;
        }
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

        // Each asset degrades independently. One corrupt/missing cache object must not erase the
        // valid art captured for every other entity in this recording generation.
        var resolved = ResolveAssets(assetFiles ?? Array.Empty<PostCombatReportAssetFile>());
        lock (_gate)
        {
            if (_recentRecordingIds.Contains(recordingId))
                return;
            var generation = GetOrCreateGenerationNoLock(recordingId, battleId);
            if (generation == null || generation.Assets != null)
                return;
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

        PendingVideoArtifact video;
        try
        {
            if (string.IsNullOrWhiteSpace(completed.FinalFilePath))
                throw new ArgumentException("The completed video path is required.");
            video = new PendingVideoArtifact(
                completed.RecordingId,
                completed.BattleId,
                Path.GetFullPath(completed.FinalFilePath),
                NormalizeLocale(locale, useTraditionalChinese),
                completed.SyncAnchors ?? Array.Empty<ReplayVideoSyncAnchor>()
            );
            ValidateVideo(video);
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

        lock (_gate)
        {
            if (_recentRecordingIds.Contains(video.RecordingId))
                return;
            var generation = GetOrCreateGenerationNoLock(video.RecordingId, video.BattleId);
            if (generation == null || generation.Video != null)
                return;
            generation.Video = video;
            generation.Locale = video.Locale;
        }

        TryAdvance();
    }

    /// <summary>Allows the Runtime update loop to advance transient retries after backoff.</summary>
    internal void RetryPendingPublications() => TryAdvance();

    internal void DrainTerminals(Action<CombatReplayReportPublicationTerminal> observe)
    {
        if (observe == null)
            throw new ArgumentNullException(nameof(observe));

        // Runtime already calls this once per Update, so retrying here self-heals without a timer
        // that can outlive the plugin. RetryPendingPublications remains the explicit test/API seam.
        TryAdvance();
        while (_terminals.TryDequeue(out var terminal))
            observe(terminal);
    }

    private void TryAdvance()
    {
        List<PublicationCandidate> ready;
        var now = _monotonicMilliseconds();
        lock (_gate)
        {
            ready = new List<PublicationCandidate>();
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
                    new PublicationCandidate(
                        generation.RecordingId,
                        generation.BattleId,
                        CloneDocument(generation.Document),
                        generation.Assets,
                        generation.Video,
                        generation.AttemptCount
                    )
                );
            }
        }

        for (var index = 0; index < ready.Count; index++)
        {
            var candidate = ready[index];
            PublicationWorkItem work;
            try
            {
                work = BuildWorkItem(candidate);
            }
            catch (Exception ex)
            {
                CompleteNonRetryable(
                    candidate.RecordingId,
                    candidate.BattleId,
                    "Static report identity, schema, or path validation failed: "
                        + DescribeFailure(ex)
                );
                continue;
            }

            StartPublication(work);
        }
    }

    private PublicationWorkItem BuildWorkItem(PublicationCandidate candidate)
    {
        var recordingId = StaticReportPaths.ParseRecordingId(candidate.RecordingId);
        if (
            !string.Equals(
                candidate.Document.BattleId,
                candidate.BattleId,
                StringComparison.Ordinal
            )
            || !string.Equals(
                candidate.Video.BattleId,
                candidate.BattleId,
                StringComparison.Ordinal
            )
            || !string.Equals(candidate.Video.RecordingId, recordingId, StringComparison.Ordinal)
        )
        {
            throw new InvalidDataException("Report recording-generation identity is inconsistent.");
        }

        ValidateVideo(candidate.Video);
        ApplyAssets(candidate.Document, candidate.Assets);
        CombatReportJson.RefreshDocumentId(candidate.Document);
        var videoRelativeUrl = _paths.BuildVideoRelativeUrl(candidate.Video.FinalVideoFilePath);
        _ = TypedReportSiblingUrl.Parse(videoRelativeUrl);
        var exactSyncAnchors = ReplayVideoSyncMetadata.SelectExactAnchors(
            recordingId,
            candidate.BattleId,
            candidate.Video.SyncAnchors
        );
        var recordingManifest = new RecordingReportManifestV1
        {
            ArtifactId = "report-" + recordingId,
            RecordingId = recordingId,
            BattleId = candidate.BattleId,
            ViewerVersion = ViewerVersion,
            VideoRelativeUrl = videoRelativeUrl,
            SyncMetadataStatus = exactSyncAnchors.Count >= 2 ? "ReadyExact" : "ReadyUnsynced",
            SyncAnchors = exactSyncAnchors
                .Select(anchor => new RecordingReportSyncAnchorV1
                {
                    CombatFrame = anchor.CombatFrame,
                    CombatMs = anchor.CombatMs,
                    MediaPtsMs = anchor.MediaPtsMs,
                    OutputOrdinal = anchor.OutputOrdinal,
                })
                .ToList(),
            Assets = candidate.Assets.ManifestAssets.Select(CloneManifestAsset).ToList(),
        };
        var envelope = new EmbeddedReportEnvelopeV1
        {
            Locale = candidate.Video.Locale,
            BattleDocument = candidate.Document,
            RecordingManifest = recordingManifest,
        };

        ValidateSchemaCompatibility(envelope);
        ValidateAssetConsistency(envelope);
        var envelopeJson = CombatReportJson.Serialize(envelope);
        var htmlBytes = _htmlEmitter.Emit(
            BuildTitle(candidate.Document),
            candidate.Video.Locale,
            envelopeJson,
            ViewerVersion
        );

        return new PublicationWorkItem(
            recordingId,
            candidate.BattleId,
            _paths.GetReportHtmlFilePath(recordingId),
            htmlBytes,
            candidate.AttemptCount
        );
    }

    private void StartPublication(PublicationWorkItem work)
    {
        try
        {
            _schedule(() => Publish(work));
        }
        catch (Exception ex)
        {
            HandlePublicationFailure(work, ex, "Static report publication could not start: ");
        }
    }

    private void Publish(PublicationWorkItem work)
    {
        try
        {
            _ensureViewerInstalled(ViewerVersion);
            _commitReport(work.ReportHtmlFilePath, work.ReportHtmlBytes);
            CompleteSuccess(work);
        }
        catch (Exception ex)
        {
            HandlePublicationFailure(work, ex, "Static report publication failed: ");
        }
    }

    private void HandlePublicationFailure(
        PublicationWorkItem work,
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

    private void CompleteSuccess(PublicationWorkItem work)
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

    private ResolvedReportAssetSet ResolveAssets(
        IReadOnlyList<PostCombatReportAssetFile> assetFiles
    )
    {
        var manifestByDigest = new Dictionary<string, RecordingReportAssetV1>(
            StringComparer.Ordinal
        );
        var entityAssets = new Dictionary<string, ResolvedBindingAsset>(StringComparer.Ordinal);
        var eventSemanticAssets = new Dictionary<string, ResolvedBindingAsset>(
            StringComparer.Ordinal
        );

        for (var index = 0; index < assetFiles.Count; index++)
        {
            var asset = assetFiles[index];
            if (asset == null || string.IsNullOrWhiteSpace(asset.BindingKey))
                continue;

            try
            {
                var resolved = ResolveAsset(asset);
                switch (asset.BindingKind)
                {
                    case PostCombatReportAssetBindingKind.Entity:
                        entityAssets[asset.BindingKey] = resolved.BindingAsset;
                        break;
                    case PostCombatReportAssetBindingKind.EventSemantic:
                        eventSemanticAssets[asset.BindingKey] = resolved.BindingAsset;
                        break;
                    default:
                        continue;
                }
                manifestByDigest.TryAdd(resolved.ManifestAsset.Sha256, resolved.ManifestAsset);
            }
            catch
            {
                // Per-asset degradation is intentional. The entity's URL/key remain cleared and
                // the Viewer renders its deterministic placeholder.
            }
        }

        return new ResolvedReportAssetSet(
            manifestByDigest
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToList(),
            entityAssets,
            eventSemanticAssets
        );
    }

    private ResolvedAsset ResolveAsset(PostCombatReportAssetFile asset)
    {
        ReportPhysicalFile.RequireBelowRoot(
            _paths.AssetRootDirectoryPath,
            asset.FilePath,
            "Captured report card preview"
        );
        var digest = StaticReportIntegrity.Sha256File(asset.FilePath);
        if (
            !string.IsNullOrWhiteSpace(asset.ContentHash)
            && !string.Equals(asset.ContentHash, digest, StringComparison.Ordinal)
        )
        {
            throw new InvalidDataException("A report asset content hash is inconsistent.");
        }

        var expectedObjectPath = Path.Combine(
            _paths.AssetRootDirectoryPath,
            "objects",
            digest.Substring(0, 2),
            digest + ".png"
        );
        if (
            !string.Equals(
                Path.GetFullPath(asset.FilePath),
                Path.GetFullPath(expectedObjectPath),
                PathComparison
            )
        )
        {
            throw new InvalidDataException(
                "A report asset did not resolve to its content-addressed cache object."
            );
        }

        if (!TryDecodePng(asset.FilePath, out var width, out var height))
            throw new InvalidDataException("A report asset is not a fully decodable PNG.");
        if (
            (asset.PixelWidth > 0 && asset.PixelWidth != width)
            || (asset.PixelHeight > 0 && asset.PixelHeight != height)
        )
        {
            throw new InvalidDataException("A report asset's cached geometry is inconsistent.");
        }

        var relativeUrl = StaticReportPaths.BuildAssetRelativeUrl(digest);
        _ = TypedReportSiblingUrl.Parse(relativeUrl);
        var contentKey = "sha256-" + digest;
        return new ResolvedAsset(
            new RecordingReportAssetV1
            {
                ContentKey = contentKey,
                RelativeUrl = relativeUrl,
                SemanticRole = asset.SemanticRole,
                NaturalWidth = width,
                NaturalHeight = height,
                Sha256 = digest,
            },
            new ResolvedBindingAsset(contentKey, relativeUrl)
        );
    }

    private void ValidateSchemaCompatibility(EmbeddedReportEnvelopeV1 envelope)
    {
        var release = _viewerRegistry.GetRequired(ViewerVersion);
        if (
            envelope.SchemaVersion != release.ReportSchemaVersion
            || envelope.BattleDocument.SchemaVersion != release.ReportSchemaVersion
            || envelope.RecordingManifest.SchemaVersion != release.ReportSchemaVersion
            || !string.Equals(
                envelope.RecordingManifest.ViewerVersion,
                release.Version,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidDataException(
                "Viewer, envelope, document, and recording-manifest schemas are incompatible."
            );
        }
    }

    private static void ValidateAssetConsistency(EmbeddedReportEnvelopeV1 envelope)
    {
        var manifestByContentKey = new Dictionary<string, RecordingReportAssetV1>(
            StringComparer.Ordinal
        );
        foreach (var asset in envelope.RecordingManifest.Assets)
        {
            var digest = StaticReportPaths.ParseSha256(asset.Sha256, nameof(asset.Sha256));
            var expectedContentKey = "sha256-" + digest;
            var expectedUrl = StaticReportPaths.BuildAssetRelativeUrl(digest);
            if (
                !string.Equals(asset.ContentKey, expectedContentKey, StringComparison.Ordinal)
                || !string.Equals(asset.RelativeUrl, expectedUrl, StringComparison.Ordinal)
                || !manifestByContentKey.TryAdd(asset.ContentKey, asset)
            )
            {
                throw new InvalidDataException("The report asset manifest is inconsistent.");
            }
        }

        foreach (var entity in envelope.BattleDocument.Entities)
        {
            if (
                string.IsNullOrWhiteSpace(entity.ContentKey)
                && string.IsNullOrWhiteSpace(entity.AssetRelativeUrl)
            )
            {
                continue;
            }
            if (
                string.IsNullOrWhiteSpace(entity.ContentKey)
                || string.IsNullOrWhiteSpace(entity.AssetRelativeUrl)
                || !manifestByContentKey.TryGetValue(entity.ContentKey, out var asset)
                || !string.Equals(
                    asset.RelativeUrl,
                    entity.AssetRelativeUrl,
                    StringComparison.Ordinal
                )
            )
            {
                throw new InvalidDataException(
                    "An entity asset reference is absent from the recording manifest."
                );
            }
        }

        foreach (var reportEvent in envelope.BattleDocument.Events)
        {
            if (
                string.IsNullOrWhiteSpace(reportEvent.IconContentKey)
                && string.IsNullOrWhiteSpace(reportEvent.IconAssetRelativeUrl)
            )
            {
                continue;
            }
            if (
                string.IsNullOrWhiteSpace(reportEvent.IconContentKey)
                || string.IsNullOrWhiteSpace(reportEvent.IconAssetRelativeUrl)
                || !manifestByContentKey.TryGetValue(reportEvent.IconContentKey, out var asset)
                || !string.Equals(
                    asset.RelativeUrl,
                    reportEvent.IconAssetRelativeUrl,
                    StringComparison.Ordinal
                )
            )
            {
                throw new InvalidDataException(
                    "An event icon reference is absent from the recording manifest."
                );
            }
        }
    }

    private static void ApplyAssets(CombatReportDocumentV1 document, ResolvedReportAssetSet assets)
    {
        for (var index = 0; index < document.Entities.Count; index++)
        {
            var entity = document.Entities[index];
            // Always clear the projected/source value first. A missing or invalid asset for this
            // recording must not inherit a URL from a prior application or recording generation.
            entity.ContentKey = null;
            entity.AssetRelativeUrl = null;
            if (!assets.EntityAssets.TryGetValue(entity.EntityId, out var resolved))
                continue;
            entity.ContentKey = resolved.ContentKey;
            entity.AssetRelativeUrl = resolved.RelativeUrl;
        }

        for (var index = 0; index < document.Events.Count; index++)
        {
            var reportEvent = document.Events[index];
            reportEvent.IconContentKey = null;
            reportEvent.IconAssetRelativeUrl = null;
            if (
                string.IsNullOrWhiteSpace(reportEvent.IconSemanticKey)
                || !assets.EventSemanticAssets.TryGetValue(
                    reportEvent.IconSemanticKey,
                    out var resolved
                )
            )
            {
                continue;
            }
            reportEvent.IconContentKey = resolved.ContentKey;
            reportEvent.IconAssetRelativeUrl = resolved.RelativeUrl;
        }
    }

    private RecordingGeneration? GetOrCreateGenerationNoLock(string recordingId, string battleId)
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

        var generation = new RecordingGeneration(recordingId, battleId, ++_sequence);
        if (_draftsByBattle.TryGetValue(battleId, out var draft))
            generation.Document = CloneDocument(draft);
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

    private void ValidateVideo(PendingVideoArtifact video)
    {
        ReportPhysicalFile.RequireBelowRoot(
            _paths.VideoRootDirectoryPath,
            video.FinalVideoFilePath,
            "Recorded combat video"
        );
        _ = TypedReportSiblingUrl.Parse(_paths.BuildVideoRelativeUrl(video.FinalVideoFilePath));
    }

    private static bool IsNonRetryablePublicationFailure(Exception exception)
    {
        if (
            exception is ArgumentException
            || exception is InvalidDataException
            || exception is InvalidOperationException
            || exception is NotSupportedException
            || exception is UnauthorizedAccessException
        )
        {
            return true;
        }

        if (exception is not IOException)
            return false;
        var message = exception.Message ?? string.Empty;
        return message.IndexOf("immutable", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("identity", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("symbolic link", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("reparse", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("escaped", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("must be a file", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static long RetryDelayMilliseconds(int attemptCount)
    {
        var exponent = Math.Min(Math.Max(attemptCount - 1, 0), 7);
        return Math.Min(
            InitialRetryDelayMilliseconds * (1L << exponent),
            MaximumRetryDelayMilliseconds
        );
    }

    private static CombatReportDocumentV1 CloneDocument(CombatReportDocumentV1 document) =>
        JsonConvert.DeserializeObject<CombatReportDocumentV1>(CombatReportJson.Serialize(document))
        ?? throw new InvalidDataException("The projected combat report could not be cloned.");

    private static RecordingReportAssetV1 CloneManifestAsset(RecordingReportAssetV1 asset) =>
        new()
        {
            ContentKey = asset.ContentKey,
            RelativeUrl = asset.RelativeUrl,
            SemanticRole = asset.SemanticRole,
            NaturalWidth = asset.NaturalWidth,
            NaturalHeight = asset.NaturalHeight,
            Sha256 = asset.Sha256,
        };

    private static string BuildTitle(CombatReportDocumentV1 document)
    {
        var player = string.IsNullOrWhiteSpace(document.Summary.PlayerName)
            ? "Player"
            : document.Summary.PlayerName;
        var opponent = string.IsNullOrWhiteSpace(document.Summary.OpponentName)
            ? "Opponent"
            : document.Summary.OpponentName;
        return player + " vs " + opponent + " · BazaarPlusPlus";
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

    private static bool TryDecodePng(string filePath, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan
            );
            using var image = Image.Load<Rgba32>(stream, out var format);
            if (
                format == null
                || !format.FileExtensions.Contains("png", StringComparer.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            width = image.Width;
            height = image.Height;
            return width > 0 && height > 0;
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
                        or UnknownImageFormatException
                        or InvalidImageContentException
            )
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    private static string DescribeFailure(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

    private static StringComparison PathComparison =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed class RecordingGeneration
    {
        internal RecordingGeneration(string recordingId, string battleId, long arrivalSequence)
        {
            RecordingId = recordingId;
            BattleId = battleId;
            ArrivalSequence = arrivalSequence;
        }

        internal string RecordingId { get; }
        internal string BattleId { get; }
        internal long ArrivalSequence { get; }
        internal CombatReportDocumentV1? Document { get; set; }
        internal ResolvedReportAssetSet? Assets { get; set; }
        internal PendingVideoArtifact? Video { get; set; }
        internal string Locale { get; set; } = "en";
        internal int AttemptCount { get; set; }
        internal long NextAttemptAtMilliseconds { get; set; }
    }

    private sealed record PendingVideoArtifact(
        string RecordingId,
        string BattleId,
        string FinalVideoFilePath,
        string Locale,
        IReadOnlyList<ReplayVideoSyncAnchor> SyncAnchors
    );

    private sealed record PublicationCandidate(
        string RecordingId,
        string BattleId,
        CombatReportDocumentV1 Document,
        ResolvedReportAssetSet Assets,
        PendingVideoArtifact Video,
        int AttemptCount
    );

    private sealed record PublicationWorkItem(
        string RecordingId,
        string BattleId,
        string ReportHtmlFilePath,
        byte[] ReportHtmlBytes,
        int AttemptCount
    );

    private sealed record ResolvedAsset(
        RecordingReportAssetV1 ManifestAsset,
        ResolvedBindingAsset BindingAsset
    );

    private sealed record ResolvedBindingAsset(string ContentKey, string RelativeUrl);

    private sealed record ResolvedReportAssetSet(
        IReadOnlyList<RecordingReportAssetV1> ManifestAssets,
        IReadOnlyDictionary<string, ResolvedBindingAsset> EntityAssets,
        IReadOnlyDictionary<string, ResolvedBindingAsset> EventSemanticAssets
    );
}
