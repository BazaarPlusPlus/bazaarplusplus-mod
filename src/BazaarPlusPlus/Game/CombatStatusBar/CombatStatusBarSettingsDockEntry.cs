#nullable enable
using System.Globalization;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.CombatStatusBar,
            "CombatStatusBar",
            CombatStatusBarSettingsMenuLabel.Resolve,
            _ => CombatStatusBarFeature.GetEnabledSettingValue(),
            (_, enabled) => CombatStatusBarFeature.SetEnabledSettingValue(enabled)
        );
}

internal static class CombatStatusBarSpeedSettingsDockEntry
{
    private static readonly float[] SpeedLadder = { 0.5f, 0.67f, 1f };
    private static readonly LocalizedTextSet Label = new("Combat Speed", "战斗速度", "戰鬥速度");

    internal static CyclingSettingsDockEntry<float> Create() =>
        new(
            BppSettingsDockOrder.CombatStatusBarSpeed,
            "CombatStatusBarSpeed",
            languageCode => Label.Resolve(languageCode, L.CurrentMode),
            SpeedLadder,
            config => config.CombatStatusBarSpeedMultiplierConfig?.Value ?? 1f,
            (config, speed) =>
            {
                var entry = config.CombatStatusBarSpeedMultiplierConfig;
                if (entry != null)
                    entry.Value = speed;
            },
            speed => speed < 0.999f,
            (speed, _) => speed.ToString("0.##", CultureInfo.InvariantCulture) + "x"
        );
}
