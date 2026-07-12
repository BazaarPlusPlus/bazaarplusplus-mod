#nullable enable
using System;

namespace BazaarPlusPlus.Game.BilingualItemNames;

internal static class BilingualItemNamePresentation
{
    private const string SubtitleSize = "42%";
    private const string SubtitleOffset = "-4px";

    internal static string? TryBuild(
        string? primaryTitle,
        string? chineseTitle,
        bool enabled,
        bool isItem,
        bool currentLanguageIsChinese
    )
    {
        if (
            !enabled
            || !isItem
            || currentLanguageIsChinese
            || string.IsNullOrWhiteSpace(primaryTitle)
            || string.IsNullOrWhiteSpace(chineseTitle)
            || string.Equals(primaryTitle.Trim(), chineseTitle.Trim(), StringComparison.Ordinal)
        )
            return null;

        return $"{primaryTitle}\n<size={SubtitleSize}><voffset={SubtitleOffset}><noparse>{chineseTitle.Trim()}</noparse></voffset></size>";
    }
}
