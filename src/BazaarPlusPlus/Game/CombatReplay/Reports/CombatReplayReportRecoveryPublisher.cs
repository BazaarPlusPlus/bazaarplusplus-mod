#nullable enable
using BazaarPlusPlus.Game.CombatReplay.ReportAssets;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.PvpBattles.Persistence;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

/// <summary>
/// Rehydrates the inputs of the normal publication state machine from durable replay state.
/// Recovery is cache-only: every resolved asset is published and unresolved bindings intentionally
/// use Viewer placeholders. It never loads Addressables or creates Unity render objects.
/// </summary>
internal sealed class CombatReplayReportRecoveryPublisher : ICombatReplayReportRecoveryPublisher
{
    private readonly IPvpBattleCatalog _battleCatalog;
    private readonly CombatReplayPayloadStore _payloadStore;
    private readonly CombatReplayLoader _loader;
    private readonly PostCombatReportNativeCardAssetExporter _cardAssetExporter;
    private readonly PostCombatReportNativeSpriteMaterializer? _nativeSpriteMaterializer;
    private readonly CombatReplayReportPublicationCoordinator _publication;

    internal CombatReplayReportRecoveryPublisher(
        IPvpBattleCatalog battleCatalog,
        CombatReplayPayloadStore payloadStore,
        CombatReplayLoader loader,
        PostCombatReportNativeCardAssetExporter cardAssetExporter,
        PostCombatReportNativeSpriteMaterializer? nativeSpriteMaterializer,
        CombatReplayReportPublicationCoordinator publication
    )
    {
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));
        _payloadStore = payloadStore ?? throw new ArgumentNullException(nameof(payloadStore));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _cardAssetExporter =
            cardAssetExporter ?? throw new ArgumentNullException(nameof(cardAssetExporter));
        _nativeSpriteMaterializer = nativeSpriteMaterializer;
        _publication = publication ?? throw new ArgumentNullException(nameof(publication));
    }

    public CombatReplayReportRecoveryQueueOutcome TryQueue(
        CompletedVideoReportRecoveryCandidate candidate,
        string physicalVideoFilePath,
        string locale,
        bool useTraditionalChinese,
        out string reason
    )
    {
        return TryQueueCore(
            candidate,
            physicalVideoFilePath,
            locale,
            useTraditionalChinese,
            out reason
        );
    }

    private CombatReplayReportRecoveryQueueOutcome TryQueueCore(
        CompletedVideoReportRecoveryCandidate candidate,
        string physicalVideoFilePath,
        string locale,
        bool useTraditionalChinese,
        out string reason
    )
    {
        try
        {
            if (
                !Enum.TryParse<CombatReplayPlaybackSource>(
                    candidate.Source,
                    ignoreCase: false,
                    out var source
                )
                || !Enum.IsDefined(typeof(CombatReplayPlaybackSource), source)
                || !string.Equals(candidate.Source, source.ToString(), StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "The completed video has an unsupported replay source."
                );
            }

            var manifest =
                _battleCatalog.TryLoad(candidate.BattleId)
                ?? throw new InvalidOperationException(
                    "The completed video's battle manifest is unavailable."
                );
            if (!string.Equals(manifest.BattleId, candidate.BattleId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The recovered battle manifest is inconsistent."
                );

            var payload =
                _payloadStore.Load(candidate.BattleId)
                ?? throw new InvalidOperationException(
                    "The completed video's replay payload is unavailable."
                );
            if (!string.Equals(payload.BattleId, candidate.BattleId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The recovered replay payload is inconsistent."
                );

            var sequence = _loader.Load(payload);
            if (!_publication.TryCaptureDraft(manifest, sequence.CombatMessage, out reason))
                return CombatReplayReportRecoveryQueueOutcome.Failed;
            if (
                !_publication.TryGetDraftSnapshot(candidate.BattleId, out var document)
                || document == null
            )
            {
                reason = "The recovered report draft is unavailable.";
                return CombatReplayReportRecoveryQueueOutcome.Failed;
            }

            var availableAssets = new List<PostCombatReportAssetFile>();
            try
            {
                availableAssets.AddRange(_cardAssetExporter.ListAvailableCardAssets(manifest));
            }
            catch
            {
                // A cache read failure is handled by the completeness gate below. Recovery must
                // never turn it into Unity work from this synchronous startup path.
            }

            if (_nativeSpriteMaterializer != null)
            {
                try
                {
                    availableAssets.AddRange(
                        _nativeSpriteMaterializer.ListAvailableAssets(manifest, document)
                    );
                }
                catch
                {
                    // Startup recovery only reuses already committed native cache entries.
                }
            }

            _publication.ObserveVideoStarted(
                new CombatReplayVideoRecordingStarted
                {
                    RecordingId = candidate.RecordingId,
                    BattleId = candidate.BattleId,
                    Source = source,
                }
            );
            _publication.MarkAssetsReady(
                candidate.RecordingId,
                candidate.BattleId,
                availableAssets
            );
            _publication.ObserveVideoTerminal(
                new CombatReplayVideoRecordingCompleted
                {
                    RecordingId = candidate.RecordingId,
                    BattleId = candidate.BattleId,
                    Source = source,
                    FinalFilePath = physicalVideoFilePath,
                    ArtifactUsable = true,
                    AudioStatus = ReplayVideoAudioStatus.Full,
                    MetadataStatus = ReplayVideoMetadataStatus.Complete,
                    ReasonCode = ReplayVideoRecordingReasonCode.Completed,
                    SyncAnchors = candidate.SyncAnchors,
                },
                locale,
                useTraditionalChinese
            );
            reason = string.Empty;
            return CombatReplayReportRecoveryQueueOutcome.Queued;
        }
        catch (Exception ex)
        {
            reason = ex.GetBaseException().Message;
            return CombatReplayReportRecoveryQueueOutcome.Failed;
        }
    }
}
