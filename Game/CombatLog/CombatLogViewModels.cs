#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogDisplayRowViewModel
{
    public CombatLogDisplayRowViewModel(
        string primaryText,
        string? secondaryText,
        bool emphasize,
        CombatLogRowVisualState visualState = CombatLogRowVisualState.Played
    )
    {
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        Emphasize = emphasize;
        VisualState = visualState;
    }

    public string PrimaryText { get; }

    public string? SecondaryText { get; }

    public bool Emphasize { get; }

    public CombatLogRowVisualState VisualState { get; }
}

internal sealed class CombatLogFrameGroupViewModel
{
    public CombatLogFrameGroupViewModel(
        int frameIndex,
        TimeSpan logicalTime,
        string summaryText,
        CombatLogRowVisualState visualState,
        bool isExpanded,
        int visibleRowCount,
        int totalRowCount,
        IReadOnlyList<CombatLogDisplayRowViewModel> rows
    )
    {
        FrameIndex = frameIndex;
        LogicalTime = logicalTime;
        SummaryText = summaryText;
        VisualState = visualState;
        IsExpanded = isExpanded;
        VisibleRowCount = visibleRowCount;
        TotalRowCount = totalRowCount;
        Rows = rows;
    }

    public int FrameIndex { get; }

    public TimeSpan LogicalTime { get; }

    public string SummaryText { get; }

    public CombatLogRowVisualState VisualState { get; }

    public bool IsExpanded { get; }

    public int VisibleRowCount { get; }

    public int TotalRowCount { get; }

    public IReadOnlyList<CombatLogDisplayRowViewModel> Rows { get; }
}
