#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class CardSetPreviewSponsorTextFormatter
{
    public static string FormatSupportedBy(string sponsorName, string languageCode)
    {
        if (string.IsNullOrWhiteSpace(sponsorName))
            return string.Empty;

        var trimmedName = sponsorName.Trim();
        return LanguageCodeMatcher.IsChinese(languageCode)
            ? $"由 {trimmedName} 支持"
            : $"Supported by {trimmedName}";
    }
}
