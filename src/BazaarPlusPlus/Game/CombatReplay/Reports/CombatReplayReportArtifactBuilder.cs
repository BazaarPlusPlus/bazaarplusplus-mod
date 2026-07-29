#nullable enable
using BazaarPlusPlus.Game.CombatReplay.ReportAssets;
using BazaarPlusPlus.Game.CombatReplay.ReportData;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Infrastructure.Files;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

/// <summary>
/// Builds one immutable report artifact from a complete recording generation. This collaborator
/// owns CPU/filesystem validation and document enrichment; coordinator state and retry policy stay
/// in <see cref="CombatReplayReportPublicationCoordinator"/>.
/// </summary>
internal sealed class CombatReplayReportArtifactBuilder
{
    private readonly StaticReportPaths _paths;
    private readonly ViewerArtifactBundle _viewerArtifacts;
    private readonly ReportHtmlEmitter _htmlEmitter = new();

    internal CombatReplayReportArtifactBuilder(
        StaticReportPaths paths,
        ViewerArtifactBundle viewerArtifacts
    )
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _viewerArtifacts =
            viewerArtifacts ?? throw new ArgumentNullException(nameof(viewerArtifacts));
    }

    internal CombatReplayReportPublicationWorkItem Build(
        CombatReplayReportPublicationCandidate candidate
    )
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
        var document = CloneDocument(candidate.Document);
        ApplyAssets(document, candidate.Assets);
        CombatReportJson.RefreshDocumentId(document);
        var videoRelativeUrl = _paths.BuildVideoRelativeUrl(candidate.Video.FinalVideoFilePath);
        _ = TypedReportSiblingUrl.Parse(videoRelativeUrl);
        string? scrubVideoRelativeUrl = null;
        if (
            ReplayVideoScrubProxy.TryGetExistingFilePath(
                candidate.Video.FinalVideoFilePath,
                out var scrubVideoFilePath
            )
        )
        {
            ReportPhysicalFile.RequireBelowRoot(
                _paths.VideoRootDirectoryPath,
                scrubVideoFilePath,
                "Recorded combat scrub proxy"
            );
            scrubVideoRelativeUrl = _paths.BuildVideoRelativeUrl(scrubVideoFilePath);
            _ = TypedReportSiblingUrl.Parse(scrubVideoRelativeUrl);
        }
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
            VideoRelativeUrl = videoRelativeUrl,
            ScrubVideoRelativeUrl = scrubVideoRelativeUrl,
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
            BattleDocument = document,
            RecordingManifest = recordingManifest,
        };

        ValidateSchemaCompatibility(envelope);
        ValidateAssetConsistency(envelope);
        var envelopeJson = CombatReportJson.Serialize(envelope);
        var htmlBytes = _htmlEmitter.Emit(
            BuildTitle(document),
            candidate.Video.Locale,
            envelopeJson,
            _viewerArtifacts.BundleId,
            _viewerArtifacts.ScriptSha256,
            _viewerArtifacts.StylesheetSha256
        );

        return new CombatReplayReportPublicationWorkItem(
            recordingId,
            candidate.BattleId,
            _paths.GetReportHtmlFilePath(recordingId),
            htmlBytes,
            candidate.AttemptCount
        );
    }

    internal CombatReplayReportResolvedAssetSet ResolveAssets(
        IReadOnlyList<PostCombatReportAssetFile> assetFiles
    )
    {
        var manifestByDigest = new Dictionary<string, RecordingReportAssetV1>(
            StringComparer.Ordinal
        );
        var entityAssets = new Dictionary<string, CombatReplayReportResolvedBindingAsset>(
            StringComparer.Ordinal
        );
        var eventSemanticAssets = new Dictionary<string, CombatReplayReportResolvedBindingAsset>(
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

        return new CombatReplayReportResolvedAssetSet(
            manifestByDigest
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToList(),
            entityAssets,
            eventSemanticAssets
        );
    }

    internal void ValidateVideo(CombatReplayReportPendingVideoArtifact video)
    {
        ReportPhysicalFile.RequireBelowRoot(
            _paths.VideoRootDirectoryPath,
            video.FinalVideoFilePath,
            "Recorded combat video"
        );
        _ = TypedReportSiblingUrl.Parse(_paths.BuildVideoRelativeUrl(video.FinalVideoFilePath));
        if (
            ReplayVideoScrubProxy.TryGetExistingFilePath(
                video.FinalVideoFilePath,
                out var scrubVideoFilePath
            )
        )
        {
            ReportPhysicalFile.RequireBelowRoot(
                _paths.VideoRootDirectoryPath,
                scrubVideoFilePath,
                "Recorded combat scrub proxy"
            );
            _ = TypedReportSiblingUrl.Parse(_paths.BuildVideoRelativeUrl(scrubVideoFilePath));
        }
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
                PhysicalPathPolicy.PathComparison
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
            new CombatReplayReportResolvedBindingAsset(contentKey, relativeUrl, asset.DisplayName)
        );
    }

    private void ValidateSchemaCompatibility(EmbeddedReportEnvelopeV1 envelope)
    {
        if (
            envelope.SchemaVersion != _viewerArtifacts.ReportSchemaVersion
            || envelope.BattleDocument.SchemaVersion != _viewerArtifacts.ReportSchemaVersion
            || envelope.RecordingManifest.SchemaVersion != _viewerArtifacts.ReportSchemaVersion
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

    private static void ApplyAssets(
        CombatReportDocumentV1 document,
        CombatReplayReportResolvedAssetSet assets
    )
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
            if (!string.IsNullOrWhiteSpace(resolved.DisplayName))
                entity.Name = resolved.DisplayName;
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

    internal static CombatReportDocumentV1 CloneDocument(CombatReportDocumentV1 document) =>
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

    private sealed record ResolvedAsset(
        RecordingReportAssetV1 ManifestAsset,
        CombatReplayReportResolvedBindingAsset BindingAsset
    );
}
