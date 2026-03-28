#nullable enable
using System;

namespace BazaarPlusPlus.Game.EndOfRun;

internal static class LegendaryEndOfMatchRatingFormatter
{
    private const string PositiveDeltaColor = "#6DD16B";
    private const string NegativeDeltaColor = "#E06767";
    private const string NeutralDeltaColor = "#D8D8D8";

    public static string BuildLine(int before, int after)
    {
        var delta = after - before;
        var deltaText = delta > 0 ? $"+{delta}" : delta.ToString();
        var deltaColor = delta > 0
            ? PositiveDeltaColor
            : delta < 0
                ? NegativeDeltaColor
                : NeutralDeltaColor;

        return $"{before} -> {after} <color={deltaColor}>({deltaText})</color>";
    }
}
