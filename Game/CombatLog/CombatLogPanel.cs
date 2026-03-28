#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogPanel
{
    private const float WindowWidth = 420f;
    private const float MinWindowWidth = 320f;
    private const float Spacing = 10f;
    private const float ScreenMargin = 10f;
    private const float ScrollViewportChromeHeight = 190f;

    private static readonly GUIStyle HeaderStyle = new GUIStyle();
    private static readonly GUIStyle StatusStyle = new GUIStyle();
    private static readonly GUIStyle FrameHeaderStyle = new GUIStyle();
    private static readonly GUIStyle CurrentFrameHeaderStyle = new GUIStyle();
    private static readonly GUIStyle DimmedFrameHeaderStyle = new GUIStyle();
    private static readonly GUIStyle FrameSummaryStyle = new GUIStyle();
    private static readonly GUIStyle CurrentFrameSummaryStyle = new GUIStyle();
    private static readonly GUIStyle DimmedFrameSummaryStyle = new GUIStyle();
    private static readonly GUIStyle RowStyle = new GUIStyle();
    private static readonly GUIStyle CurrentRowStyle = new GUIStyle();
    private static readonly GUIStyle DimmedRowStyle = new GUIStyle();
    private static readonly GUIStyle MutedStyle = new GUIStyle();
    private static readonly GUIStyle SecondaryRowStyle = new GUIStyle();
    private static readonly GUIStyle SecondaryCurrentRowStyle = new GUIStyle();
    private static readonly GUIStyle SecondaryDimmedRowStyle = new GUIStyle();
    private static readonly GUIStyle FilterButtonStyle = new GUIStyle();
    private static bool _stylesInitialized;

    private readonly CombatLogPanelState _state = new CombatLogPanelState();
    private Vector2 _scroll;

    public bool IsVisible { get; private set; }

    public void ToggleVisibility()
    {
        IsVisible = !IsVisible;
    }

    public void SetVisibility(bool visible)
    {
        IsVisible = visible;
    }

    public void Draw(Rect debugPanelRect, CombatLogTimeline? timeline, int processedFrameCount)
    {
        if (!IsVisible)
            return;

        var windowRect = GetWindowRect(debugPanelRect);
        if (windowRect.width <= 0f)
            return;

        InitStyles();
        _state.Refresh(processedFrameCount);

        GUI.Box(windowRect, string.Empty);
        GUILayout.BeginArea(
            new Rect(
                windowRect.x + 8f,
                windowRect.y + 8f,
                windowRect.width - 16f,
                windowRect.height - 16f
            )
        );
        DrawWindowContents(timeline, windowRect.height - 16f);
        GUILayout.EndArea();
    }

    public void DrawStandalone(CombatLogTimeline? timeline, int processedFrameCount)
    {
        if (!IsVisible)
            return;

        var windowRect = GetStandaloneWindowRect();
        if (windowRect.width <= 0f || windowRect.height <= 0f)
            return;

        InitStyles();
        _state.Refresh(processedFrameCount);

        GUI.Box(windowRect, string.Empty);
        GUILayout.BeginArea(
            new Rect(
                windowRect.x + 8f,
                windowRect.y + 8f,
                windowRect.width - 16f,
                windowRect.height - 16f
            )
        );
        DrawWindowContents(timeline, windowRect.height - 16f);
        GUILayout.EndArea();
    }

    private Rect GetWindowRect(Rect debugPanelRect)
    {
        var remainingWidth = Screen.width - (debugPanelRect.xMax + Spacing) - 10f;
        if (remainingWidth < MinWindowWidth)
            return Rect.zero;

        var width = Mathf.Min(WindowWidth, remainingWidth);
        return new Rect(
            debugPanelRect.xMax + Spacing,
            debugPanelRect.y,
            width,
            debugPanelRect.height
        );
    }

    private Rect GetStandaloneWindowRect()
    {
        var availableWidth = Screen.width - (ScreenMargin * 2f);
        if (availableWidth < MinWindowWidth)
            return Rect.zero;

        var width = Mathf.Min(WindowWidth, availableWidth);
        var height = Mathf.Max(Screen.height - (ScreenMargin * 2f), 0f);
        return new Rect(Screen.width - width - ScreenMargin, ScreenMargin, width, height);
    }

    private void DrawWindowContents(CombatLogTimeline? timeline, float contentHeight)
    {
        GUILayout.Label("Combat Log", HeaderStyle);
        GUILayout.Label(
            $"Pass: {timeline?.PlaybackPass.ToString() ?? "-"}  Current: {_state.CurrentFrameIndex}",
            StatusStyle
        );
        DrawModeButtons();
        DrawVerbosityButtons();
        DrawFilters();
        GUILayout.Space(8f);

        if (timeline == null)
        {
            GUILayout.Label("No combat timeline loaded.", MutedStyle);
            return;
        }

        var viewportHeight = Mathf.Max(contentHeight - ScrollViewportChromeHeight, 80f);
        var visibleGroups = _state.BuildVisibleFrameGroups(timeline);
        if (visibleGroups.Count == 0)
        {
            GUILayout.Label("No visible frame groups yet.", MutedStyle);
            return;
        }

        // Keep the old row-range helper referenced while the viewport transitions to group-aware rendering.
        var _ = CombatLogViewport.CalculateVisibleRowRange(
            Mathf.Max(visibleGroups.Sum(group => 1 + group.VisibleRowCount), 1),
            _scroll.y,
            viewportHeight
        );
        var visibleRange = CombatLogViewport.CalculateVisibleGroupRange(
            visibleGroups,
            _scroll.y,
            viewportHeight
        );
        _scroll = GUILayout.BeginScrollView(
            _scroll,
            GUILayout.Height(viewportHeight),
            GUILayout.ExpandHeight(true)
        );
        if (visibleRange.TopSpacerHeight > 0f)
            GUILayout.Space(visibleRange.TopSpacerHeight);
        DrawFrameGroups(visibleGroups, visibleRange.StartIndex, visibleRange.EndIndex);
        if (visibleRange.BottomSpacerHeight > 0f)
            GUILayout.Space(visibleRange.BottomSpacerHeight);
        GUILayout.EndScrollView();
    }

    private void DrawModeButtons()
    {
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        DrawOptionButton(
            "Release",
            _state.DisplayOptions.Mode == CombatLogDisplayMode.Release,
            () => _state.SetDisplayMode(CombatLogDisplayMode.Release)
        );
        DrawOptionButton(
            "Debug",
            _state.DisplayOptions.Mode == CombatLogDisplayMode.Debug,
            () => _state.SetDisplayMode(CombatLogDisplayMode.Debug)
        );
        GUILayout.EndHorizontal();
    }

    private void DrawVerbosityButtons()
    {
        GUILayout.BeginHorizontal();
        DrawOptionButton(
            "Compact",
            _state.DisplayOptions.Verbosity == CombatLogVerbosity.Compact,
            () => _state.SetVerbosity(CombatLogVerbosity.Compact)
        );
        DrawOptionButton(
            "Standard",
            _state.DisplayOptions.Verbosity == CombatLogVerbosity.Standard,
            () => _state.SetVerbosity(CombatLogVerbosity.Standard)
        );
        DrawOptionButton(
            "Verbose",
            _state.DisplayOptions.Verbosity == CombatLogVerbosity.Verbose,
            () => _state.SetVerbosity(CombatLogVerbosity.Verbose)
        );
        GUILayout.EndHorizontal();
    }

    private void DrawFilters()
    {
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        DrawFilterButton("Actions", _state.ShowActions, _state.ToggleActions);
        DrawFilterButton("Combatants", _state.ShowCombatants, _state.ToggleCombatants);
        DrawFilterButton("Cards", _state.ShowCards, _state.ToggleCards);
        DrawFilterButton("Rewards", _state.ShowRewards, _state.ToggleRewards);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        DrawFilterButton("System", _state.ShowSystem, _state.ToggleSystem);
        DrawFilterButton("Unknown", _state.ShowUnknown, _state.ToggleUnknown);
        GUILayout.EndHorizontal();
    }

    private static void DrawFilterButton(string label, bool enabled, System.Action onClick)
    {
        var previousColor = GUI.color;
        GUI.color = enabled ? new Color(0.85f, 1f, 0.85f) : new Color(0.75f, 0.75f, 0.75f);
        if (GUILayout.Button(label, FilterButtonStyle))
            onClick();
        GUI.color = previousColor;
    }

    private static void DrawOptionButton(string label, bool enabled, System.Action onClick)
    {
        DrawFilterButton(label, enabled, onClick);
    }

    private void DrawFrameGroups(
        IReadOnlyList<CombatLogFrameGroupViewModel> groups,
        int startIndex,
        int endIndex
    )
    {
        for (var index = startIndex; index < endIndex; index++)
        {
            var group = groups[index];
            DrawFrameHeader(group);
            if (group.IsExpanded)
                DrawFrameRows(group.Rows);
            GUILayout.Space(4f);
        }
    }

    private void DrawFrameHeader(CombatLogFrameGroupViewModel group)
    {
        var toggleLabel = group.IsExpanded ? "[-]" : "[+]";
        if (
            GUILayout.Button(
                $"{toggleLabel} [{group.FrameIndex:000}] {group.SummaryText}",
                GetFrameHeaderStyle(group.VisualState)
            )
        )
        {
            _state.ToggleFrameExpanded(group.FrameIndex);
        }

        GUILayout.Label(
            $"{group.LogicalTime:mm\\:ss\\.ff}  rows: {group.VisibleRowCount}/{group.TotalRowCount}",
            GetFrameSummaryStyle(group.VisualState)
        );
    }

    private static void DrawFrameRows(IReadOnlyList<CombatLogDisplayRowViewModel> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var primaryStyle = GetPrimaryRowStyle(row);
            GUILayout.Label($"    {row.PrimaryText}", primaryStyle);
            if (!string.IsNullOrWhiteSpace(row.SecondaryText))
                GUILayout.Label($"      {row.SecondaryText}", GetSecondaryRowStyle(row.VisualState));
            GUILayout.Space(2f);
        }
    }

    private static GUIStyle GetFrameHeaderStyle(CombatLogRowVisualState visualState)
    {
        return visualState switch
        {
            CombatLogRowVisualState.Current => CurrentFrameHeaderStyle,
            CombatLogRowVisualState.FutureDimmed => DimmedFrameHeaderStyle,
            _ => FrameHeaderStyle,
        };
    }

    private static GUIStyle GetFrameSummaryStyle(CombatLogRowVisualState visualState)
    {
        return visualState switch
        {
            CombatLogRowVisualState.Current => CurrentFrameSummaryStyle,
            CombatLogRowVisualState.FutureDimmed => DimmedFrameSummaryStyle,
            _ => FrameSummaryStyle,
        };
    }

    private static GUIStyle GetPrimaryRowStyle(CombatLogDisplayRowViewModel row)
    {
        if (row.VisualState == CombatLogRowVisualState.FutureDimmed)
            return DimmedRowStyle;

        if (row.VisualState == CombatLogRowVisualState.Current || row.Emphasize)
            return CurrentRowStyle;

        return RowStyle;
    }

    private static GUIStyle GetSecondaryRowStyle(CombatLogRowVisualState visualState)
    {
        return visualState switch
        {
            CombatLogRowVisualState.Current => SecondaryCurrentRowStyle,
            CombatLogRowVisualState.FutureDimmed => SecondaryDimmedRowStyle,
            _ => SecondaryRowStyle,
        };
    }

    private static void InitStyles()
    {
        if (_stylesInitialized)
            return;

        HeaderStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
        HeaderStyle.fontStyle = FontStyle.Bold;
        HeaderStyle.fontSize = 18;

        StatusStyle.normal.textColor = new Color(0.82f, 0.9f, 1f);
        StatusStyle.fontSize = 13;

        FrameHeaderStyle.normal.textColor = new Color(0.9f, 0.92f, 1f);
        FrameHeaderStyle.fontSize = 13;
        FrameHeaderStyle.fontStyle = FontStyle.Bold;
        FrameHeaderStyle.alignment = TextAnchor.MiddleLeft;
        FrameHeaderStyle.padding = new RectOffset(8, 8, 6, 6);
        FrameHeaderStyle.margin = new RectOffset(0, 0, 2, 2);

        CurrentFrameHeaderStyle.normal.textColor = new Color(1f, 0.95f, 0.6f);
        CurrentFrameHeaderStyle.fontSize = 13;
        CurrentFrameHeaderStyle.fontStyle = FontStyle.Bold;
        CurrentFrameHeaderStyle.alignment = TextAnchor.MiddleLeft;
        CurrentFrameHeaderStyle.padding = new RectOffset(8, 8, 6, 6);
        CurrentFrameHeaderStyle.margin = new RectOffset(0, 0, 2, 2);

        DimmedFrameHeaderStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
        DimmedFrameHeaderStyle.fontSize = 13;
        DimmedFrameHeaderStyle.fontStyle = FontStyle.Bold;
        DimmedFrameHeaderStyle.alignment = TextAnchor.MiddleLeft;
        DimmedFrameHeaderStyle.padding = new RectOffset(8, 8, 6, 6);
        DimmedFrameHeaderStyle.margin = new RectOffset(0, 0, 2, 2);

        FrameSummaryStyle.normal.textColor = new Color(0.7f, 0.76f, 0.82f);
        FrameSummaryStyle.fontSize = 11;
        FrameSummaryStyle.wordWrap = true;

        CurrentFrameSummaryStyle.normal.textColor = new Color(0.93f, 0.86f, 0.60f);
        CurrentFrameSummaryStyle.fontSize = 11;
        CurrentFrameSummaryStyle.wordWrap = true;

        DimmedFrameSummaryStyle.normal.textColor = new Color(0.55f, 0.55f, 0.55f);
        DimmedFrameSummaryStyle.fontSize = 11;
        DimmedFrameSummaryStyle.wordWrap = true;

        RowStyle.normal.textColor = Color.white;
        RowStyle.fontSize = 13;
        RowStyle.wordWrap = true;

        CurrentRowStyle.normal.textColor = new Color(1f, 0.95f, 0.6f);
        CurrentRowStyle.fontSize = 13;
        CurrentRowStyle.fontStyle = FontStyle.Bold;
        CurrentRowStyle.wordWrap = true;

        DimmedRowStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
        DimmedRowStyle.fontSize = 13;
        DimmedRowStyle.wordWrap = true;

        MutedStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        MutedStyle.fontSize = 12;
        MutedStyle.wordWrap = true;

        SecondaryRowStyle.normal.textColor = new Color(0.72f, 0.76f, 0.82f);
        SecondaryRowStyle.fontSize = 11;
        SecondaryRowStyle.wordWrap = true;

        SecondaryCurrentRowStyle.normal.textColor = new Color(0.93f, 0.86f, 0.60f);
        SecondaryCurrentRowStyle.fontSize = 11;
        SecondaryCurrentRowStyle.wordWrap = true;

        SecondaryDimmedRowStyle.normal.textColor = new Color(0.55f, 0.55f, 0.55f);
        SecondaryDimmedRowStyle.fontSize = 11;
        SecondaryDimmedRowStyle.wordWrap = true;

        var buttonStyle = GUI.skin.button;
        FilterButtonStyle.normal = buttonStyle.normal;
        FilterButtonStyle.hover = buttonStyle.hover;
        FilterButtonStyle.active = buttonStyle.active;
        FilterButtonStyle.focused = buttonStyle.focused;
        FilterButtonStyle.onNormal = buttonStyle.onNormal;
        FilterButtonStyle.onHover = buttonStyle.onHover;
        FilterButtonStyle.onActive = buttonStyle.onActive;
        FilterButtonStyle.onFocused = buttonStyle.onFocused;
        FilterButtonStyle.border = buttonStyle.border;
        FilterButtonStyle.margin = new RectOffset(2, 2, 2, 2);
        FilterButtonStyle.overflow = buttonStyle.overflow;
        FilterButtonStyle.padding = new RectOffset(6, 6, 4, 4);
        FilterButtonStyle.fontSize = 12;

        _stylesInitialized = true;
    }
}
