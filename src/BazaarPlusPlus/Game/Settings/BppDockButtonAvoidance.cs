#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppDockButtonBounds(float minX, float maxX, float minY, float maxY)
{
    internal float MinX { get; } = minX;
    internal float MaxX { get; } = maxX;
    internal float MinY { get; } = minY;
    internal float MaxY { get; } = maxY;
    internal float Width => Math.Max(0f, MaxX - MinX);
    internal float Height => Math.Max(0f, MaxY - MinY);
    internal bool IsValid =>
        float.IsFinite(MinX)
        && float.IsFinite(MaxX)
        && float.IsFinite(MinY)
        && float.IsFinite(MaxY)
        && MaxX >= MinX
        && MaxY >= MinY;

    internal static BppDockButtonBounds FromCenter(
        float centerX,
        float centerY,
        float width,
        float height
    )
    {
        var halfWidth = Math.Max(0f, width) * 0.5f;
        var halfHeight = Math.Max(0f, height) * 0.5f;
        return new BppDockButtonBounds(
            centerX - halfWidth,
            centerX + halfWidth,
            centerY - halfHeight,
            centerY + halfHeight
        );
    }

    internal bool Contains(BppDockButtonBounds other) =>
        IsValid
        && other.IsValid
        && other.MinX >= MinX
        && other.MaxX <= MaxX
        && other.MinY >= MinY
        && other.MaxY <= MaxY;

    internal bool Overlaps(BppDockButtonBounds other) =>
        IsValid
        && other.IsValid
        && MinX < other.MaxX
        && MaxX > other.MinX
        && MinY < other.MaxY
        && MaxY > other.MinY;

    internal bool TryIntersect(BppDockButtonBounds other, out BppDockButtonBounds intersection)
    {
        intersection = new BppDockButtonBounds(
            Math.Max(MinX, other.MinX),
            Math.Min(MaxX, other.MaxX),
            Math.Max(MinY, other.MinY),
            Math.Min(MaxY, other.MaxY)
        );
        return intersection.IsValid;
    }
}

internal readonly struct BppDockButtonObstacle(
    string name,
    BppDockButtonBounds bounds,
    bool isActive
)
{
    internal string Name { get; } = name;
    internal BppDockButtonBounds Bounds { get; } = bounds;
    internal bool IsActive { get; } = isActive;
}

internal readonly struct BppDockButtonAvoidanceResult(
    bool canApply,
    BppSettingsDockLocalPosition position,
    string? blockerName,
    bool wasAdjusted
)
{
    internal bool CanApply { get; } = canApply;
    internal BppSettingsDockLocalPosition Position { get; } = position;
    internal string? BlockerName { get; } = blockerName;
    internal bool WasAdjusted { get; } = wasAdjusted;
}

internal static class BppDockButtonAvoidanceSolver
{
    internal static BppDockButtonAvoidanceResult Resolve(
        BppDockButtonBounds parentBounds,
        float dockWidth,
        float dockHeight,
        BppSettingsDockLocalPosition desiredPosition,
        IReadOnlyList<BppSettingsDockLocalPosition> orderedCandidates,
        IReadOnlyList<BppDockButtonObstacle> blockers
    )
    {
        if (
            !parentBounds.IsValid
            || dockWidth <= 0f
            || dockHeight <= 0f
            || !float.IsFinite(dockWidth)
            || !float.IsFinite(dockHeight)
        )
        {
            return new BppDockButtonAvoidanceResult(
                canApply: false,
                desiredPosition,
                blockerName: null,
                wasAdjusted: false
            );
        }

        var desiredBounds = BppDockButtonBounds.FromCenter(
            desiredPosition.X,
            desiredPosition.Y,
            dockWidth,
            dockHeight
        );
        var desiredBlocker = FindBlocker(desiredBounds, blockers);
        if (parentBounds.Contains(desiredBounds) && desiredBlocker == null)
        {
            return new BppDockButtonAvoidanceResult(
                canApply: true,
                desiredPosition,
                blockerName: null,
                wasAdjusted: false
            );
        }

        for (var index = 0; index < orderedCandidates.Count; index++)
        {
            var candidate = orderedCandidates[index];
            var candidateBounds = BppDockButtonBounds.FromCenter(
                candidate.X,
                candidate.Y,
                dockWidth,
                dockHeight
            );
            if (!parentBounds.Contains(candidateBounds))
                continue;

            if (FindBlocker(candidateBounds, blockers) != null)
                continue;

            return new BppDockButtonAvoidanceResult(
                canApply: true,
                candidate,
                desiredBlocker,
                wasAdjusted: true
            );
        }

        return new BppDockButtonAvoidanceResult(
            canApply: false,
            desiredPosition,
            desiredBlocker,
            wasAdjusted: false
        );
    }

    private static string? FindBlocker(
        BppDockButtonBounds candidateBounds,
        IReadOnlyList<BppDockButtonObstacle> blockers
    )
    {
        for (var index = 0; index < blockers.Count; index++)
        {
            var blocker = blockers[index];
            if (blocker.IsActive && candidateBounds.Overlaps(blocker.Bounds))
                return blocker.Name;
        }

        return null;
    }
}

internal sealed class BppDockButtonAvoidance
{
    private const string BppObjectPrefix = "BPP_";
    private const int FallbackCandidateCount = 6;
    private readonly List<Button> _buttonScratch = [];
    private readonly List<BppDockButtonObstacle> _blockerScratch = [];
    private readonly BppSettingsDockLocalPosition[] _candidateScratch =
        new BppSettingsDockLocalPosition[FallbackCandidateCount];
    private readonly Vector3[] _cornerScratch = new Vector3[4];

