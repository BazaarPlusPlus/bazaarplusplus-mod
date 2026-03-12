#pragma warning disable CS0436
using HarmonyLib;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardController), "ProceedClick")]
internal static class ShowcaseCardClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardController __instance)
    {
        return __instance == null || __instance.GetComponent<ShowcaseCardMarker>() == null;
    }
}

[HarmonyPatch(typeof(ItemController), nameof(ItemController.OnBeginDrag))]
internal static class ShowcaseCardDragPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemController __instance)
    {
        return __instance == null || __instance.GetComponent<ShowcaseCardMarker>() == null;
    }
}

/// <summary>
/// When ShowTooltips runs on a showcase card, set a bypass flag so the
/// tooltip lock check does not block it.
/// </summary>
[HarmonyPatch(typeof(CardController), "ShowTooltips")]
internal static class ShowcaseCardShowTooltipsPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardController __instance)
    {
        if (__instance != null && __instance.GetComponent<ShowcaseCardMarker>() != null)
            ShowcaseTooltipBypass.Active = true;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        ShowcaseTooltipBypass.Active = false;
    }
}

/// <summary>
/// While the bypass flag is active, pretend the tooltip controller is not locked.
/// </summary>
[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.IsCardTooltipControllerLocked)
)]
internal static class ShowcaseTooltipLockBypassPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        if (!ShowcaseTooltipBypass.Active)
            return true;

        __result = false;
        return false;
    }
}

/// <summary>
/// Track when ShowTooltipController executes for a showcase card so we can
/// prevent DisableLockModeCanvas from tearing down the big-picture lock.
///
/// ShowTooltipController is synchronous and calls DisableLockModeCanvas
/// directly, so a simple flag works reliably here.
/// </summary>
[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ShowTooltipController))]
internal static class ShowcaseShowTooltipControllerPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        if (ShowcaseTooltipBypass.Active)
            ShowcaseTooltipBypass.ShowingTooltip = true;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        ShowcaseTooltipBypass.ShowingTooltip = false;
    }
}

/// <summary>
/// Skip DisableLockModeCanvas when showing a tooltip for a showcase card.
/// This keeps the big-picture lock mode canvas (raycast blocking, visuals) intact.
/// </summary>
[HarmonyPatch(typeof(CardTooltipController), "DisableLockModeCanvas")]
internal static class ShowcaseDisableLockCanvasPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !ShowcaseTooltipBypass.ShowingTooltip;
    }
}

internal static class ShowcaseTooltipBypass
{
    /// <summary>
    /// Set during CardController.ShowTooltips() for showcase cards.
    /// Makes IsCardTooltipControllerLocked return false.
    /// </summary>
    public static bool Active;

    /// <summary>
    /// Set during CardTooltipController.ShowTooltipController() when
    /// triggered by a showcase card. Blocks DisableLockModeCanvas.
    /// </summary>
    public static bool ShowingTooltip;
}
