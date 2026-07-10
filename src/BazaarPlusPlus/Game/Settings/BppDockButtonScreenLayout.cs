#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppDockButtonScreenLayout
{
    private const string BppObjectPrefix = "BPP_";
    private const float VisibleAlphaThreshold = 0.01f;

    private readonly List<BppDockButtonObstacle> _blockerScratch = [];
    private readonly Vector3[] _cornerScratch = new Vector3[4];

    internal BppDockButtonLayoutPlan ResolveAndApply(
        Button anchorButton,
        RectTransform collectionRect,
        RectTransform settingsRect,
        float localGap
    )
    {
        var collectionButton = collectionRect.GetComponent<Button>();
        var settingsButton = settingsRect.GetComponent<Button>();
        if (collectionButton == null || settingsButton == null)
            return default;

        if (
            !TryCalculateButtonFootprint(
                anchorButton,
                (RectTransform)anchorButton.transform,
                out var gearBounds
            )
            || !TryCalculateButtonFootprint(
                collectionButton,
                collectionRect,
                out var collectionBounds
            )
            || !TryCalculateButtonFootprint(settingsButton, settingsRect, out var settingsBounds)
        )
            return default;

        var viewportBounds = new BppDockButtonBounds(0f, Screen.width, 0f, Screen.height);
        var anchorCanvas = ResolveRootCanvas(anchorButton.transform);
        if (anchorCanvas == null || !anchorCanvas.isActiveAndEnabled)
            return default;

        var gap = ResolveScreenGap(settingsRect.parent as RectTransform, localGap);
        var blockers = CollectVisibleNativeBlockers(
            viewportBounds,
            anchorButton,
            collectionButton,
            settingsButton,
            anchorCanvas.targetDisplay
        );
        var plan = BppDockButtonLayoutPlanner.Resolve(
            viewportBounds,
            gearBounds,
            collectionBounds.Width,
            collectionBounds.Height,
            settingsBounds.Width,
            settingsBounds.Height,
            gap,
            blockers
        );
        if (!plan.CanApply)
            return plan;

        if (
            !TryCalculateTargetLocalPosition(
                collectionRect,
                collectionButton,
                plan.CollectionBounds,
                out var collectionLocalPosition
            )
            || !TryCalculateTargetLocalPosition(
                settingsRect,
                settingsButton,
                plan.SettingsBounds,
                out var settingsLocalPosition
            )
        )
        {
            return new BppDockButtonLayoutPlan(
                canApply: false,
                plan.CollectionBounds,
                plan.SettingsBounds,
                plan.SettingsSlot,
                plan.BlockerName,
                wasAdjusted: false
            );
        }

        collectionRect.localPosition = collectionLocalPosition;
        settingsRect.localPosition = settingsLocalPosition;
        collectionRect.localRotation = Quaternion.identity;
        settingsRect.localRotation = Quaternion.identity;
        collectionRect.SetAsLastSibling();
        settingsRect.SetAsLastSibling();
        return plan;
    }

    private IReadOnlyList<BppDockButtonObstacle> CollectVisibleNativeBlockers(
        BppDockButtonBounds viewportBounds,
        Button anchorButton,
        Button collectionButton,
        Button settingsButton,
        int targetDisplay
    )
    {
        _blockerScratch.Clear();
        foreach (var button in UnityEngine.Object.FindObjectsOfType<Button>(includeInactive: true))
        {
            if (
                button == null
                || button == anchorButton
                || button == collectionButton
                || button == settingsButton
                || !button.gameObject.scene.IsValid()
                || !button.isActiveAndEnabled
                || !button.gameObject.activeInHierarchy
                || HasBppAncestor(button.transform)
                || IsRelated(button.transform, anchorButton.transform)
            )
                continue;

            var canvas = ResolveRootCanvas(button.transform);
            if (
                canvas == null
                || !canvas.isActiveAndEnabled
                || canvas.targetDisplay != targetDisplay
            )
                continue;

            var targetGraphic = button.targetGraphic;
            if (
                targetGraphic == null
                || !IsVisible(targetGraphic)
                || !TryCalculateScreenBounds(targetGraphic.rectTransform, out var bounds)
                || !viewportBounds.Overlaps(bounds)
            )
                continue;

            _blockerScratch.Add(
                new BppDockButtonObstacle(
                    $"{BuildHierarchyPath(button.transform)} [canvas={BuildHierarchyPath(canvas.transform)}]",
                    bounds,
                    isActive: true
                )
            );
        }

        return _blockerScratch;
    }

    private bool TryCalculateButtonFootprint(
        Button button,
        RectTransform fallbackRect,
        out BppDockButtonBounds bounds
    )
    {
        var visualRect = button.targetGraphic?.rectTransform ?? fallbackRect;
        return TryCalculateScreenBounds(visualRect, out bounds);
    }

    private bool TryCalculateScreenBounds(RectTransform rect, out BppDockButtonBounds bounds)
    {
        rect.GetWorldCorners(_cornerScratch);
        if (!TryResolveEventCamera(rect, out var camera))
        {
            bounds = default;
            return false;
        }

        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        for (var index = 0; index < _cornerScratch.Length; index++)
        {
            var screen = RectTransformUtility.WorldToScreenPoint(camera, _cornerScratch[index]);
            minX = Math.Min(minX, screen.x);
            maxX = Math.Max(maxX, screen.x);
            minY = Math.Min(minY, screen.y);
            maxY = Math.Max(maxY, screen.y);
        }

        bounds = new BppDockButtonBounds(minX, maxX, minY, maxY);
        return bounds.IsValid && bounds.Width > 0.0001f && bounds.Height > 0.0001f;
    }

    private bool TryCalculateTargetLocalPosition(
        RectTransform buttonRect,
        Button button,
        BppDockButtonBounds targetBounds,
        out Vector3 targetLocalPosition
    )
    {
        targetLocalPosition = default;
        var parentRect = buttonRect.parent as RectTransform;
        var visualRect = button.targetGraphic?.rectTransform ?? buttonRect;
        if (parentRect == null)
            return false;

        if (
            !TryCalculateScreenBounds(visualRect, out var currentBounds)
            || !TryResolveEventCamera(visualRect, out var camera)
        )
            return false;

        var currentScreenCenter = new Vector2(currentBounds.CenterX, currentBounds.CenterY);
        var targetScreenCenter = new Vector2(targetBounds.CenterX, targetBounds.CenterY);
        if (
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                visualRect,
                currentScreenCenter,
                camera,
                out var currentVisualLocalPoint
            )
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                visualRect,
                targetScreenCenter,
                camera,
                out var targetVisualLocalPoint
            )
        )
            return false;

        var currentWorldCenter = visualRect.TransformPoint(
            new Vector3(currentVisualLocalPoint.x, currentVisualLocalPoint.y, 0f)
        );
        var targetWorldCenter = visualRect.TransformPoint(
            new Vector3(targetVisualLocalPoint.x, targetVisualLocalPoint.y, 0f)
        );
        var targetRootWorldPosition = buttonRect.position + targetWorldCenter - currentWorldCenter;
        targetLocalPosition = parentRect.InverseTransformPoint(targetRootWorldPosition);
        return true;
    }

    private static bool IsVisible(Graphic graphic)
    {
        if (
            graphic == null
            || !graphic.isActiveAndEnabled
            || !graphic.gameObject.activeInHierarchy
            || graphic.canvasRenderer.cull
            || ResolveRootCanvas(graphic.transform) is not { isActiveAndEnabled: true }
        )
            return false;

        var alpha = graphic.color.a * graphic.canvasRenderer.GetAlpha();
        var stopAtCurrentTransform = false;
        for (
            var current = graphic.transform;
            current != null && !stopAtCurrentTransform;
            current = current.parent
        )
        {
            var canvas = current.GetComponent<Canvas>();
            if (canvas != null && !canvas.isActiveAndEnabled)
                return false;

            foreach (var group in current.GetComponents<CanvasGroup>())
            {
                alpha *= group.alpha;
                if (group.ignoreParentGroups)
                {
                    stopAtCurrentTransform = true;
                    break;
                }
            }
        }

        return alpha > VisibleAlphaThreshold;
    }

    private static float ResolveScreenGap(RectTransform? referenceRect, float localGap)
    {
        if (referenceRect == null || localGap <= 0f)
            return Math.Max(0f, localGap);

        if (!TryResolveEventCamera(referenceRect, out var camera))
            return Math.Max(0f, localGap);

        var origin = RectTransformUtility.WorldToScreenPoint(
            camera,
            referenceRect.TransformPoint(Vector3.zero)
        );
        var horizontal = RectTransformUtility.WorldToScreenPoint(
            camera,
            referenceRect.TransformPoint(new Vector3(localGap, 0f, 0f))
        );
        var vertical = RectTransformUtility.WorldToScreenPoint(
            camera,
            referenceRect.TransformPoint(new Vector3(0f, localGap, 0f))
        );
        return (Vector2.Distance(origin, horizontal) + Vector2.Distance(origin, vertical)) * 0.5f;
    }

    private static bool TryResolveEventCamera(Transform transform, out Camera? camera)
    {
        var canvas = ResolveRootCanvas(transform);
        if (canvas == null || !canvas.isActiveAndEnabled)
        {
            camera = null;
            return false;
        }

        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            camera = null;
            return true;
        }

        camera = canvas.worldCamera;
        return camera != null;
    }

    private static Canvas? ResolveRootCanvas(Transform transform)
    {
        Canvas? rootCanvas = null;
        for (var current = transform; current != null; current = current.parent)
        {
            var canvas = current.GetComponent<Canvas>();
            if (canvas != null)
                rootCanvas = canvas;
        }

        return rootCanvas;
    }

    private static bool HasBppAncestor(Transform transform)
    {
        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith(BppObjectPrefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsRelated(Transform candidate, Transform anchor) =>
        candidate.IsChildOf(anchor) || anchor.IsChildOf(candidate);

    private static string BuildHierarchyPath(Transform transform)
    {
        var path = transform.name;
        for (var current = transform.parent; current != null; current = current.parent)
            path = $"{current.name}/{path}";

        return path;
    }
}
