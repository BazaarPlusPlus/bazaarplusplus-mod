#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogPanelState
{
    private CombatLogTimeline? _cachedTimeline;
    private IReadOnlyList<CombatLogVisibleRow> _cachedVisibleRows =
        System.Array.Empty<CombatLogVisibleRow>();
    private bool _visibleRowsDirty = true;

    public int ProcessedFrameCount { get; private set; }

    public int CurrentFrameIndex { get; private set; } = -1;

    public bool ShowActions { get; private set; } = true;

    public bool ShowCombatants { get; private set; } = true;

    public bool ShowCards { get; private set; } = true;

    public bool ShowRewards { get; private set; } = true;

    public bool ShowSystem { get; private set; } = true;

    public bool ShowUnknown { get; private set; } = true;

    public void Refresh(int processedFrameCount)
    {
        var normalizedFrameCount = processedFrameCount < 0 ? 0 : processedFrameCount;
        if (ProcessedFrameCount != normalizedFrameCount)
            _visibleRowsDirty = true;

        ProcessedFrameCount = normalizedFrameCount;
        CurrentFrameIndex = CombatLogPlaybackState.GetLastProcessedFrameIndex(ProcessedFrameCount);
    }

    public void ToggleActions()
    {
        ShowActions = !ShowActions;
        _visibleRowsDirty = true;
    }

    public void ToggleCombatants()
    {
        ShowCombatants = !ShowCombatants;
        _visibleRowsDirty = true;
    }

    public void ToggleCards()
    {
        ShowCards = !ShowCards;
        _visibleRowsDirty = true;
    }

    public void ToggleRewards()
    {
        ShowRewards = !ShowRewards;
        _visibleRowsDirty = true;
    }

    public void ToggleSystem()
    {
        ShowSystem = !ShowSystem;
        _visibleRowsDirty = true;
    }

    public void ToggleUnknown()
    {
        ShowUnknown = !ShowUnknown;
        _visibleRowsDirty = true;
    }

    public IReadOnlyList<CombatLogVisibleRow> BuildVisibleRows(CombatLogTimeline? timeline)
    {
        if (timeline?.Rows == null || timeline.Rows.Count == 0)
            return System.Array.Empty<CombatLogVisibleRow>();

        if (!_visibleRowsDirty && ReferenceEquals(_cachedTimeline, timeline))
            return _cachedVisibleRows;

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

        _cachedTimeline = timeline;
        _cachedVisibleRows = rows;
        _visibleRowsDirty = false;
        return _cachedVisibleRows;
    }

    private bool ShouldInclude(CombatLogRowCategory category)
    {
        return category switch
        {
            CombatLogRowCategory.Event or CombatLogRowCategory.Death => ShowActions,
            CombatLogRowCategory.Health or CombatLogRowCategory.Attribute => ShowCombatants,
            CombatLogRowCategory.CardAttribute => ShowCards,
            CombatLogRowCategory.Reward => ShowRewards,
            CombatLogRowCategory.System => ShowSystem,
            CombatLogRowCategory.Unknown => ShowUnknown,
            _ => true,
        };
    }
}
