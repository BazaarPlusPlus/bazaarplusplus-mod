#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.EndOfRunScreenshot;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        BppSettingsDockDefinition.Toggle(
            "EndOfRunScreenshot",
            EndOfRunScreenshotSettingsMenuLabel.Resolve,
            _ => EndOfRunScreenshotSettingsPolicy.IsEnabledOrForced(config) ? "ON" : "OFF",
            () => EndOfRunScreenshotSettingsPolicy.IsEnabledOrForced(config),
            enabled => WriteEnabled(config, enabled),
            isInteractable: () => !EndOfRunScreenshotSettingsPolicy.IsForcedOn(config),
            activate: () => ToggleEnabled(config)
        );

    private static bool ReadEnabled(IBppConfig config) =>
        config.EndOfRunScreenshotEnabledConfig?.Value ?? true;

    private static void ToggleEnabled(IBppConfig config)
    {
        if (EndOfRunScreenshotSettingsPolicy.IsForcedOn(config))
        {
            EndOfRunScreenshotSettingsPolicy.ForceEnabled(config);
            return;
        }

        WriteEnabled(config, !ReadEnabled(config));
    }

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.EndOfRunScreenshotEnabledConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}
