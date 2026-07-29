#nullable enable
using BazaarPlusPlus.Game.CombatReplay.ReportData;
using BazaarPlusPlus.Game.CombatReplay.Video;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal sealed class CombatReplayReportRecordingGeneration
{
    internal CombatReplayReportRecordingGeneration(
        string recordingId,
        string battleId,
        long arrivalSequence
    )
    {
        RecordingId = recordingId;
        BattleId = battleId;
        ArrivalSequence = arrivalSequence;
    }

    internal string RecordingId { get; }
    internal string BattleId { get; }
    internal long ArrivalSequence { get; }
    internal CombatReportDocumentV1? Document { get; set; }
    internal CombatReplayReportResolvedAssetSet? Assets { get; set; }
    internal bool AssetsResolutionStarted { get; set; }
    internal CombatReplayReportPendingVideoArtifact? Video { get; set; }
    internal int AttemptCount { get; set; }
    internal long NextAttemptAtMilliseconds { get; set; }
}

internal sealed record CombatReplayReportPendingVideoArtifact(
    string RecordingId,
    string BattleId,
    string FinalVideoFilePath,
    string Locale,
    IReadOnlyList<ReplayVideoSyncAnchor> SyncAnchors
);

internal sealed record CombatReplayReportPublicationCandidate(
    string RecordingId,
    string BattleId,
    CombatReportDocumentV1 Document,
    CombatReplayReportResolvedAssetSet Assets,
    CombatReplayReportPendingVideoArtifact Video,
    int AttemptCount
);

internal sealed record CombatReplayReportPublicationWorkItem(
    string RecordingId,
    string BattleId,
    string ReportHtmlFilePath,
    byte[] ReportHtmlBytes,
    int AttemptCount
);

internal sealed record CombatReplayReportResolvedBindingAsset(
    string ContentKey,
    string RelativeUrl,
    string DisplayName
);

internal sealed record CombatReplayReportResolvedAssetSet(
    IReadOnlyList<RecordingReportAssetV1> ManifestAssets,
    IReadOnlyDictionary<string, CombatReplayReportResolvedBindingAsset> EntityAssets,
    IReadOnlyDictionary<string, CombatReplayReportResolvedBindingAsset> EventSemanticAssets
);
