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
            TooltipCanvasSortingOverride.SetElevated(
                __instance,
                ItemEnchantPreviewTooltipLayerPolicy.ShouldElevateForPassiveText(text)
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("ItemEnchantPreview", "Failed to update tooltip canvas sorting", ex);
        }
    }
}

internal static class TooltipCanvasSortingOverride
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

    private static readonly Dictionary<Canvas, CanvasSortingState> States =
        new Dictionary<Canvas, CanvasSortingState>();

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
}
