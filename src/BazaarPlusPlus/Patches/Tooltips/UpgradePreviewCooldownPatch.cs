#nullable enable
using BazaarPlusPlus.Game.Tooltips;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(typeof(CooldownRenderer), nameof(CooldownRenderer.RenderFromTooltip))]
internal static class UpgradePreviewCooldownPatch
{
    [HarmonyPostfix]
    private static void Postfix(CooldownRenderer __instance, CardTooltipData tooltipData)
    {
        if (
            !UpgradePreviewValueRegistry.TryResolveEffectiveCooldowns(
                tooltipData,
                out var currentSeconds,
                out var upgradedSeconds
            )
        )
            return;

        __instance.SetCooldown($"{currentSeconds:F1}", canFuse: true, $"{upgradedSeconds:F1}");
    }
}
