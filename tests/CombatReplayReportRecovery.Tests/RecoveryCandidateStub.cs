#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed record CompletedVideoReportRecoveryCandidate(
    string RecordingId,
    string BattleId,
    string Source,
    string VideoRelativePath
);

internal sealed record CompletedVideoReportRecoveryCursor(
    string CompletedAtUtc,
    string StartedAtUtc,
    string RecordingId
);

internal sealed record CompletedVideoReportRecoveryPage(
    IReadOnlyList<CompletedVideoReportRecoveryCandidate> Candidates,
    CompletedVideoReportRecoveryCursor? NextCursor
);
