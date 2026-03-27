#nullable enable
using System;

namespace BazaarPlusPlus.Game.CombatLog;

internal static class CombatLogViewport
{
    private const float EstimatedRowHeight = 38f;
    private const int ViewportOverscanRows = 8;

    internal static (int StartIndex, int EndIndex, float TopSpacerHeight, float BottomSpacerHeight) CalculateVisibleRowRange(
        int totalRowCount,
        float scrollY,
        float viewportHeight
    )
    {
        if (totalRowCount <= 0 || viewportHeight <= 0f)
            return (0, 0, 0f, 0f);

        var rowsPerViewport = Math.Max(
            1,
            (int)Math.Ceiling(viewportHeight / EstimatedRowHeight)
        );
        var requestedStartIndex = Math.Max(
            (int)Math.Floor(Math.Max(scrollY, 0f) / EstimatedRowHeight) - ViewportOverscanRows,
            0
        );
        var windowRowCount = rowsPerViewport + (ViewportOverscanRows * 2);
        var startIndex = Math.Min(
            requestedStartIndex,
            Math.Max(totalRowCount - windowRowCount, 0)
        );
        var endIndex = Math.Min(
            startIndex + windowRowCount,
            totalRowCount
        );

        return (
            startIndex,
            endIndex,
            startIndex * EstimatedRowHeight,
            Math.Max(totalRowCount - endIndex, 0) * EstimatedRowHeight
        );
    }
}
