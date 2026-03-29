#nullable enable
using System;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarSettingsMenuLabel
{
    private const string EnglishLabel = "Combat Status Bar | F6 Toggle";
    private const string SimplifiedChineseLabel =
        "\u6218\u6597\u72b6\u6001\u680f | F6 \u5207\u6362";
    private const string GermanLabel = "Kampfstatusleiste | F6 umschalten";
    private const string PortugueseLabel = "Barra de status do combate | Alternar com F6";
    private const string KoreanLabel =
        "\uc804\ud22c \uc0c1\ud0dc \ubc14 | F6 \uc804\ud658";
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
