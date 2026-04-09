#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.LegendaryPosition;

internal static class LegendaryPositionDisplayFormatter
{
    internal static string Format(string? currentText, int? fallbackPosition)
    {
        var mode =
            BppRuntimeHost.Config.LegendaryPositionDisplayModeConfig?.Value
            ?? LegendaryPositionDisplayMode.Default;

        return mode switch
        {
            LegendaryPositionDisplayMode.Blank => string.Empty,
            LegendaryPositionDisplayMode.Fixed999999 => "999999",
            LegendaryPositionDisplayMode.PositionWithRating => FormatPositionWithRating(
                currentText,
                fallbackPosition
            ),
            _ => currentText ?? fallbackPosition?.ToString() ?? string.Empty,
        };
    }

    private static string FormatPositionWithRating(string? currentText, int? fallbackPosition)
    {
        var position = ResolvePosition(fallbackPosition);
        BppClientCacheBridge.TryGetPlayerRankSnapshot(out _, out var rating, out _);
        if (!position.HasValue || !rating.HasValue)
            return currentText ?? fallbackPosition?.ToString() ?? string.Empty;

        return $"#{position.Value} | {rating.Value}";
    }

    private static int? ResolvePosition(int? fallbackPosition)
    {
        return BppClientCacheBridge.TryGetPlayerLeaderboardPosition(out var cachedPosition)
            && cachedPosition.HasValue
            ? cachedPosition
            : fallbackPosition;
    }
}
