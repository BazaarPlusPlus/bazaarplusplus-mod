#nullable enable
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

// TEMP DEBUG TUNER — DELETE WHEN BAKED.
//
// Live-tune the BattleBoardPreview overlay's screen rect inside the history panel's preview
// container. Press F9 to toggle the IMGUI window; drag sliders until the preview lines up,
// then click "Log Current Values" to print the numbers to BepInEx console. Bake those
// numbers into ApplyPreviewContainerBounds (and BattleBoardPreview.SetClipMaskEnabled if you
// kept the mask off) and delete this file.
//
// Knob semantics — pixel offsets/deltas relative to the UI Toolkit preview container's
// screen rect (bottom-left origin), expressed in **1920×1080 reference-resolution pixels**.
// ApplyPreviewContainerBounds multiplies them by PanelScale (= Screen.width / 1920) so the
// visual effect stays consistent across resolutions. So a value of "X=4" means "4px on a
// 1080p screen" — at 4K it becomes 8 actual pixels, at 720p it becomes 2.67 actual pixels.
//   X       - position.x offset from container's left edge   (positive = move right)
//   Y       - position.y offset from container's bottom edge (positive = move up)
//   W       - delta applied to container's width             (negative = narrower clip)
//   H       - delta applied to container's height            (negative = shorter clip)
//   Scale   - multiplied onto the auto-fit cardScale (1.0 = exact fit to clip), resolution-
//             independent on its own
//   Mask    - toggles RectMask2D. OFF lets cards render past the clip rect into the rest of
//             the screen-space overlay Canvas — that's the "soft space" you couldn't reach.
// The board inside the clip is always centered on the clip rect, so the cards' visual
// center sits at the clip's geometric center.
internal sealed partial class HistoryPanel
{
    private float _debugX = 4f;
    private float _debugY = 10f;
    private float _debugWidthDelta = -8f;
    private float _debugHeightDelta = -20f;
    private float _debugCardScaleMul = 1f;
    private bool _debugClipMaskEnabled = true;
    private bool _debugClipMaskAppliedToPreview = true;
    private bool _debugTunerVisible;
    private Rect _debugTunerWindowRect = new(24f, 24f, 380f, 280f);

    private const float DebugTunerPositionRange = 1500f;
    private const float DebugTunerSizeDeltaMin = -1500f;
    private const float DebugTunerSizeDeltaMax = 1500f;
    private const float DebugTunerScaleMulMin = 0.10f;
    private const float DebugTunerScaleMulMax = 5.00f;

    private void OnGUI()
    {
        var ev = Event.current;
        if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.F9)
        {
            _debugTunerVisible = !_debugTunerVisible;
            ev.Use();
        }

        if (!_debugTunerVisible)
            return;
        if (!IsVisible || _battleBoardPreview == null || !_hasPreviewContainerBounds)
            return;

        SyncDebugTunerMaskState();

        _debugTunerWindowRect = GUI.Window(
            GetInstanceID() ^ 0x70E_71E,
            _debugTunerWindowRect,
            DrawDebugTunerWindow,
            "Preview Tuner (F9)"
        );
    }

    private void DrawDebugTunerWindow(int windowId)
    {
        GUILayout.BeginVertical();

        DrawDebugTunerSlider(
            "X",
            ref _debugX,
            -DebugTunerPositionRange,
            DebugTunerPositionRange
        );
        DrawDebugTunerSlider(
            "Y",
            ref _debugY,
            -DebugTunerPositionRange,
            DebugTunerPositionRange
        );
        DrawDebugTunerSlider(
            "W",
            ref _debugWidthDelta,
            DebugTunerSizeDeltaMin,
            DebugTunerSizeDeltaMax
        );
        DrawDebugTunerSlider(
            "H",
            ref _debugHeightDelta,
            DebugTunerSizeDeltaMin,
            DebugTunerSizeDeltaMax
        );
        DrawDebugTunerSlider(
            "Scale Mul",
            ref _debugCardScaleMul,
            DebugTunerScaleMulMin,
            DebugTunerScaleMulMax
        );

        DrawDebugTunerMaskRow();
        DrawDebugTunerComputedRow();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset"))
        {
            _debugX = 4f;
            _debugY = 10f;
            _debugWidthDelta = -8f;
            _debugHeightDelta = -20f;
            _debugCardScaleMul = 1f;
            _debugClipMaskEnabled = true;
            SyncDebugTunerMaskState();
            ReapplyAfterDebugTunerChange();
        }
        if (GUILayout.Button("Log Current Values"))
            BppLog.Info(
                "HistoryPanel",
                "PreviewTuner (1080p-equiv px, multiplied by panelScale at runtime): "
                    + $"X={_debugX:F2}f, "
                    + $"Y={_debugY:F2}f, "
                    + $"WΔ={_debugWidthDelta:F2}f, "
                    + $"HΔ={_debugHeightDelta:F2}f, "
                    + $"cardScaleMul={_debugCardScaleMul:F4}f, "
                    + $"mask={_debugClipMaskEnabled} "
                    + $"@ screen={Screen.width}x{Screen.height} panelScale={ComputePreviewPanelScale():F3}"
            );
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
        GUI.DragWindow();
    }

    private void DrawDebugTunerSlider(string label, ref float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}: {value:F2}", GUILayout.Width(130));
        var next = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.EndHorizontal();
        if (!Mathf.Approximately(next, value))
        {
            value = next;
            ReapplyAfterDebugTunerChange();
        }
    }

    private void DrawDebugTunerMaskRow()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Clip Mask", GUILayout.Width(130));
        var next = GUILayout.Toggle(
            _debugClipMaskEnabled,
            _debugClipMaskEnabled ? " ON (clipped to W×H)" : " OFF (cards spill into overlay)"
        );
        GUILayout.EndHorizontal();
        if (next != _debugClipMaskEnabled)
        {
            _debugClipMaskEnabled = next;
            SyncDebugTunerMaskState();
        }
    }

    private void DrawDebugTunerComputedRow()
    {
        var b = _previewContainerBounds;
        var panelScale = ComputePreviewPanelScale();
        var clipX = b.x + _debugX * panelScale;
        var clipY = b.y + _debugY * panelScale;
        var clipW = Mathf.Max(1f, b.width + _debugWidthDelta * panelScale);
        var clipH = Mathf.Max(1f, b.height + _debugHeightDelta * panelScale);
        GUILayout.Label(
            $"clip = ({clipX:F0},{clipY:F0}) {clipW:F0}x{clipH:F0}  [actual px]"
        );
        GUILayout.Label(
            $"bounds = ({b.x:F0},{b.y:F0}) {b.width:F0}x{b.height:F0}  screen={Screen.width}x{Screen.height} panelScale={panelScale:F3}"
        );
    }

    private void SyncDebugTunerMaskState()
    {
        if (_battleBoardPreview == null)
            return;
        if (_debugClipMaskAppliedToPreview == _debugClipMaskEnabled)
            return;
        _battleBoardPreview.SetClipMaskEnabled(_debugClipMaskEnabled);
        _debugClipMaskAppliedToPreview = _debugClipMaskEnabled;
    }

    private void ReapplyAfterDebugTunerChange()
    {
        if (_battleBoardPreview == null || !_hasPreviewContainerBounds)
            return;
        if (ApplyPreviewContainerBounds(_previewContainerBounds))
            RefreshSelectedBattlePreview();
    }
}
