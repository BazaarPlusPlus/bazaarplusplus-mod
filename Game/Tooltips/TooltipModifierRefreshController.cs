#nullable enable
using System;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Input;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.Tooltips;

internal sealed class TooltipModifierRefreshController : MonoBehaviour
{
    private enum TooltipModifierMode
    {
        Normal,
        Enchant,
        Upgrade,
    }

    private TooltipModifierMode _lastMode;
    private IBppConfig? _config;

    internal void Initialize(IBppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    private void Update()
    {
        var mode = GetCurrentMode();
        if (mode == _lastMode)
            return;

        _lastMode = mode;
        BppLog.Info("TooltipPreview", $"ModeChanged mode={mode}");
        TryRefreshHoveredCardTooltip();
    }

    private TooltipModifierMode GetCurrentMode()
    {
        if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview))
            return TooltipModifierMode.Upgrade;

        var alwaysShowEnchant = _config?.EnchantPreviewAlwaysShowConfig?.Value ?? true;
        if (alwaysShowEnchant || BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview))
            return TooltipModifierMode.Enchant;

        return TooltipModifierMode.Normal;
    }

    private static void TryRefreshHoveredCardTooltip()
    {
        var tooltipParent = Data.TooltipParentComponent;
        var lookup = Data.CardAndSkillLookup;
        if (tooltipParent == null || lookup == null)
        {
            BppLog.Info(
                "TooltipPreview",
                $"RefreshSkipped reason={(tooltipParent == null ? "no-tooltip-parent" : "no-card-lookup")}"
            );
            return;
        }

        if (tooltipParent.HasAnyLockedTooltipControllers())
        {
            BppLog.Info("TooltipPreview", "RefreshSkipped reason=tooltip-locked");
            return;
        }

        foreach (var controller in lookup.CardControllerDictionary.Values)
        {
            if (controller == null || controller.CardData == null)
                continue;

            var card = controller.CardData;
            if (card is not ItemCard)
                continue;

            if (!controller.IsCursorOverCard && !controller.IsHovering)
                continue;

            BppLog.Info(
                "TooltipPreview",
                $"HoveredItemFound card={DescribeCard(card)} hovered={(controller.IsCursorOverCard ? "cursor" : "hover")}"
            );
            var tooltipController = tooltipParent.GetCardTooltipController(card);
            if (tooltipController == null)
            {
                BppLog.Info(
                    "TooltipPreview",
                    $"PrimaryTooltipMissing card={DescribeCard(card)}"
                );
                continue;
            }

            BppLog.Info(
                "TooltipPreview",
                $"PrimaryTooltipFound card={DescribeCard(card)}"
            );

            var tooltipData = controller.GetTooltipData();
            if (tooltipData is not CardTooltipData cardTooltipData)
            {
                BppLog.Info(
                    "TooltipPreview",
                    $"RefreshSkipped card={DescribeCard(card)} reason=non-card-tooltip-data"
                );
                continue;
            }

            var refreshedTooltipData = new CardTooltipData(card, cardTooltipData.CardTemplate);
            tooltipParent.HideCardTooltipController();
            tooltipParent.ShowCardTooltipController(
                controller.transform,
                controller.TooltipOffset,
                refreshedTooltipData
            );

            var scheduled = UpgradePreviewTooltipPatch.TryScheduleUpgradeTooltip(
                controller,
                refreshedTooltipData
            );
            BppLog.Info(
                "TooltipPreview",
                $"UpgradeScheduleAttempted card={DescribeCard(card)} scheduled={scheduled}"
            );

            return;
        }

        BppLog.Info("TooltipPreview", "RefreshSkipped reason=no-hovered-item");
    }

    private static string DescribeCard(Card card)
    {
        if (card == null)
            return "null";

        var templateName = card.Template?.InternalName;
        if (!string.IsNullOrWhiteSpace(templateName))
            return templateName;

        return card.TemplateId.ToString();
    }
}
