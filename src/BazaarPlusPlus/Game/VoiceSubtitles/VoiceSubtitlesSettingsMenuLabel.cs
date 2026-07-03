#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class VoiceSubtitlesSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Voice Subtitles",
        "语音字幕",
        "語音字幕"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
