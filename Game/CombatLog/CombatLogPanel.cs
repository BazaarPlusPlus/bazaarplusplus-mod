#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogPanel
{
    private const float WindowWidth = 420f;
    private const float MinWindowWidth = 320f;
    private const float Spacing = 10f;

    private static readonly GUIStyle HeaderStyle = new GUIStyle();
    private static readonly GUIStyle StatusStyle = new GUIStyle();
    private static readonly GUIStyle RowStyle = new GUIStyle();
    private static readonly GUIStyle CurrentRowStyle = new GUIStyle();
    private static readonly GUIStyle DimmedRowStyle = new GUIStyle();
    private static readonly GUIStyle MutedStyle = new GUIStyle();
    private static readonly GUIStyle FilterButtonStyle = new GUIStyle();
    private static bool _stylesInitialized;

    private readonly CombatLogPanelState _state = new CombatLogPanelState();
    private Vector2 _scroll;

    public bool IsVisible { get; private set; } = true;

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
            new Rect(windowRect.x + 8f, windowRect.y + 8f, windowRect.width - 16f, windowRect.height - 16f)
        );
        DrawWindowContents(timeline);
        GUILayout.EndArea();
    }

    private Rect GetWindowRect(Rect debugPanelRect)
    {
        var remainingWidth = Screen.width - (debugPanelRect.xMax + Spacing) - 10f;
        if (remainingWidth < MinWindowWidth)
            return Rect.zero;

        var width = Mathf.Min(WindowWidth, remainingWidth);
        return new Rect(debugPanelRect.xMax + Spacing, debugPanelRect.y, width, debugPanelRect.height);
    }

    private void DrawWindowContents(CombatLogTimeline? timeline)
    {
        GUILayout.Label("Combat Log", HeaderStyle);
        GUILayout.Label(
            $"Pass: {timeline?.PlaybackPass.ToString() ?? "-"}  Current: {_state.CurrentFrameIndex}",
            StatusStyle
        );
        DrawFilters();
        GUILayout.Space(8f);

        if (timeline == null)
        {
            GUILayout.Label("No combat timeline loaded.", MutedStyle);
            return;
        }

        var visibleRows = _state.BuildVisibleRows(timeline);
        if (visibleRows.Count == 0)
        {
            GUILayout.Label("No visible log rows yet.", MutedStyle);
            return;
        }

        _scroll = GUILayout.BeginScrollView(_scroll);
        DrawRows(visibleRows);
        GUILayout.EndScrollView();
    }

    private void DrawFilters()
    {
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        DrawFilterButton("Actions", _state.ShowActions, _state.ToggleActions);
        DrawFilterButton("State", _state.ShowStateChanges, _state.ToggleStateChanges);
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

    private static void DrawRows(IReadOnlyList<CombatLogVisibleRow> rows)
    {
        foreach (var row in rows)
        {
            var style = row.VisualState switch
            {
                CombatLogRowVisualState.Current => CurrentRowStyle,
                CombatLogRowVisualState.FutureDimmed => DimmedRowStyle,
                _ => RowStyle,
            };
            GUILayout.Label(
                $"[{row.Row.FrameIndex:000}] {row.Row.Text}",
                style
            );
            GUILayout.Space(2f);
        }
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
