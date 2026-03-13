#pragma warning disable CS0436
using System.Text;
using HarmonyLib;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

// Item enchant preview: append BazaarPlusPlus-generated text into passive tooltip block
[HarmonyPatch(typeof(CardTooltipData), nameof(CardTooltipData.GetPassiveTooltipBlock))]
public static class CardTooltipDataPassivePatch
{
    [HarmonyPostfix]
    static void Postfix(
        CardTooltipData __instance,
        ref System.ValueTuple<StringBuilder, TooltipSegment?> __result
    )
    {
        try
        {
            if (__result.Item1 == null)
                return;

            if (Data.IsInCombat)
                return;

            var alwaysShow = ModState.EnchantPreviewAlwaysShowConfig?.Value ?? true;
            if (!alwaysShow && !KeyBindings.Modifiers.IsCtrlPressed(Keyboard.current))
                return;

            var previewSegments = ItemEnchantPreviewService.BuildPreviewSegments(
                __instance.CardInstance
            );
            if (previewSegments.Count == 0)
                return;

            var passiveBuilder = __result.Item1;
            if (passiveBuilder.Length > 0 && passiveBuilder[passiveBuilder.Length - 1] != '\n')
            {
                passiveBuilder.AppendLine();
            }

            passiveBuilder.AppendLine("Bazaar++");

            foreach (var segment in previewSegments)
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                    passiveBuilder.AppendLine(segment.Text);
            }
        }
        catch (System.Exception ex)
        {
            BppLog.Error("ItemEnchantPreview", "Failed to append passive tooltip previews", ex);
        }
    }
}
