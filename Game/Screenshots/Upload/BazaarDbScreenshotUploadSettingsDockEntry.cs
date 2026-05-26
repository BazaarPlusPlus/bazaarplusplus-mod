#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbScreenshotUploadSettingsDockEntry : ISettingsDockEntry
{
    public int Order => 6;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "BazaarDbUpload",
            BazaarDbScreenshotUploadSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => ReadEnabled(config),
                enabled => WriteEnabled(config, enabled),
                BazaarDbScreenshotUploadController.OnEnabledChanged
            )
        );

    private static bool ReadEnabled(IBppConfig config) =>
        config.BazaarDbUploadEnabled?.Value ?? false;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.BazaarDbUploadEnabled;
        if (entry != null)
            entry.Value = enabled;
    }
}
