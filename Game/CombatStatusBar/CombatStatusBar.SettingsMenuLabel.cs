using System;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarSettingsMenuLabel
{
    private const string EnglishLabel = "Combat Status Bar | F6 Toggle";
    private const string SimplifiedChineseLabel = "战斗状态栏｜F6 显隐";

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
