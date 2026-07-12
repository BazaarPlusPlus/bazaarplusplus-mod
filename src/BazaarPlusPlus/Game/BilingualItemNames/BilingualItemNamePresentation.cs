#nullable enable
using System;

namespace BazaarPlusPlus.Game.BilingualItemNames;

internal static class BilingualItemNamePresentation
{
    private const string SubtitleSize = "42%";
    private const string SubtitleOffset = "-7px";

    internal static string? TryBuild(
        string? primaryTitle,
        string? secondaryTitle,
        bool enabled,
        bool isSupportedCard
    )
    {
        if (
            !enabled
            || !isSupportedCard
            || string.IsNullOrWhiteSpace(primaryTitle)
            || string.IsNullOrWhiteSpace(secondaryTitle)
            || string.Equals(primaryTitle.Trim(), secondaryTitle.Trim(), StringComparison.Ordinal)
        )
            return null;

        return $"{primaryTitle}\n<size={SubtitleSize}><voffset={SubtitleOffset}><noparse>{secondaryTitle.Trim()}</noparse></voffset></size>";
    }
}
