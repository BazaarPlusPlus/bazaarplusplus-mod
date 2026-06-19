#nullable enable
using System;
using System.Text;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.GameInterop.CustomCards;

internal static class BppCustomCardText
{
    public static string ResolveOrEnglish(LocalizedTextSet set)
    {
        try
        {
            return L.Resolve(set);
        }
        catch (InvalidOperationException)
        {
            return set.English ?? string.Empty;
        }
    }

    public static string AllLocales(LocalizedTextSet set)
    {
        var builder = new StringBuilder();
        Append(builder, set.English);
        Append(builder, set.ChineseMainland);
        Append(builder, set.ChineseTaiwan);
        Append(builder, set.ChineseHongKong);
        Append(builder, set.German);
        Append(builder, set.Portuguese);
        Append(builder, set.Korean);
        Append(builder, set.Italian);
        return builder.ToString();
    }

    public static bool HasRequiredText(LocalizedTextSet set) =>
        !string.IsNullOrWhiteSpace(set.English)
        && !string.IsNullOrWhiteSpace(set.ChineseMainland)
        && !string.IsNullOrWhiteSpace(set.ChineseTaiwan)
        && !string.IsNullOrWhiteSpace(set.ChineseHongKong);

    private static void Append(StringBuilder builder, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            builder.Append(value);
    }
}
