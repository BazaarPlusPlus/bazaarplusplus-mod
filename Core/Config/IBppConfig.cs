#nullable enable
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal interface IBppConfig
{
    ConfigEntry<bool>? UseNativeMonsterPreviewConfig { get; }

    ConfigEntry<bool>? EnableNameOverrideConfig { get; }

    ConfigEntry<bool>? EnchantPreviewAlwaysShowConfig { get; }

    ConfigEntry<bool>? EnableCombatStatusBarConfig { get; }

    ConfigEntry<float>? CombatStatusBarSpeedMultiplierConfig { get; }

    ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; }

    ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; }

    ConfigEntry<bool>? EnableRunUploadConfig { get; }

    ConfigEntry<string>? RunUploadEndpointConfig { get; }

    ConfigEntry<string>? RunUploadRegistrationEndpointConfig { get; }

    ConfigEntry<int>? RunUploadStartupDelaySecondsConfig { get; }

    ConfigEntry<int>? RunUploadIntervalSecondsConfig { get; }

    ConfigEntry<int>? RunUploadBatchSizeConfig { get; }

    ConfigEntry<int>? RunUploadRequestTimeoutSecondsConfig { get; }
}
