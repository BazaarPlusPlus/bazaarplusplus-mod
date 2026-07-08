#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class ItemEnchantPreviewTooltipLayerPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController __instance, string text)
    {
        try
        {
            TooltipLayerOverride.SetElevated(
                __instance,
                ItemEnchantPreviewTooltipLayerPolicy.ShouldElevateForPassiveText(text)
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("ItemEnchantPreview", "Failed to update tooltip render layer", ex);
        }
    }
}

internal static class TooltipLayerOverride
{
    private sealed class CanvasSortingState
    {
        internal CanvasSortingState(bool overrideSorting, int sortingOrder)
        {
            OverrideSorting = overrideSorting;
            SortingOrder = sortingOrder;
        }

        internal bool OverrideSorting { get; }

        internal int SortingOrder { get; }

        internal HashSet<int> Owners { get; } = new HashSet<int>();
    }

    private sealed class TransformSiblingState
    {
        internal TransformSiblingState(Transform? parent, int siblingIndex)
        {
            Parent = parent;
            SiblingIndex = siblingIndex;
        }

        internal Transform? Parent { get; }

        internal int SiblingIndex { get; }

        internal HashSet<int> Owners { get; } = new HashSet<int>();
    }

    private static readonly Dictionary<Canvas, CanvasSortingState> States =
        new Dictionary<Canvas, CanvasSortingState>();

    private static readonly Dictionary<Transform, TransformSiblingState> SiblingStates =
        new Dictionary<Transform, TransformSiblingState>();

    internal static void SetElevated(CardTooltipController? controller, bool elevated)
    {
        if (controller == null)
            return;

        var ownerId = controller.GetInstanceID();
        var rootCanvas = controller.RootCanvasComponent;
        Apply(rootCanvas, ownerId, elevated);

        var positioningCanvas = controller.PositioningCanvas;
        if (positioningCanvas != null && positioningCanvas != rootCanvas)
            Apply(positioningCanvas, ownerId, elevated);

        var lockModeCanvas = controller.LockModeCanvas;
        if (
            lockModeCanvas != null
            && lockModeCanvas != rootCanvas
            && lockModeCanvas != positioningCanvas
        )
            Apply(lockModeCanvas, ownerId, elevated);

        // MonsterBoardTooltip is part of the same CardTooltipController prefab,
        // so Canvas sorting alone cannot beat it; move the tooltip content to
        // the end of its local draw order while the BPP preview is visible.
        Apply(controller.transform, ownerId, elevated);
        Apply(controller.RootCanvas, ownerId, elevated);
        Apply(controller.CanvasContentRectTransform, ownerId, elevated);
        Apply(controller.TooltipRectTransform, ownerId, elevated);

        var lockModeContainer = controller.LockModeContainer;
        if (lockModeContainer != null)
            Apply(lockModeContainer.transform, ownerId, elevated);
    }

    private static void Apply(Canvas? canvas, int ownerId, bool elevated)
    {
        if (canvas == null)
            return;

        if (elevated)
        {
            if (!States.TryGetValue(canvas, out var state))
            {
                state = new CanvasSortingState(canvas.overrideSorting, canvas.sortingOrder);
                States.Add(canvas, state);
            }

            state.Owners.Add(ownerId);
            canvas.overrideSorting = true;
            if (canvas.sortingOrder < ItemEnchantPreviewTooltipLayerPolicy.ElevatedSortingOrder)
                canvas.sortingOrder = ItemEnchantPreviewTooltipLayerPolicy.ElevatedSortingOrder;

            return;
        }

        if (!States.TryGetValue(canvas, out var existing))
            return;

        existing.Owners.Remove(ownerId);
        if (existing.Owners.Count > 0)
            return;

        canvas.overrideSorting = existing.OverrideSorting;
        canvas.sortingOrder = existing.SortingOrder;
        States.Remove(canvas);
    }

    private static void Apply(Transform? transform, int ownerId, bool elevated)
    {
        if (transform == null)
            return;

        if (elevated)
        {
            if (!SiblingStates.TryGetValue(transform, out var state))
            {
                state = new TransformSiblingState(transform.parent, transform.GetSiblingIndex());
                SiblingStates.Add(transform, state);
            }

            state.Owners.Add(ownerId);
            transform.SetAsLastSibling();
            return;
        }

        if (!SiblingStates.TryGetValue(transform, out var existing))
            return;

        existing.Owners.Remove(ownerId);
        if (existing.Owners.Count > 0)
            return;

        if (existing.Parent != null && transform.parent == existing.Parent)
        {
            var siblingIndex = Math.Min(existing.SiblingIndex, existing.Parent.childCount - 1);
            transform.SetSiblingIndex(siblingIndex);
        }

        SiblingStates.Remove(transform);
    }
}
