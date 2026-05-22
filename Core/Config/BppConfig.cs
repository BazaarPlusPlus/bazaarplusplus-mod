#nullable enable
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal sealed class BppConfig : IBppConfig
{
    public ConfigEntry<string>? ItemBoardAnchoredPositionConfig { get; private set; }

    public ConfigEntry<bool>? EnableNameOverrideConfig { get; private set; }

    public ConfigEntry<bool>? EnchantPreviewAlwaysShowConfig { get; private set; }

    public ConfigEntry<bool>? EnableCombatStatusBarConfig { get; private set; }

    public ConfigEntry<float>? CombatStatusBarSpeedMultiplierConfig { get; private set; }

    public ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<BppChineseLocaleMode>? ChineseLocaleModeConfig { get; private set; }

    public ConfigEntry<LegendaryPositionDisplayMode>? LegendaryPositionDisplayModeConfig
    {
        get;
        private set;
    }

    public ConfigEntry<bool>? AutoBazaarEnabled { get; private set; }

    public ConfigEntry<float>? AutoBazaarDecisionIntervalSeconds { get; private set; }

    public ConfigEntry<int>? AutoBazaarHttpListenerPort { get; private set; }

    public ConfigEntry<float>? AutoBazaarHttpEndpointTimeoutSeconds { get; private set; }

    public ConfigEntry<bool>? CombatReplayVideoEnabled { get; private set; }

    public ConfigEntry<int>? CombatReplayVideoFps { get; private set; }

    public ConfigEntry<int>? CombatReplayVideoWidth { get; private set; }

    public ConfigEntry<int>? CombatReplayVideoHeight { get; private set; }

    public ConfigEntry<int>? CombatReplayVideoCrf { get; private set; }

    public ConfigEntry<string>? CombatReplayVideoPreset { get; private set; }

    public ConfigEntry<bool>? CombatReplayVideoForceSpeed1x { get; private set; }

    public ConfigEntry<bool>? CombatReplayVideoSuppressBppOverlays { get; private set; }

    public ConfigEntry<int>? CombatReplayVideoMaxQueuedFrames { get; private set; }

    public void Initialize(ConfigFile config)
    {
        ItemBoardAnchoredPositionConfig = config.Bind(
            "ItemBoard",
            "AnchoredPosition",
            "auto",
            "Anchored position override for the standalone item board overlay. Use 'auto' to follow the source tooltip, or 'x,y' such as '320,-40'."
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
            "Whether to show the combat status bar with elapsed time, speed controls, and pause controls"
        );
        CombatStatusBarSpeedMultiplierConfig = config.Bind(
            "CombatStatusBar",
            "SpeedMultiplier",
            1.0f,
            "Default combat playback speed multiplier. The speed buttons cycle between 0.50, 0.67, and 1.00."
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
        ChineseLocaleModeConfig = config.Bind(
            "Localization",
            "ChineseLocaleMode",
            BppChineseLocaleMode.Mainland,
            "Chinese locale variant for BazaarPlusPlus UI when the game language is Chinese. Cycles between Mainland, Taiwan, and HongKong."
        );
        LegendaryPositionDisplayModeConfig = config.Bind(
            "LegendaryPositionDisplay",
            "Mode",
            LegendaryPositionDisplayMode.Default,
            "How BazaarPlusPlus should rewrite native Legendary leaderboard position labels. Default keeps the original value, Blank clears it, Fixed999999 forces 999999, and PositionWithRating shows '#position | rating'."
        );
        // AutoBazaar
        AutoBazaarEnabled = config.Bind(
            "AutoBazaar",
            "Enabled",
            true,
            "Master switch for the AutoBazaar HTTP endpoint. When true, a loopback HTTP server starts on the configured port. There is no in-game UI for this toggle; edit the cfg file to disable.");
        AutoBazaarDecisionIntervalSeconds = config.Bind(
            "AutoBazaar",
            "DecisionIntervalSeconds",
            1.5f,
            "Mod tick cadence for snapshot publication, in seconds. Clamped to [0.5, 10] at runtime.");
        AutoBazaarHttpListenerPort = config.Bind(
            "AutoBazaar",
            "HttpListenerPort",
            47900,
            "Loopback port for the AutoBazaar HTTP listener. Changing this restarts the listener.");
        AutoBazaarHttpEndpointTimeoutSeconds = config.Bind(
            "AutoBazaar",
            "HttpEndpointTimeoutSeconds",
            3.0f,
            "Maximum time (seconds) the server will block on a POST /v1/actions before returning 503.");
        // CombatReplayVideo
        CombatReplayVideoEnabled = config.Bind(
            "CombatReplayVideo",
            "Enabled",
            false,
            "Master switch for recording saved combat replays as MP4 videos. Requires FFmpeg in <GameRoot>/BazaarPlusPlus/tools/ffmpeg/ or on PATH. Silently disabled if FFmpeg is missing."
        );
        CombatReplayVideoFps = config.Bind(
            "CombatReplayVideo",
            "Fps",
            30,
            "Target frames per second for the recorded MP4. Clamped to [10, 60] at runtime."
        );
        CombatReplayVideoWidth = config.Bind(
            "CombatReplayVideo",
            "Width",
            0,
            "Video width in pixels. 0 means use Screen.width at capture start."
        );
        CombatReplayVideoHeight = config.Bind(
            "CombatReplayVideo",
            "Height",
            0,
            "Video height in pixels. 0 means use Screen.height at capture start."
        );
        CombatReplayVideoCrf = config.Bind(
            "CombatReplayVideo",
            "Crf",
            23,
            "x264 constant rate factor (lower = higher quality, larger file). Clamped to [0, 51] at runtime."
        );
        CombatReplayVideoPreset = config.Bind(
            "CombatReplayVideo",
            "Preset",
            "veryfast",
            "x264 preset name. One of: ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow, placebo."
        );
        CombatReplayVideoForceSpeed1x = config.Bind(
            "CombatReplayVideo",
            "ForceSpeed1x",
            true,
            "If true, the combat playback speed is locked to 1x while recording and restored when the replay ends."
        );
        CombatReplayVideoSuppressBppOverlays = config.Bind(
            "CombatReplayVideo",
            "SuppressBppOverlays",
            true,
            "If true, BPP overlays (settings dock, combat status bar) are hidden while recording so they don't appear in the MP4."
        );
        CombatReplayVideoMaxQueuedFrames = config.Bind(
            "CombatReplayVideo",
            "MaxQueuedFrames",
            90,
            "Maximum frames buffered between GPU readback and the FFmpeg encoder. Frames above this are dropped to protect memory. Default 90 ≈ 3 seconds at 30fps."
        );
    }
}
