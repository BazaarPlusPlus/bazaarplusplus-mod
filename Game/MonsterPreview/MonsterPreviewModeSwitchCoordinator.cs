#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class MonsterPreviewModeSwitchCoordinator
{
    private static readonly System.Reflection.PropertyInfo? TooltipControllerProperty =
        AccessTools.Property(typeof(TooltipParentComponent), "CardTooltipController");

    private static readonly System.Reflection.PropertyInfo? CurrentCardProperty =
        AccessTools.Property(typeof(CardTooltipController), "CurrentCard");

    private static readonly System.Reflection.FieldInfo? CurrentCardField = AccessTools.Field(
        typeof(CardTooltipController),
        "_currentCard"
    );

    internal static void Apply(bool useNativePreview)
    {
        MonsterPreviewItemBoardRuntime.Instance?.HandlePreviewModeChanged();

        var tooltipParent = Data.TooltipParentComponent;
        var tooltipController = GetPrimaryTooltipController(tooltipParent);
        var wasLocked =
            tooltipParent != null
            && tooltipController != null
            && tooltipParent.IsCardTooltipControllerLocked(tooltipController);

        if (wasLocked)
            tooltipParent!.UnlockCardTooltipController();

        if (tooltipParent != null && tooltipController != null)
            tooltipParent.HideCardTooltipController();

        if (useNativePreview)
        {
            MonsterLockShowcaseRuntime.Instance?.HandlePreviewModeChanged(useNativePreview: true);
            return;
        }
    }

    private static CardTooltipController? GetPrimaryTooltipController(
        TooltipParentComponent? tooltipParent
    )
    {
        return tooltipParent != null
            ? TooltipControllerProperty?.GetValue(tooltipParent) as CardTooltipController
            : null;
    }

    private static Card? GetCurrentCard(CardTooltipController? tooltipController)
    {
        if (tooltipController == null)
            return null;

        return CurrentCardProperty?.GetValue(tooltipController) as Card
            ?? CurrentCardField?.GetValue(tooltipController) as Card;
    }
}
