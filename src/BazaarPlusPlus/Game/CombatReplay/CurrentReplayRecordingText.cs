#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CombatReplay;

internal static class CurrentReplayRecordingText
{
    internal static string CueLabel() =>
        L.Resolve(new LocalizedTextSet("Record video", "录制视频", "錄製影片"));
}
