using System;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.NameOverride;

internal static class NameOverrideSettingsMenuLabel
{
    private const string EnglishLabel = "Anonymous Mode";
    private const string SimplifiedChineseLabel = "匿名模式";

    internal static string Resolve(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return EnglishLabel;

        return IsSimplifiedChinese(languageCode) ? SimplifiedChineseLabel : EnglishLabel;
    }

    private static bool IsSimplifiedChinese(string languageCode)
    {
        return SimplifiedChineseLanguage.Matches(languageCode);
    }
}
