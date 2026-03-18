#nullable enable
using System;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal static class EnchantPreviewSettingsMenuLabel
{
    private const string EnglishLabel = "Always Show Enchant Preview";
    private const string SimplifiedChineseLabel = "始终显示附魔预览";

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
