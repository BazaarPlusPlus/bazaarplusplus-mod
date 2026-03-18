#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogPanelState
{
    public int ProcessedFrameCount { get; private set; }

    public int CurrentFrameIndex { get; private set; } = -1;

    public bool ShowActions { get; private set; } = true;

    public bool ShowStateChanges { get; private set; } = true;

    public bool ShowRewards { get; private set; } = true;

    public bool ShowSystem { get; private set; } = true;

    public bool ShowUnknown { get; private set; } = true;

    public void Refresh(int processedFrameCount)
    {
        ProcessedFrameCount = processedFrameCount < 0 ? 0 : processedFrameCount;
        CurrentFrameIndex = CombatLogPlaybackState.GetLastProcessedFrameIndex(ProcessedFrameCount);
    }

    public void ToggleActions()
    {
        ShowActions = !ShowActions;
    }

    public void ToggleStateChanges()
    {
        ShowStateChanges = !ShowStateChanges;
    }

    public void ToggleRewards()
    {
        ShowRewards = !ShowRewards;
    }

    public void ToggleSystem()
    {
        ShowSystem = !ShowSystem;
    }

    public void ToggleUnknown()
    {
        ShowUnknown = !ShowUnknown;
    }

    public IReadOnlyList<CombatLogVisibleRow> BuildVisibleRows(CombatLogTimeline? timeline)
    {
        if (timeline?.Rows == null || timeline.Rows.Count == 0)
            return System.Array.Empty<CombatLogVisibleRow>();

        var rows = new List<CombatLogVisibleRow>(timeline.Rows.Count);
        foreach (var row in timeline.Rows)
        {
            var visualState = CombatLogPlaybackState.GetVisualState(
                row.FrameIndex,
                ProcessedFrameCount,
                timeline.PlaybackPass
            );
            if (visualState == CombatLogRowVisualState.FutureHidden)
                continue;
            if (!ShouldInclude(row.Category))
                continue;

            rows.Add(new CombatLogVisibleRow(row, visualState));
        }

        return rows;
    }

    private bool ShouldInclude(CombatLogRowCategory category)
    {
        return category switch
        {
            CombatLogRowCategory.Event or CombatLogRowCategory.Death => ShowActions,
            CombatLogRowCategory.Health
            or CombatLogRowCategory.Attribute
            or CombatLogRowCategory.CardAttribute => ShowStateChanges,
            CombatLogRowCategory.Reward => ShowRewards,
            CombatLogRowCategory.System => ShowSystem,
            CombatLogRowCategory.Unknown => ShowUnknown,
            _ => true,
        };
    }
}
