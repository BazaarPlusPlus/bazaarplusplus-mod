using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Input;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class TooltipModifierRefreshController : MonoBehaviour
{
    private enum TooltipModifierMode
    {
        Normal,
        Enchant,
        Upgrade,
    }

    private TooltipModifierMode _lastMode;

    private void Update()
    {
        var mode = GetCurrentMode();
        if (mode == _lastMode)
            return;

        _lastMode = mode;
        TryRefreshHoveredCardTooltip();
    }

    private static TooltipModifierMode GetCurrentMode()
    {
        if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview))
            return TooltipModifierMode.Upgrade;

        var alwaysShowEnchant = BppRuntimeHost.Config.EnchantPreviewAlwaysShowConfig?.Value ?? true;
        if (alwaysShowEnchant || BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview))
            return TooltipModifierMode.Enchant;

        return TooltipModifierMode.Normal;
    }

    private static void TryRefreshHoveredCardTooltip()
    {
        var tooltipParent = Data.TooltipParentComponent;
        var lookup = Data.CardAndSkillLookup;
        if (tooltipParent == null || lookup == null)
            return;

        if (tooltipParent.HasAnyLockedTooltipControllers())
            return;

        foreach (var controller in lookup.CardControllerDictionary.Values)
        {
            if (controller == null || controller.CardData == null)
                continue;

            var card = controller.CardData;
            if (card is not ItemCard)
                continue;

            if (!controller.IsCursorOverCard && !controller.IsHovering)
                continue;

            var tooltipController = tooltipParent.GetCardTooltipController(card);
            if (tooltipController == null)
                continue;

            var tooltipData = controller.GetTooltipData();
            if (tooltipData is not CardTooltipData cardTooltipData)
                continue;

            tooltipParent.HideCardTooltipController();
            tooltipParent.ShowCardTooltipController(
                controller.transform,
                controller.TooltipOffset,
                cardTooltipData
            );

            UpgradePreviewTooltipPatch.TryScheduleUpgradeTooltip(controller, cardTooltipData);

            return;
        }
    }
}
