#pragma warning disable CS0436
using System.Collections;
using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.Input;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardController), "ShowTooltips")]
internal static class UpgradePreviewTooltipPatch
{
    private static readonly HashSet<CardController> PendingControllers =
        new HashSet<CardController>();

    [HarmonyPostfix]
    private static void Postfix(CardController __instance)
    {
        TryScheduleUpgradeTooltip(__instance);
    }

    internal static bool TryScheduleUpgradeTooltip(
        CardController controller,
        CardTooltipData tooltipData = null
    )
    {
        if (controller == null)
            return false;

        if (!BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview))
            return false;

        var card = controller.CardData;
        if (card is not ItemCard)
            return false;

        if (!card.CanCardUpgrade())
            return false;

        var resolvedTooltipData = tooltipData ?? controller.GetTooltipData() as CardTooltipData;
        if (resolvedTooltipData == null)
            return false;

        if (Data.TooltipParentComponent == null)
            return false;

        if (!PendingControllers.Add(controller))
            return false;

        controller.StartCoroutine(
            ShowUpgradeTooltipWhenReady(controller, card, resolvedTooltipData)
        );
        return true;
    }

    private static IEnumerator ShowUpgradeTooltipWhenReady(
        CardController controller,
        Card card,
        CardTooltipData tooltipData
    )
    {
        try
        {
            const int maxFramesToWait = 10;
            for (var i = 0; i < maxFramesToWait; i++)
            {
                if (
                    controller == null
                    || controller.CardData != card
                    || !BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview)
                )
                {
                    yield break;
                }

                var tooltipParent = Data.TooltipParentComponent;
                if (tooltipParent != null && tooltipParent.GetCardTooltipController(card) != null)
                {
                    tooltipParent.DisplayUpgradeTooltips(
                        controller.transform,
                        controller.TooltipOffset,
                        tooltipData
                    );
                    yield break;
                }

                yield return null;
            }
        }
        finally
        {
            PendingControllers.Remove(controller);
        }
    }
}
