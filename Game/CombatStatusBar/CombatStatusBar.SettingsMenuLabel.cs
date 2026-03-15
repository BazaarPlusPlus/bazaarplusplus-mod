using System;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarSettingsMenuLabel
{
    private const string EnglishLabel = "Combat Status Bar";
    private const string SimplifiedChineseLabel = "战斗状态栏";

    internal static string Resolve(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return EnglishLabel;

        return IsSimplifiedChinese(languageCode) ? SimplifiedChineseLabel : EnglishLabel;
    }

    private static bool IsSimplifiedChinese(string languageCode)
    {
        return string.Equals(languageCode, "zh-Hans", StringComparison.OrdinalIgnoreCase)
            || string.Equals(languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase);
    }
}
