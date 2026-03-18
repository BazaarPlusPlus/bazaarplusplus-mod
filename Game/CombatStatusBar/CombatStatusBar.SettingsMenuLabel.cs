using System;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarSettingsMenuLabel
{
    private const string EnglishLabel = "Combat Status Bar | F6 Toggle";
    private const string SimplifiedChineseLabel = "战斗状态栏 | F6 切换";
    private const string GermanLabel = "Kampfstatusleiste | F6 umschalten";
    private const string PortugueseLabel = "Barra de status do combate | Alternar com F6";
    private const string KoreanLabel = "전투 상태 바 | F6 전환";
    private const string ItalianLabel = "Barra stato combattimento | Attiva/disattiva con F6";

    internal static string Resolve(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return EnglishLabel;

        if (LanguageCodeMatcher.IsSimplifiedChinese(languageCode))
            return SimplifiedChineseLabel;
        if (LanguageCodeMatcher.IsGerman(languageCode))
            return GermanLabel;
        if (LanguageCodeMatcher.IsPortuguese(languageCode))
            return PortugueseLabel;
        if (LanguageCodeMatcher.IsKorean(languageCode))
            return KoreanLabel;
        if (LanguageCodeMatcher.IsItalian(languageCode))
            return ItalianLabel;

        return EnglishLabel;
    }
}
