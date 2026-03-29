#nullable enable
using System;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class MonsterPreviewSettingsMenuLabel
{
    private const string EnglishLabel = "Use Native Monster Preview";
    private const string SimplifiedChineseLabel = "\u4f7f\u7528\u539f\u751f\u91ce\u602a\u9884\u89c8";
    private const string GermanLabel = "Native Monstervorschau verwenden";
    private const string PortugueseLabel = "Usar previa nativa de monstro";
    private const string KoreanLabel = "\uae30\ubcf8 \ubaac\uc2a4\ud130 \ubbf8\ub9ac\ubcf4\uae30 \uc0ac\uc6a9";
    private const string ItalianLabel = "Usa anteprima mostro nativa";

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
