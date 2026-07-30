#nullable enable

using BazaarPlusPlus.Game.CombatReplay.Video;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Combat;

[HarmonyPatch(typeof(CardController), nameof(CardController.OnPointerEnter))]
internal static class ReplayRecordingCardPointerEnterPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !ReplayRecordingHoverSuppression.IsActive;
}

[HarmonyPatch(typeof(ItemController), nameof(ItemController.OnPointerMove))]
internal static class ReplayRecordingItemPointerMovePatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !ReplayRecordingHoverSuppression.IsActive;
}

[HarmonyPatch(typeof(SkillProxyRenderer), nameof(SkillProxyRenderer.OnPointerEnter))]
internal static class ReplayRecordingSkillPointerEnterPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !ReplayRecordingHoverSuppression.IsActive;
}

[HarmonyPatch(typeof(RecapItemVisualController), nameof(RecapItemVisualController.OnPointerEnter))]
internal static class ReplayRecordingRecapItemPointerEnterPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !ReplayRecordingHoverSuppression.IsActive;
}

[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.ShowCardTooltipController)
)]
internal static class ReplayRecordingCardTooltipPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !ReplayRecordingHoverSuppression.IsActive;
}

[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.ShowSecondaryCardTooltipController)
)]
internal static class ReplayRecordingSecondaryCardTooltipPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Task __result)
    {
        if (!ReplayRecordingHoverSuppression.IsActive)
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.ShowAuxiliaryTooltipController)
)]
internal static class ReplayRecordingAuxiliaryTooltipPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !ReplayRecordingHoverSuppression.IsActive;
}
