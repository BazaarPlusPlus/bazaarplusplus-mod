#nullable enable
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal sealed class BppConfig : IBppConfig
{
    public ConfigEntry<bool>? UseNativeMonsterPreviewConfig { get; private set; }

    public ConfigEntry<bool>? EnableNameOverrideConfig { get; private set; }

    public ConfigEntry<bool>? EnchantPreviewAlwaysShowConfig { get; private set; }

    public ConfigEntry<bool>? EnableCombatStatusBarConfig { get; private set; }

    public ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<bool>? EnableRunUploadConfig { get; private set; }

    public void Initialize(ConfigFile config)
    {
        UseNativeMonsterPreviewConfig = config.Bind(
            "MonsterPreview",
            "UseNativePreview",
            false,
            "Whether monster preview should use the game's native preview instead of the BazaarPlusPlus overlay. Does not affect history panel battle previews."
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
            "Whether to show the combat status bar with elapsed time and pause controls"
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
            true,
            "Whether completed run logs should be uploaded in the background while not in a live run."
        );
    }
}
