#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class TooltipPreviewTargetResolver
{
    internal readonly struct TooltipRefreshTarget
    {
        public TooltipRefreshTarget(CardController controller, ItemCard card, CardTooltipData tooltipData)
        {
            Controller = controller;
            Card = card;
            TooltipData = tooltipData;
        }

        public CardController Controller { get; }

        public ItemCard Card { get; }

        public CardTooltipData TooltipData { get; }
    }

    internal static bool TryResolveCurrentPrimaryItemTooltip(
        TooltipParentComponent tooltipParent,
        out TooltipRefreshTarget target
    )
    {
        target = default;

        if (tooltipParent == null || Data.CardAndSkillLookup == null)
            return false;

        var primaryController = Traverse
            .Create(tooltipParent)
            .Property("CardTooltipController")
            .GetValue<CardTooltipController>();
        if (primaryController == null)
            return false;

        var tooltipData = TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
            primaryController.CurrentTooltipData,
            primaryController.CurrentCard
        );
        if (tooltipData?.CardInstance is not ItemCard itemCard)
            return false;

        var cardController = Data.CardAndSkillLookup.GetCardController(itemCard);
        if (cardController?.CardData is not ItemCard controllerItemCard)
            return false;

        if (!TooltipPreviewTargetSelection.AreSameCard(controllerItemCard, itemCard))
            return false;

        target = new TooltipRefreshTarget(cardController, controllerItemCard, tooltipData);
        return true;
    }

    internal static Card? TryResolveCurrentPrimaryCard(TooltipParentComponent tooltipParent)
    {
        if (tooltipParent == null)
            return null;

        var primaryController = Traverse
            .Create(tooltipParent)
            .Property("CardTooltipController")
            .GetValue<CardTooltipController>();

        return primaryController?.CurrentCard;
    }
}
