#nullable enable
using System;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal static class EnchantPreviewSettingsMenuLabel
{
    private const string EnglishLabel = "Enchant Preview Always Show";
    private const string SimplifiedChineseLabel = "附魔预览始终显示";

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
