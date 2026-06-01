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
    private static void AppendTooltipText(StringBuilder builder, string text)
    {
        if (builder == null || string.IsNullOrWhiteSpace(text))
            return;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (normalized.Length == 0)
            return;

        var lines = normalized.Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            builder.Append(line);
            builder.Append('\n');
        }
    }

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

            passiveBuilder.Append("Bazaar++\n");

            foreach (var segment in previewSegments)
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                    AppendTooltipText(passiveBuilder, segment.Text);
            }
        }
        catch (System.Exception ex)
        {
            BppLog.Error("ItemEnchantPreview", "Failed to append passive tooltip previews", ex);
        }
    }
}
