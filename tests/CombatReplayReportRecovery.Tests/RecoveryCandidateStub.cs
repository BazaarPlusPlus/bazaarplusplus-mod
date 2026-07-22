#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed record CompletedVideoReportRecoveryCandidate(
    string RecordingId,
    string BattleId,
    string Source,
    string VideoRelativePath
);
