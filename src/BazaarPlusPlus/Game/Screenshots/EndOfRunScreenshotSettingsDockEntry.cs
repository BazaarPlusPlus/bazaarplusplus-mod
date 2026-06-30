#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.EndOfRunScreenshot;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "EndOfRunScreenshot",
            EndOfRunScreenshotSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => ReadEnabled(config),
                enabled => WriteEnabled(config, enabled)
            )
        );

    private static bool ReadEnabled(IBppConfig config) =>
        config.EndOfRunScreenshotEnabledConfig?.Value ?? true;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.EndOfRunScreenshotEnabledConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}
