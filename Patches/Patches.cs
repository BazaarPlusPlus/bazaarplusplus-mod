#pragma warning disable CS0436
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

// Item enchant preview: append BazaarPlusPlus-generated tooltip segments
[HarmonyPatch(typeof(CardTooltipData), nameof(CardTooltipData.GetActiveAbilityTooltipBlock))]
public static class CardTooltipDataActiveAbilityPatch
{
    [HarmonyPostfix]
    static void Postfix(
        CardTooltipData __instance,
        ref System.Collections.Generic.List<TooltipSegment> __result
    )
    {
        try
        {
            if (__result == null)
                return;

            var previewSegments = ItemEnchantPreviewBuilder.BuildPreviewSegments(
                __instance.CardInstance
            );
            if (previewSegments.Count == 0)
                return;

            __result.AddRange(previewSegments);
        }
        catch (System.Exception ex)
        {
            ModState.Logger?.LogError(
                $"[ItemEnchantPreview] Failed to build tooltip previews: {ex.Message}"
            );
        }
    }
}
