using System;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.NameOverride;

internal static class NameOverrideSettingsMenuLabel
{
    private const string EnglishLabel = "Anonymous Mode";
    private const string SimplifiedChineseLabel = "匿名模式";
    private const string GermanLabel = "Anonymer Modus";
    private const string PortugueseLabel = "Modo anonimo";
    private const string KoreanLabel = "익명 모드";
    private const string ItalianLabel = "Modalita anonima";

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
