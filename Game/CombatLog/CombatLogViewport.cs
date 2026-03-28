#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CombatLog;

internal static class CombatLogViewport
{
    private const float EstimatedRowHeight = 38f;
    private const int ViewportOverscanRows = 8;
    private const float EstimatedFrameHeaderHeight = 30f;
    private const float EstimatedFrameSummaryHeight = 18f;
    private const float EstimatedPrimaryRowHeight = 20f;
    private const float EstimatedSecondaryRowHeight = 16f;
    private const float EstimatedRowSpacingHeight = 2f;
    private const float EstimatedGroupSpacingHeight = 4f;

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

    internal static (
        int StartIndex,
        int EndIndex,
        float TopSpacerHeight,
        float BottomSpacerHeight
    ) CalculateVisibleGroupRange(
        IReadOnlyList<CombatLogFrameGroupViewModel> groups,
        float scrollY,
        float viewportHeight
    )
    {
        if (groups.Count == 0 || viewportHeight <= 0f)
            return (0, 0, 0f, 0f);

        var overscanHeight = EstimatedRowHeight * ViewportOverscanRows;
        var requestedStartY = Math.Max(scrollY - overscanHeight, 0f);
        var requestedEndY = Math.Max(scrollY, 0f) + viewportHeight + overscanHeight;

        var currentY = 0f;
        var startIndex = 0;
        while (startIndex < groups.Count)
        {
            var groupHeight = EstimateGroupHeight(groups[startIndex]);
            if (currentY + groupHeight > requestedStartY)
                break;

            currentY += groupHeight;
            startIndex++;
        }

        var topSpacerHeight = currentY;
        var endIndex = startIndex;
        while (endIndex < groups.Count && currentY < requestedEndY)
        {
            currentY += EstimateGroupHeight(groups[endIndex]);
            endIndex++;
        }

        var totalHeight = 0f;
        foreach (var group in groups)
            totalHeight += EstimateGroupHeight(group);

        return (
            startIndex,
            endIndex,
            topSpacerHeight,
            Math.Max(totalHeight - currentY, 0f)
        );
    }

    internal static float EstimateGroupHeight(CombatLogFrameGroupViewModel group)
    {
        var height =
            EstimatedFrameHeaderHeight + EstimatedFrameSummaryHeight + EstimatedGroupSpacingHeight;
        if (!group.IsExpanded)
            return height;

        foreach (var row in group.Rows)
        {
            height += EstimatedPrimaryRowHeight + EstimatedRowSpacingHeight;
            if (!string.IsNullOrWhiteSpace(row.SecondaryText))
                height += EstimatedSecondaryRowHeight;
        }

        return height;
    }
}
