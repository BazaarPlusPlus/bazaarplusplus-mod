using BepInEx.Configuration;

namespace BazaarPlusPlus;

internal sealed partial class CombatStatusBar
{
    private static ConfigEntry<bool>? _enableCombatStatusBarConfig;
    private static ConfigEntry<float>? _defaultCombatSpeedConfig;

    internal static void InitializeConfig(ConfigFile config)
    {
        _enableCombatStatusBarConfig = config.Bind(
            "Combat",
            "EnableCombatStatusBar",
            true,
            "Whether to show the combat status bar with elapsed time and speed controls"
        );
        _defaultCombatSpeedConfig = config.Bind(
            "Combat",
            "DefaultSpeedMultiplier",
            1f,
            new ConfigDescription("Default combat playback speed multiplier", new AcceptableValueRange<float>(0.25f, 8f))
        );
        CombatSpeedMultiplier = SetConfiguredDefaultSpeed(_defaultCombatSpeedConfig.Value);
        BppLog.Info(
            "CombatStatusBar",
            $"Combat config initialized: enabled={_enableCombatStatusBarConfig.Value}, speed={CombatSpeedMultiplier:F2}x"
        );
    }

    internal static bool IsEnabled()
    {
        return _enableCombatStatusBarConfig?.Value ?? true;
    }

    static partial void PersistCombatSpeed(float speed)
    {
        if (_defaultCombatSpeedConfig != null)
            _defaultCombatSpeedConfig.Value = speed;
    }

    private static float SetConfiguredDefaultSpeed(float configuredSpeed)
    {
        if (IsSupportedSpeedStep(configuredSpeed))
            return SetCombatSpeed(configuredSpeed);

        CombatSpeedMultiplier = 1f;
        _defaultCombatSpeedConfig!.Value = CombatSpeedMultiplier;
        return CombatSpeedMultiplier;
    }
}
