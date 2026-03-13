#nullable enable

using BepInEx.Configuration;

namespace BazaarPlusPlus;

internal sealed partial class CombatStatusBar
{
    private static ConfigEntry<bool>? _enableCombatStatusBarConfig;
    private static ConfigEntry<float>? _defaultCombatSpeedConfig;

    internal static void InitializeConfig(ConfigFile config)
    {
        _enableCombatStatusBarConfig = config.Bind(
            "CombatStatusBar",
            "Enabled",
            false,
            "Whether to show the combat status bar with elapsed time and speed controls"
        );
        _defaultCombatSpeedConfig = config.Bind(
                "CombatStatusBar",
                "SpeedMultiplier",
                1f,
                new ConfigDescription(
                "Default combat playback speed multiplier. Supported values: 0.25, 0.50, 1.00, 2.00, 3.00, 4.00, 5.00",
                new AcceptableValueRange<float>(0.25f, 5f)
            )
        );
        CombatSpeedMultiplier = SetConfiguredDefaultSpeed(_defaultCombatSpeedConfig.Value);
        BppLog.Info(
            "CombatStatusBar",
            $"Combat config initialized: enabled={_enableCombatStatusBarConfig.Value}, speed={CombatSpeedMultiplier:F2}x"
        );
    }

    internal static bool IsEnabled()
    {
        return _enableCombatStatusBarConfig?.Value ?? false;
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
