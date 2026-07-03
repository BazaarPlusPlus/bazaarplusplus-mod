#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceSubtitlesSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.VoiceSubtitles;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "VoiceSubtitles",
            VoiceSubtitlesSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => ReadEnabled(config),
                enabled => WriteEnabled(config, enabled)
            )
        );

    private static bool ReadEnabled(IBppConfig config) =>
        config.EnableVoiceSubtitlesConfig?.Value ?? false;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.EnableVoiceSubtitlesConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}
