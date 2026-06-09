#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static string Resolve(LocalizedTextSet set) => L.Resolve(set);

    private static string FormatCount(int count, string noun)
    {
        var languageCode = L.CurrentLanguageCode;
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return $"{noun} {count}";

        return $"{count} {noun}";
    }

    private static string FormatSimple(string english, string chineseMainland)
    {
        return FormatSimple(english, chineseMainland, null, null);
    }

    private static string FormatSimple(
        string english,
        string chineseMainland,
        string? chineseTaiwan,
        string? chineseHongKong
    )
    {
        var languageCode = L.CurrentLanguageCode;
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return ChineseScriptConverter.Convert(
                chineseMainland,
                chineseTaiwan,
                chineseHongKong,
                L.CurrentMode
            );
        }

        return english;
    }

    private static string ResolveChinese(
        string chineseMainland,
        string? chineseTaiwan,
        string? chineseHongKong
    )
    {
        return ChineseScriptConverter.Convert(
            chineseMainland,
            chineseTaiwan,
            chineseHongKong,
            L.CurrentMode
        );
    }

    private static string Pluralize(int count, string singular, string plural)
    {
        return count == 1 ? singular : plural;
    }
}
