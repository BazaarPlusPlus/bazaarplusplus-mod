#nullable enable
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal interface IBppConfig
{
    ConfigEntry<bool>? EnableNameOverrideConfig { get; }

    ConfigEntry<PreviewVisibilityMode>? EnchantPreviewModeConfig { get; }

    ConfigEntry<bool>? EnableCombatStatusBarConfig { get; }

    ConfigEntry<float>? CombatStatusBarSpeedMultiplierConfig { get; }

    ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; }

    ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; }

    ConfigEntry<BppChineseLocaleMode>? ChineseLocaleModeConfig { get; }

    ConfigEntry<LegendaryPositionDisplayMode>? LegendaryPositionDisplayModeConfig { get; }

    ConfigEntry<bool>? BazaarDbUploadEnabled { get; }
}
