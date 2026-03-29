#nullable enable
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal sealed class BppConfig : IBppConfig
{
    public ConfigEntry<bool>? EnableMonsterPreviewConfig { get; private set; }

    public ConfigEntry<bool>? EnableNameOverrideConfig { get; private set; }

    public ConfigEntry<bool>? EnchantPreviewAlwaysShowConfig { get; private set; }

    public ConfigEntry<bool>? EnableCombatStatusBarConfig { get; private set; }

    public ConfigEntry<bool>? VisibleCombatStatusBarConfig { get; private set; }

    public ConfigEntry<float>? CombatStatusBarSpeedMultiplierConfig { get; private set; }

    public ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<bool>? EnableRunUploadConfig { get; private set; }

    public ConfigEntry<string>? RunUploadEndpointConfig { get; private set; }

    public ConfigEntry<string>? RunUploadRegistrationEndpointConfig { get; private set; }

    public ConfigEntry<int>? RunUploadStartupDelaySecondsConfig { get; private set; }

    public ConfigEntry<int>? RunUploadIntervalSecondsConfig { get; private set; }

    public ConfigEntry<int>? RunUploadBatchSizeConfig { get; private set; }

    public void Initialize(ConfigFile config)
    {
        EnableMonsterPreviewConfig = config.Bind(
            "MonsterPreview",
            "Enabled",
            true,
            "Whether BazaarPlusPlus should enable its live monster preview overlay and encounter-preview capture. Does not affect history panel battle previews."
        );
        EnableNameOverrideConfig = config.Bind(
            "StreamerMode",
            "EnableNameOverride",
            false,
            "Whether to set the in-game display name to Anonymous"
        );
        EnchantPreviewAlwaysShowConfig = config.Bind(
            "EnchantPreview",
            "AlwaysShow",
            true,
            "Whether to always show enchant preview text in item tooltips. If disabled, hold Ctrl to show it."
        );
        EnableCombatStatusBarConfig = config.Bind(
            "CombatStatusBar",
            "Enabled",
            false,
            "Whether to show the combat status bar with elapsed time and speed controls"
        );
        VisibleCombatStatusBarConfig = config.Bind(
            "CombatStatusBar",
            "Visible",
            true,
            "Whether the combat status bar is currently visible when enabled. Toggled in game with F6."
        );
        CombatStatusBarSpeedMultiplierConfig = config.Bind(
            "CombatStatusBar",
            "SpeedMultiplier",
            1f,
            new ConfigDescription(
                "Default combat playback speed multiplier. Supported values: 0.25, 0.33, 0.50, 1.00",
                new AcceptableValueList<float>(0.25f, 0.33f, 0.5f, 1f)
            )
        );
        EnchantPreviewHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "EnchantPreview",
            "<Keyboard>/ctrl",
            "Binding path for enchant preview tooltip mode."
        );
        UpgradePreviewHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "UpgradePreview",
            "<Keyboard>/shift",
            "Binding path for upgrade preview tooltip mode."
        );
        EnableRunUploadConfig = config.Bind(
            "RunUpload",
            "Enabled",
            false,
            "Whether completed run logs should be uploaded in the background while not in a live run."
        );
        RunUploadEndpointConfig = config.Bind(
            "RunUpload",
            "Endpoint",
            "https://mod-api.bazaarplusplus.com/runs/upload",
            "Run upload endpoint."
        );
        RunUploadRegistrationEndpointConfig = config.Bind(
            "RunUpload",
            "RegistrationEndpoint",
            "https://mod-api.bazaarplusplus.com/clients/register",
            "Run upload client registration endpoint."
        );
        RunUploadStartupDelaySecondsConfig = config.Bind(
            "RunUpload",
            "StartupDelaySeconds",
            20,
            new ConfigDescription(
                "How long to wait after startup before the first background upload attempt.",
                new AcceptableValueRange<int>(5, 600)
            )
        );
        RunUploadIntervalSecondsConfig = config.Bind(
            "RunUpload",
            "IntervalSeconds",
            180,
            new ConfigDescription(
                "How long to wait between background upload attempts while outside a live run.",
                new AcceptableValueRange<int>(15, 3600)
            )
        );
        RunUploadBatchSizeConfig = config.Bind(
            "RunUpload",
            "BatchSize",
            3,
            new ConfigDescription(
                "Maximum number of completed runs to upload in one background batch.",
                new AcceptableValueRange<int>(1, 20)
            )
        );
    }
}
