#pragma warning disable CS0436
using System.Text;
using System.Threading;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

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
        ModState.SetCombatFrameTotal(message.Data?.Frames?.Count ?? 0);
        ModState.LastVictoryCondition =
            message.Data.Winner == ECombatantId.Player
                ? EVictoryCondition.Win
                : EVictoryCondition.Lose;
    }
}

// Combat speed: set speed to the combat speed multiplier
[HarmonyPatch(typeof(CombatSimHandler), "SetSpeed")]
class CombatSpeedPatch
{
    [HarmonyPrefix]
    static void Prefix(ref float speed)
    {
        if (!ModState.CombatPlaybackActive)
            return;

        speed = ModState.CombatSpeedMultiplier;
    }
}

[HarmonyPatch(typeof(FinalBlowSlowDownController), nameof(FinalBlowSlowDownController.Process))]
class CombatFrameAdvancePatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        ModState.AdvanceCombatFrame();
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

            if (Data.IsInCombat)
                return;

            var previewSegments = ItemEnchantPreviewBuilder.BuildPreviewSegments(
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

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.LockTooltipToggle))]
public static class CardTooltipControllerLockTogglePatch
{
    [HarmonyPrefix]
    static bool Prefix(CardTooltipController __instance)
    {
        var currentCard = __instance?.CurrentCard;
        if (currentCard == null)
            return true;

        var controller = Data.CardAndSkillLookup?.GetCardController(currentCard);
        if (controller == null)
            return true;

        if (controller.GetComponent<ShowcaseCardMarker>() == null)
            return true;

        BppLog.Debug(
            "EncounterTooltipPreview",
            $"Suppressed lock toggle for showcase card {currentCard.Template?.InternalName ?? currentCard.TemplateId.ToString()}"
        );
        return false;
    }
}