    internal BppDockButtonAvoidanceResult Resolve(
        RectTransform parentRect,
        RectTransform anchorRect,
        RectTransform dockRect,
        BppSettingsDockLocalPosition desiredPosition,
        BppSettingsDockPlacement finalPlacement
    )
    {
        var dockBounds = CalculateLocalBounds(parentRect, dockRect);
        if (dockBounds.Width <= 0.0001f || dockBounds.Height <= 0.0001f)
            dockBounds = CalculateLocalBounds(parentRect, anchorRect);

        var allowedBounds = CalculateAllowedBounds(parentRect);
        var blockers = CollectActiveNativeBlockers(parentRect, anchorRect, dockRect);
        anchorRect.GetWorldCorners(_cornerScratch);
        var anchorCenter = parentRect.InverseTransformPoint(
            (_cornerScratch[0] + _cornerScratch[2]) * 0.5f
        );
        var anchorTop = parentRect.InverseTransformPoint(
            (_cornerScratch[1] + _cornerScratch[2]) * 0.5f
        );
        var anchorBottom = parentRect.InverseTransformPoint(
            (_cornerScratch[0] + _cornerScratch[3]) * 0.5f
        );
        for (var index = 0; index < _candidateScratch.Length; index++)
        {
            _candidateScratch[index] = CalculateStackedCandidatePosition(
                anchorCenter.x,
                anchorCenter.y,
                anchorTop.y,
                anchorBottom.y,
                desiredPosition.Z,
                finalPlacement,
                index
            );
        }

        return BppDockButtonAvoidanceSolver.Resolve(
            allowedBounds,
            dockBounds.Width,
            dockBounds.Height,
            desiredPosition,
            _candidateScratch,
            blockers
        );
    }

    internal static BppSettingsDockLocalPosition CalculateStackedCandidatePosition(
        float anchorCenterLocalX,
        float anchorCenterLocalY,
        float anchorTopLocalY,
        float anchorBottomLocalY,
        float currentLocalZ,
        BppSettingsDockPlacement finalPlacement,
        int candidateOffset
    )
    {
        var height = Math.Abs(anchorTopLocalY - anchorBottomLocalY);
        var upDirection = Math.Sign(anchorTopLocalY - anchorBottomLocalY);
        if (upDirection == 0)
            upDirection = 1;

        var stepCount = finalPlacement.SiblingStepCount + Math.Max(0, candidateOffset) + 1;
        return new BppSettingsDockLocalPosition(
            anchorCenterLocalX,
            anchorCenterLocalY + upDirection * (height + finalPlacement.SiblingGap) * stepCount,
            currentLocalZ
        );
    }

    private BppDockButtonBounds CalculateAllowedBounds(RectTransform parentRect)
    {
        var parentBounds = CalculateLocalBounds(parentRect, parentRect);
        var canvasRect =
            parentRect.GetComponentInParent<Canvas>()?.rootCanvas.transform as RectTransform;
        if (canvasRect == null || canvasRect == parentRect)
            return parentBounds;

        var canvasBounds = CalculateLocalBounds(parentRect, canvasRect);
        if (!parentBounds.IsValid)
            return canvasBounds;

        if (!canvasBounds.IsValid)
            return parentBounds;

        parentBounds.TryIntersect(canvasBounds, out var intersection);
        return intersection;
    }

    private IReadOnlyList<BppDockButtonObstacle> CollectActiveNativeBlockers(
        RectTransform parentRect,
        RectTransform anchorRect,
        RectTransform dockRect
    )
    {
        var canvasRect = parentRect.GetComponentInParent<Canvas>()?.rootCanvas.transform;
        var searchRoot = canvasRect ?? parentRect.parent ?? parentRect;
        _buttonScratch.Clear();
        _blockerScratch.Clear();
        searchRoot.GetComponentsInChildren(includeInactive: false, _buttonScratch);
        foreach (var button in _buttonScratch)
        {
            if (!IsActiveNativeBlocker(button, anchorRect, dockRect))
                continue;

            var candidateRect = (RectTransform)button.transform;
            _blockerScratch.Add(
                new BppDockButtonObstacle(
                    button.gameObject.name,
                    CalculateLocalBounds(parentRect, candidateRect),
                    isActive: true
                )
            );
        }

        return _blockerScratch;
    }

    private static bool IsActiveNativeBlocker(
        Button button,
        RectTransform anchorRect,
        RectTransform dockRect
    )
    {
        if (button == null || !button.gameObject.activeInHierarchy)
            return false;

        if (button.transform is not RectTransform candidateRect)
            return false;

        if (candidateRect == anchorRect || candidateRect == dockRect)
            return false;

        if (
            candidateRect.IsChildOf(anchorRect)
            || candidateRect.IsChildOf(dockRect)
            || anchorRect.IsChildOf(candidateRect)
            || dockRect.IsChildOf(candidateRect)
        )
            return false;

        return !HasBppAncestor(candidateRect);
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

    private BppDockButtonBounds CalculateLocalBounds(RectTransform parentRect, RectTransform rect)
    {
        rect.GetWorldCorners(_cornerScratch);

        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        for (var index = 0; index < _cornerScratch.Length; index++)
        {
            var local = parentRect.InverseTransformPoint(_cornerScratch[index]);
            minX = Math.Min(minX, local.x);
            maxX = Math.Max(maxX, local.x);
            minY = Math.Min(minY, local.y);
            maxY = Math.Max(maxY, local.y);
        }

        return new BppDockButtonBounds(minX, maxX, minY, maxY);
    }
}
