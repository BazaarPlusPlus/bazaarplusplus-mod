#nullable enable
using System;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal static class EnchantPreviewSettingsMenuLabel
{
    private const string EnglishLabel = "Always Show Enchant Preview";
    private const string SimplifiedChineseLabel = "始终显示附魔预览";
    private const string GermanLabel = "Verzauberungsvorschau immer anzeigen";
    private const string PortugueseLabel = "Sempre mostrar previa de encantamento";
    private const string KoreanLabel =
        "\ub9c8\ubc95\ubd80\uc5ec \ubbf8\ub9ac\ubcf4\uae30 \ud56d\uc0c1 \ud45c\uc2dc";
    private const string ItalianLabel = "Mostra sempre anteprima incantamento";

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
