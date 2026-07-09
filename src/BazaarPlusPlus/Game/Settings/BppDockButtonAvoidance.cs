#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppDockButtonAvoidanceResult(
    BppSettingsDockLocalPosition position,
    string? blockerName
)
{
    internal BppSettingsDockLocalPosition Position { get; } = position;
    internal string? BlockerName { get; } = blockerName;

    internal bool WasAdjusted => BlockerName != null;
}

internal static class BppDockButtonAvoidance
{
    private const string BppObjectPrefix = "BPP_";

    internal static BppDockButtonAvoidanceResult StackAboveCollectionButtonWhenLeftSlotIsBlocked(
        RectTransform parentRect,
        RectTransform anchorRect,
        RectTransform dockRect,
        BppSettingsDockLocalPosition desiredPosition,
        BppSettingsDockPlacement placement
    )
    {
        if (placement.Side == BppSettingsDockSide.AboveAnchor)
            return new BppDockButtonAvoidanceResult(desiredPosition, blockerName: null);

        var dockSize = CalculateLocalSize(parentRect, dockRect);
        if (dockSize.x <= 0.0001f || dockSize.y <= 0.0001f)
            dockSize = CalculateLocalSize(parentRect, anchorRect);

        if (dockSize.x <= 0.0001f || dockSize.y <= 0.0001f)
            return new BppDockButtonAvoidanceResult(desiredPosition, blockerName: null);

        var desiredBounds = BppDockButtonLocalBounds.FromCenter(
            desiredPosition.X,
            desiredPosition.Y,
            dockSize.x,
            dockSize.y
        );
        foreach (var candidate in EnumerateCandidateButtons(parentRect, anchorRect, dockRect))
        {
            if (candidate.transform is not RectTransform candidateRect)
                continue;

            var candidateBounds = CalculateLocalBounds(parentRect, candidateRect);
            if (!desiredBounds.Overlaps(candidateBounds))
                continue;

            var stackedPosition = TryPlaceAboveCollectionButton(
                parentRect,
                dockSize,
                desiredPosition.Z,
                placement
            );
            if (stackedPosition is { } position)
                return new BppDockButtonAvoidanceResult(position, candidate.name);

            return new BppDockButtonAvoidanceResult(desiredPosition, blockerName: null);
        }

        return new BppDockButtonAvoidanceResult(desiredPosition, blockerName: null);
    }

    private static BppSettingsDockLocalPosition? TryPlaceAboveCollectionButton(
        RectTransform parentRect,
        Vector2 dockSize,
        float z,
        BppSettingsDockPlacement placement
    )
    {
        var collectionDockName = $"BPP_SettingsDockButton_CollectionPanel_{placement.Key}";
        if (parentRect.Find(collectionDockName) is not RectTransform collectionRect)
            return null;

        if (!collectionRect.gameObject.activeInHierarchy)
            return null;

        var collectionBounds = CalculateLocalBounds(parentRect, collectionRect);
        var upDirection = ResolveVerticalDirection(parentRect, collectionRect);
        var centerX = (collectionBounds.MinX + collectionBounds.MaxX) * 0.5f;
        var halfDockHeight = dockSize.y * 0.5f;
        var centerY =
            upDirection >= 0f
                ? collectionBounds.MaxY + placement.SiblingGap + halfDockHeight
                : collectionBounds.MinY - placement.SiblingGap - halfDockHeight;

        return new BppSettingsDockLocalPosition(centerX, centerY, z);
    }

    private static float ResolveVerticalDirection(RectTransform parentRect, RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var topWorld = (corners[1] + corners[2]) * 0.5f;
        var bottomWorld = (corners[0] + corners[3]) * 0.5f;
        var topLocal = parentRect.InverseTransformPoint(topWorld);
        var bottomLocal = parentRect.InverseTransformPoint(bottomWorld);
        var worldUpDirectionLocal = Math.Sign(topLocal.y - bottomLocal.y);
        return worldUpDirectionLocal == 0 ? 1f : worldUpDirectionLocal;
    }

    private static Vector2 CalculateLocalSize(RectTransform parentRect, RectTransform rect)
    {
        var bounds = CalculateLocalBounds(parentRect, rect);
        return new Vector2(bounds.Width, bounds.Height);
    }

    private static BppDockButtonLocalBounds CalculateLocalBounds(
        RectTransform parentRect,
        RectTransform rect
    )
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        for (var index = 0; index < corners.Length; index++)
        {
            var local = parentRect.InverseTransformPoint(corners[index]);
            minX = Math.Min(minX, local.x);
            maxX = Math.Max(maxX, local.x);
            minY = Math.Min(minY, local.y);
            maxY = Math.Max(maxY, local.y);
        }

        return new BppDockButtonLocalBounds(minX, maxX, minY, maxY);
    }

    private static List<Button> EnumerateCandidateButtons(
        RectTransform parentRect,
        RectTransform anchorRect,
        RectTransform dockRect
    )
    {
        var searchRoot = parentRect.parent as RectTransform ?? parentRect;
        var candidates = new List<Button>();
        foreach (
            var candidate in searchRoot.GetComponentsInChildren<Button>(includeInactive: false)
        )
        {
            if (IsCandidateButton(candidate, anchorRect, dockRect))
                candidates.Add(candidate);
        }

        return candidates;
    }

    private static bool IsCandidateButton(
        Button candidate,
        RectTransform anchorRect,
        RectTransform dockRect
    )
    {
        if (candidate == null || !candidate.gameObject.activeInHierarchy)
            return false;

        if (candidate.transform is not RectTransform candidateRect)
            return false;

        if (candidateRect == anchorRect || candidateRect == dockRect)
            return false;

        if (candidateRect.IsChildOf(anchorRect) || candidateRect.IsChildOf(dockRect))
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

    private readonly struct BppDockButtonLocalBounds(float minX, float maxX, float minY, float maxY)
    {
        internal float MinX { get; } = minX;
        internal float MaxX { get; } = maxX;
        internal float MinY { get; } = minY;
        internal float MaxY { get; } = maxY;
        internal float Width => Math.Max(0f, MaxX - MinX);
        internal float Height => Math.Max(0f, MaxY - MinY);

        internal static BppDockButtonLocalBounds FromCenter(
            float centerX,
            float centerY,
            float width,
            float height
        )
        {
            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            return new BppDockButtonLocalBounds(
                centerX - halfWidth,
                centerX + halfWidth,
                centerY - halfHeight,
                centerY + halfHeight
            );
        }

        internal bool Overlaps(BppDockButtonLocalBounds other) =>
            MinX < other.MaxX && MaxX > other.MinX && MinY < other.MaxY && MaxY > other.MinY;
    }
}
