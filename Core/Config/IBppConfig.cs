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

    ConfigEntry<bool>? AutoBazaarEnabled { get; }

    ConfigEntry<float>? AutoBazaarDecisionIntervalSeconds { get; }

    ConfigEntry<int>? AutoBazaarHttpListenerPort { get; }

    ConfigEntry<float>? AutoBazaarHttpEndpointTimeoutSeconds { get; }

    ConfigEntry<int>? CombatReplayVideoFps { get; }

    ConfigEntry<int>? CombatReplayVideoWidth { get; }

    ConfigEntry<int>? CombatReplayVideoHeight { get; }

    ConfigEntry<int>? CombatReplayVideoCrf { get; }

    ConfigEntry<string>? CombatReplayVideoPreset { get; }

    ConfigEntry<int>? CombatReplayVideoMaxQueuedFrames { get; }

    ConfigEntry<bool>? BazaarDbUploadEnabled { get; }
}
