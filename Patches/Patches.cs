#pragma warning disable CS0436
using System.Text;
using System.Threading;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus;

// Combat sim: capture win/loss result
[HarmonyPatch(typeof(CombatSimHandler), "Simulate")]
class CombatSimPatch
{
    [HarmonyPrefix]
    static void Prefix(NetMessageCombatSim message, CancellationTokenSource cancellationToken)
    {
        if (ModState.LastMessageId == message.MessageId)
            return;
        ModState.LastMessageId = message.MessageId;
        ModState.LastVictoryCondition =
            message.Data.Winner == ECombatantId.Player
                ? EVictoryCondition.Win
                : EVictoryCondition.Lose;
    }
}

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

            var previewSegments = ItemEnchantPreviewBuilder.BuildPreviewSegments(
                __instance.CardInstance
            );
            if (previewSegments.Count == 0)
                return;

            if (__result.Item1.Length > 0)
                __result.Item1.AppendLine();

            foreach (var segment in previewSegments)
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                    __result.Item1.AppendLine(segment.Text);
            }
        }
        catch (System.Exception ex)
        {
            ModState.Logger?.LogError(
                $"[ItemEnchantPreview] Failed to append passive tooltip previews: {ex.Message}"
            );
        }
    }
}
