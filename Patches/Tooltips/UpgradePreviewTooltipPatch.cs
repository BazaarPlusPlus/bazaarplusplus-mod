#pragma warning disable CS0436
#nullable enable
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
        CardTooltipData? tooltipData = null
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

        // The ShowTooltips postfix can run for cards that are not the actively hovered card.
        // Only allow those implicit calls to schedule upgrade preview for the hovered controller.
        if (
            tooltipData == null
            && !controller.IsCursorOverCard
            && !controller.IsHovering
        )
        {
            return false;
        }

        if (!PendingControllers.Add(controller))
            return false;

        BppLog.Info(
            "TooltipPreview",
            $"UpgradeScheduleQueued card={DescribeCard(card)} source={(tooltipData == null ? "controller" : "refresh")}"
        );
        controller.StartCoroutine(
            RefreshUpgradePreviewWhenReady(controller, card, resolvedTooltipData)
        );
        return true;
    }

    private static IEnumerator RefreshUpgradePreviewWhenReady(
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
                    RefreshPrimaryTooltipForUpgradePreview(
                        controller,
                        card,
                        tooltipData,
                        tooltipParent
                    );
                    yield break;
                }

                yield return null;
            }

            BppLog.Info(
                "TooltipPreview",
                $"UpgradeScheduleTimedOut card={DescribeCard(card)}"
            );
        }
        finally
        {
            PendingControllers.Remove(controller);
        }
    }

    private static void RefreshPrimaryTooltipForUpgradePreview(
        CardController controller,
        Card card,
        CardTooltipData tooltipData,
        TooltipParentComponent tooltipParent
    )
    {
        if (controller == null || tooltipParent == null)
            return;

        if (tooltipParent.GetCardTooltipController(card) == null)
            return;

        if (
            controller == null
            || controller.CardData != card
            || !BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview)
        )
        {
            return;
        }

        tooltipParent.DisplayUpgradeTooltips(
            controller.transform,
            controller.TooltipOffset,
            new CardTooltipData(card, tooltipData.CardTemplate)
        );
    }

    private static string DescribeCard(Card? card)
    {
        if (card == null)
            return "null";

        var templateName = card.Template?.InternalName;
        if (!string.IsNullOrWhiteSpace(templateName))
            return templateName;

        return card.TemplateId.ToString();
    }
}

[HarmonyPatch(typeof(TooltipParentComponent), "DisplayUpgradeTooltips")]
internal static class UpgradePreviewTooltipDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(ITooltipData tooltipData)
    {
        if (tooltipData is not CardTooltipData cardTooltipData)
            return;

        BppLog.Info(
            "TooltipPreview",
            $"DisplayUpgradeTooltips card={DescribeCard(cardTooltipData.CardInstance)}"
        );
    }

    [HarmonyPatch(typeof(TooltipParentComponent), "HandleUpgradePreview")]
    [HarmonyPostfix]
    private static void HandleUpgradePreviewPostfix(
        TooltipParentComponent __instance,
        ITooltipData tooltipData
    )
    {
        if (tooltipData is not CardTooltipData cardTooltipData)
            return;

        var primary = Traverse
            .Create(__instance)
            .Property("CardTooltipController")
            .GetValue<CardTooltipController>();
        var secondary = Traverse
            .Create(__instance)
            .Property("SecondaryCardTooltipController")
            .GetValue<CardTooltipController>();

        BppLog.Info(
            "TooltipPreview",
            $"HandleUpgradePreview card={DescribeCard(cardTooltipData.CardInstance)} primaryMode={DescribeDisplayMode(primary)} primaryCard={DescribeTooltipCard(primary)} secondaryMode={DescribeDisplayMode(secondary)} secondaryCard={DescribeTooltipCard(secondary)}"
        );
    }

    private static string DescribeTooltipCard(CardTooltipController? controller)
    {
        if (controller == null)
            return "null";

        var currentCard = controller.CurrentCard;
        return DescribeCard(currentCard);
    }

    private static string DescribeDisplayMode(CardTooltipController? controller)
    {
        return controller == null ? "null" : controller.GetDisplayMode().ToString();
    }

    private static string DescribeCard(Card? card)
    {
        if (card == null)
            return "null";

        var templateName = card.Template?.InternalName;
        if (!string.IsNullOrWhiteSpace(templateName))
            return templateName;

        return card.TemplateId.ToString();
    }
}
