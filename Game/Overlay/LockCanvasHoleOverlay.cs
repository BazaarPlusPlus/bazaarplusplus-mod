#pragma warning disable CS0436
using System.Collections.Generic;
using System.Linq;
using TheBazaar.UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus;

internal sealed class LockCanvasHoleOverlay
{
    private const string BlockerRootName = "BPPLockCanvasHole";
    private static readonly Color DebugBlockerColor = new Color(1f, 0.15f, 0.15f, 0.12f);

    private readonly Dictionary<Graphic, bool> _raycastTargets = new Dictionary<Graphic, bool>();

    private GameObject _blockerRoot;

    public void Apply(CardTooltipController tooltipController, Rect normalizedHole)
    {
        if (tooltipController?.LockModeCanvas == null)
            return;

        var canvasRect = tooltipController.LockModeCanvas.transform as RectTransform;
        if (canvasRect == null)
            return;

        Canvas.ForceUpdateCanvases();

        var root = EnsureBlockerRoot(canvasRect);
        var canvas = new HoleRect(0f, 0f, canvasRect.rect.width, canvasRect.rect.height);
        var hole = new HoleRect(
            canvas.Width * normalizedHole.xMin,
            canvas.Height * normalizedHole.yMin,
            canvas.Width * normalizedHole.width,
            canvas.Height * normalizedHole.height
        );
        var layout = LockCanvasHoleLayout.Calculate(canvas, hole);

        DisableBackgroundRaycasts(tooltipController, root.transform);
        ApplyBlocker((RectTransform)root.transform.GetChild(0), layout.Top, "Top");
        ApplyBlocker((RectTransform)root.transform.GetChild(1), layout.Bottom, "Bottom");
        ApplyBlocker((RectTransform)root.transform.GetChild(2), layout.Left, "Left");
        ApplyBlocker((RectTransform)root.transform.GetChild(3), layout.Right, "Right");
        root.SetActive(true);

        BppLog.Debug(
            "LockCanvasHoleOverlay",
            $"Applied hole on {tooltipController.name}: canvas=({canvas.Width:0.##},{canvas.Height:0.##}), normalizedHole=({normalizedHole.xMin:0.###},{normalizedHole.yMin:0.###},{normalizedHole.width:0.###},{normalizedHole.height:0.###}), hole=({hole.X:0.##},{hole.Y:0.##},{hole.Width:0.##},{hole.Height:0.##}), disabledRaycasts={_raycastTargets.Count}"
        );
    }

    public void Clear()
    {
        var restored = 0;
        foreach (var pair in _raycastTargets.ToArray())
        {
            if (pair.Key != null)
            {
                pair.Key.raycastTarget = pair.Value;
                restored++;
            }
        }

        _raycastTargets.Clear();

        if (_blockerRoot != null)
            _blockerRoot.SetActive(false);

        BppLog.Debug(
            "LockCanvasHoleOverlay",
            $"Cleared hole overlay: restoredRaycasts={restored}, blockerRootActive={_blockerRoot != null && _blockerRoot.activeSelf}"
        );
    }

    private void DisableBackgroundRaycasts(CardTooltipController tooltipController, Transform blockerRoot)
    {
        foreach (var graphic in tooltipController.LockModeContainer.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic == null || graphic.transform.IsChildOf(blockerRoot))
                continue;

            if (tooltipController.lockModeExitButton != null && graphic.transform.IsChildOf(tooltipController.lockModeExitButton.transform))
                continue;

            if (!graphic.raycastTarget || !LooksLikeBackground(graphic, tooltipController.LockModeCanvas.transform as RectTransform))
                continue;

            if (!_raycastTargets.ContainsKey(graphic))
            {
                _raycastTargets[graphic] = graphic.raycastTarget;
                BppLog.Debug(
                    "LockCanvasHoleOverlay",
                    $"Disabled raycast on background graphic={graphic.name}, type={graphic.GetType().Name}, path={BuildPath(graphic.transform)}"
                );
            }

            graphic.raycastTarget = false;
        }
    }

    private static bool LooksLikeBackground(Graphic graphic, RectTransform canvasRect)
    {
        var rectTransform = graphic.transform as RectTransform;
        if (rectTransform == null || canvasRect == null)
            return false;

        var rect = rectTransform.rect;
        var canvas = canvasRect.rect;
        return rect.width >= canvas.width * 0.9f && rect.height >= canvas.height * 0.9f;
    }

    private GameObject EnsureBlockerRoot(RectTransform canvasRect)
    {
        if (_blockerRoot != null)
            return _blockerRoot;

        var existing = canvasRect.Find(BlockerRootName);
        if (existing != null)
        {
            _blockerRoot = existing.gameObject;
            return _blockerRoot;
        }

        _blockerRoot = new GameObject(BlockerRootName, typeof(RectTransform));
        var rootRect = (RectTransform)_blockerRoot.transform;
        rootRect.SetParent(canvasRect, false);
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        rootRect.SetAsLastSibling();

        CreateBlocker("Top", rootRect);
        CreateBlocker("Bottom", rootRect);
        CreateBlocker("Left", rootRect);
        CreateBlocker("Right", rootRect);

        return _blockerRoot;
    }

    private static void CreateBlocker(string name, Transform parent)
    {
        var blocker = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)blocker.transform;
        rect.SetParent(parent, false);

        var image = blocker.GetComponent<Image>();
        image.color = DebugBlockerColor;
        image.raycastTarget = true;
    }

    private static void ApplyBlocker(RectTransform rectTransform, HoleRect rect, string label)
    {
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = new Vector2(rect.X, -rect.Y);
        rectTransform.sizeDelta = new Vector2(rect.Width, rect.Height);

        BppLog.Debug(
            "LockCanvasHoleOverlay",
            $"Blocker {label}: pos=({rect.X:0.##},{rect.Y:0.##}) size=({rect.Width:0.##},{rect.Height:0.##})"
        );
    }

    private static string BuildPath(Transform transform)
    {
        var names = new List<string>();
        var current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }
}
