#nullable enable

using BepInEx.Configuration;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar
{
    private static ConfigEntry<bool>? _enableCombatStatusBarConfig;
    private static ConfigEntry<bool>? _visibleCombatStatusBarConfig;
    private static ConfigEntry<float>? _defaultCombatSpeedConfig;

    internal static void InitializeConfig(ConfigFile config)
    {
        _enableCombatStatusBarConfig = config.Bind(
            "CombatStatusBar",
            "Enabled",
            false,
            "Whether to show the combat status bar with elapsed time and speed controls"
        );
        _visibleCombatStatusBarConfig = config.Bind(
            "CombatStatusBar",
            "Visible",
            true,
            "Whether the combat status bar is currently visible when enabled. Toggled in game with F6."
        );
        _defaultCombatSpeedConfig = config.Bind(
            "CombatStatusBar",
            "SpeedMultiplier",
            1f,
            new ConfigDescription(
                "Default combat playback speed multiplier. Supported values: 0.25, 0.50, 1.00, 1.50, 2.00, 3.00",
                new AcceptableValueList<float>(0.25f, 0.5f, 1f, 1.5f, 2f, 3f)
            )
        );
        IsOverlayVisible = _visibleCombatStatusBarConfig.Value;
        CombatSpeedMultiplier = SetConfiguredDefaultSpeed(_defaultCombatSpeedConfig.Value);
        BppLog.Info(
            "CombatStatusBar",
            $"Combat config initialized: enabled={_enableCombatStatusBarConfig.Value}, visible={IsOverlayVisible}, speed={CombatSpeedMultiplier:F2}x"
        );
    }

    internal static bool IsEnabled()
    {
        return _enableCombatStatusBarConfig?.Value ?? false;
    }

    internal static bool GetEnabledSettingValue()
    {
        return _enableCombatStatusBarConfig?.Value ?? false;
    }

    internal static void SetEnabledSettingValue(bool enabled)
    {
        if (_enableCombatStatusBarConfig != null)
            _enableCombatStatusBarConfig.Value = enabled;
    }

    static partial void PersistOverlayVisibility(bool visible)
    {
        if (_visibleCombatStatusBarConfig != null)
            _visibleCombatStatusBarConfig.Value = visible;
    }

    static partial void PersistCombatSpeed(float speed)
    {
        if (_defaultCombatSpeedConfig != null)
            _defaultCombatSpeedConfig.Value = speed;
    }

    private static float SetConfiguredDefaultSpeed(float configuredSpeed)
    {
        var normalizedSpeed = NormalizeConfiguredDefaultSpeed(configuredSpeed);
        if (normalizedSpeed == configuredSpeed)
            return SetCombatSpeed(configuredSpeed);

        CombatSpeedMultiplier = normalizedSpeed;
        _defaultCombatSpeedConfig!.Value = normalizedSpeed;
        return CombatSpeedMultiplier;
    }
}
