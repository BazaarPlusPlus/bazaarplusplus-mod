#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.SteamTimeline;

internal static class SteamTimelineSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Steam Timeline",
        "Steam 时间轴",
        "Steam 時間軸"
    );

    internal static string Resolve(string languageCode) =>
        Labels.Resolve(languageCode, L.CurrentMode);
}
