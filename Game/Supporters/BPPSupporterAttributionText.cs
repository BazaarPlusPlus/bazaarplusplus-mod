#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterAttributionText
{
    public static string FormatSupportedBy(string supporterName, string languageCode)
    {
        if (string.IsNullOrWhiteSpace(supporterName))
            return string.Empty;

        var trimmedName = supporterName.Trim();
        return LanguageCodeMatcher.IsChinese(languageCode)
            ? $"由 {trimmedName} 支持"
            : $"Supported by {trimmedName}";
    }
}
