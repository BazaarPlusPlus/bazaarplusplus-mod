#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.SteamTimeline;

internal static class SteamTimelineSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create(Action<bool> onChanged) =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.SteamTimeline,
            "SteamTimeline",
            SteamTimelineSettingsMenuLabel.Resolve,
            config => config.EnableSteamTimelineConfig?.Value ?? true,
            (config, enabled) =>
            {
                var entry = config.EnableSteamTimelineConfig;
                if (entry != null)
                    entry.Value = enabled;
            },
            onChanged
        );
}
