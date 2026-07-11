#nullable enable
#pragma warning disable CS0436
using System.Text;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Patches;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

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

            var services = BppPatchHost.Services;
            ChoicePedestalSnapshot? choicePedestal =
                TooltipPreviewModePolicy.ShouldReadChoicePedestal(services.Config)
                    ? services.EncounterState?.GetChoicePedestal()
                    : null;
            var mode = TooltipPreviewModePolicy.Resolve(services.Config, choicePedestal);
            if (mode != TooltipPreviewMode.Enchant)
                return;

            // On an enchant pedestal choice screen, restrict the preview to the
            // enchant type(s) that pedestal would apply; empty otherwise (manual
            // Ctrl / Always) so the full preview is kept.
            var restrictTo = TooltipPreviewModePolicy.ResolveEnchantRestriction(
                services.Config,
                choicePedestal
            );

            var previewSegments = ItemEnchantPreviewService.BuildPreviewSegments(
                __instance.CardInstance,
                restrictTo
            );
            if (previewSegments.Count == 0)
                return;

            var passiveBuilder = __result.Item1;
            if (passiveBuilder.Length > 0 && passiveBuilder[passiveBuilder.Length - 1] != '\n')
            {
                passiveBuilder.Append('\n');
            }

            passiveBuilder.Append(ItemEnchantPreviewFormatting.PreviewHeaderText);
            passiveBuilder.Append('\n');

            foreach (var segment in previewSegments)
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                    ItemEnchantPreviewFormatting.AppendTooltipText(passiveBuilder, segment.Text);
            }
        }
        catch (System.Exception ex)
        {
            BppLog.Error("ItemEnchantPreview", "Failed to append passive tooltip previews", ex);
        }
    }
}
