#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogPanelState
{
    private readonly CombatLogViewModelBuilder _viewModelBuilder = new CombatLogViewModelBuilder();
    private readonly HashSet<int> _expandedFrames = new HashSet<int>();
    private readonly HashSet<int> _collapsedFrames = new HashSet<int>();

    private CombatLogTimeline? _cachedTimeline;
    private IReadOnlyList<CombatLogVisibleRow> _cachedVisibleRows =
        System.Array.Empty<CombatLogVisibleRow>();
    private CombatLogTimeline? _cachedFrameGroupTimeline;
    private IReadOnlyList<CombatLogFrameGroupViewModel> _cachedFrameGroups =
        System.Array.Empty<CombatLogFrameGroupViewModel>();
    private bool _visibleRowsDirty = true;
    private bool _frameGroupsDirty = true;

    public int ProcessedFrameCount { get; private set; }

    public int CurrentFrameIndex { get; private set; } = -1;

    public CombatLogDisplayOptions DisplayOptions { get; private set; } =
        BppBuild.IsDebug
            ? CombatLogDisplayOptions.DebugVerbose
            : CombatLogDisplayOptions.ReleaseStandard;

    public bool ShowActions { get; private set; }

    public bool ShowCombatants { get; private set; }

    public bool ShowCards { get; private set; }

    public bool ShowRewards { get; private set; }

    public bool ShowSystem { get; private set; }

    public bool ShowUnknown { get; private set; }

    public CombatLogPanelState()
    {
        SyncFiltersFromDisplayOptions();
    }

    public void Refresh(int processedFrameCount)
    {
        var normalizedFrameCount = processedFrameCount < 0 ? 0 : processedFrameCount;
        if (ProcessedFrameCount != normalizedFrameCount)
        {
            _visibleRowsDirty = true;
            _frameGroupsDirty = true;
        }

        ProcessedFrameCount = normalizedFrameCount;
        CurrentFrameIndex = CombatLogPlaybackState.GetLastProcessedFrameIndex(ProcessedFrameCount);
    }

    public void ToggleActions()
    {
        SetDisplayOptions(
            new CombatLogDisplayOptions(
                DisplayOptions.Mode,
                DisplayOptions.Verbosity,
                !ShowActions,
                ShowCombatants,
                ShowCards,
                ShowRewards,
                ShowSystem,
                ShowUnknown,
                DisplayOptions.ShowEmptyFrames
            )
        );
    }

    public void ToggleCombatants()
    {
        SetDisplayOptions(
            new CombatLogDisplayOptions(
                DisplayOptions.Mode,
                DisplayOptions.Verbosity,
                ShowActions,
                !ShowCombatants,
                ShowCards,
                ShowRewards,
                ShowSystem,
                ShowUnknown,
                DisplayOptions.ShowEmptyFrames
            )
        );
    }

    public void ToggleCards()
    {
        SetDisplayOptions(
            new CombatLogDisplayOptions(
                DisplayOptions.Mode,
                DisplayOptions.Verbosity,
                ShowActions,
                ShowCombatants,
                !ShowCards,
                ShowRewards,
                ShowSystem,
                ShowUnknown,
                DisplayOptions.ShowEmptyFrames
            )
        );
    }

    public void ToggleRewards()
    {
        SetDisplayOptions(
            new CombatLogDisplayOptions(
                DisplayOptions.Mode,
                DisplayOptions.Verbosity,
                ShowActions,
                ShowCombatants,
                ShowCards,
                !ShowRewards,
                ShowSystem,
                ShowUnknown,
                DisplayOptions.ShowEmptyFrames
            )
        );
    }

    public void ToggleSystem()
    {
        SetDisplayOptions(
            new CombatLogDisplayOptions(
                DisplayOptions.Mode,
                DisplayOptions.Verbosity,
                ShowActions,
                ShowCombatants,
                ShowCards,
                ShowRewards,
                !ShowSystem,
                ShowUnknown,
                DisplayOptions.ShowEmptyFrames
            )
        );
    }

    public void ToggleUnknown()
    {
        SetDisplayOptions(
            new CombatLogDisplayOptions(
                DisplayOptions.Mode,
                DisplayOptions.Verbosity,
                ShowActions,
                ShowCombatants,
                ShowCards,
                ShowRewards,
                ShowSystem,
                !ShowUnknown,
                DisplayOptions.ShowEmptyFrames
            )
        );
    }

    public void SetDisplayOptions(CombatLogDisplayOptions options)
    {
        DisplayOptions = options;
        SyncFiltersFromDisplayOptions();
        _visibleRowsDirty = true;
        _frameGroupsDirty = true;
    }

    public void SetDisplayMode(CombatLogDisplayMode mode)
    {
        if (DisplayOptions.Mode == mode)
            return;

        SetDisplayOptions(
            new CombatLogDisplayOptions(
                mode,
                DisplayOptions.Verbosity,
                DisplayOptions.ShowEvents,
                DisplayOptions.ShowCombatants,
                DisplayOptions.ShowCards,
                DisplayOptions.ShowRewards,
                DisplayOptions.ShowSystem,
                DisplayOptions.ShowUnknown,
                DisplayOptions.ShowEmptyFrames
            )
        );
    }

    public void SetVerbosity(CombatLogVerbosity verbosity)
    {
        if (DisplayOptions.Verbosity == verbosity)
            return;

        SetDisplayOptions(
            new CombatLogDisplayOptions(
                DisplayOptions.Mode,
                verbosity,
                DisplayOptions.ShowEvents,
                DisplayOptions.ShowCombatants,
                DisplayOptions.ShowCards,
                DisplayOptions.ShowRewards,
                DisplayOptions.ShowSystem,
                DisplayOptions.ShowUnknown,
                DisplayOptions.ShowEmptyFrames
            )
        );
    }

    public void ToggleFrameExpanded(int frameIndex)
    {
        if (IsFrameExpanded(frameIndex))
        {
            _expandedFrames.Remove(frameIndex);
            _collapsedFrames.Add(frameIndex);
        }
        else
        {
            _collapsedFrames.Remove(frameIndex);
            _expandedFrames.Add(frameIndex);
        }

        _frameGroupsDirty = true;
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

    public IReadOnlyList<CombatLogFrameGroupViewModel> BuildVisibleFrameGroups(
        CombatLogTimeline? timeline
    )
    {
        if (timeline?.Frames == null || timeline.Frames.Count == 0)
            return System.Array.Empty<CombatLogFrameGroupViewModel>();

        if (!_frameGroupsDirty && ReferenceEquals(_cachedFrameGroupTimeline, timeline))
            return _cachedFrameGroups;

        var visibleTimeline = BuildTimelineForVisibleFrames(timeline);
        var expandedFrames = GetExpandedFramesForCurrentOptions();
        _cachedFrameGroupTimeline = timeline;
        _cachedFrameGroups = _viewModelBuilder.Build(
            visibleTimeline,
            DisplayOptions,
            expandedFrames,
            ProcessedFrameCount
        );
        _frameGroupsDirty = false;
        return _cachedFrameGroups;
    }

    private CombatLogTimeline BuildTimelineForVisibleFrames(CombatLogTimeline timeline)
    {
        var visibleFrames = new List<CombatLogFrame>(timeline.Frames.Count);
        foreach (var frame in timeline.Frames)
        {
            var visualState = CombatLogPlaybackState.GetVisualState(
                frame.FrameIndex,
                ProcessedFrameCount,
                timeline.PlaybackPass
            );
            if (visualState == CombatLogRowVisualState.FutureHidden)
                continue;

            visibleFrames.Add(frame);
        }

        return new CombatLogTimeline(timeline.PlaybackPass, visibleFrames, timeline.Rows);
    }

    private IReadOnlyCollection<int> GetExpandedFramesForCurrentOptions()
    {
        var expandedFrames = new HashSet<int>(_expandedFrames);

        if (
            DisplayOptions.Verbosity == CombatLogVerbosity.Verbose
            && CombatLogPlaybackState.TryGetCurrentFrameIndex(ProcessedFrameCount, out var currentFrame)
            && !_collapsedFrames.Contains(currentFrame)
        )
        {
            expandedFrames.Add(currentFrame);
        }

        return expandedFrames.Count == 0 ? System.Array.Empty<int>() : expandedFrames;
    }

    private bool IsFrameExpanded(int frameIndex)
    {
        return _expandedFrames.Contains(frameIndex)
            || (
                DisplayOptions.Verbosity == CombatLogVerbosity.Verbose
                && CurrentFrameIndex == frameIndex
                && !_collapsedFrames.Contains(frameIndex)
            );
    }

    private bool ShouldInclude(CombatLogRowCategory category)
    {
        return CombatLogFormatter.ShouldInclude(DisplayOptions, category);
    }

    private void SyncFiltersFromDisplayOptions()
    {
        ShowActions = DisplayOptions.ShowEvents;
        ShowCombatants = DisplayOptions.ShowCombatants;
        ShowCards = DisplayOptions.ShowCards;
        ShowRewards = DisplayOptions.ShowRewards;
        ShowSystem = DisplayOptions.ShowSystem;
        ShowUnknown = DisplayOptions.ShowUnknown;
    }
}
