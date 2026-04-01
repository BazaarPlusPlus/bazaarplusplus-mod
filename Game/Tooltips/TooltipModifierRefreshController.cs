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
        TryRefreshCurrentItemTooltip();
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

    private static void TryRefreshCurrentItemTooltip()
    {
        var tooltipParent = Data.TooltipParentComponent;
        if (tooltipParent == null)
        {
            BppLog.Info(
                "TooltipPreview",
                "RefreshSkipped reason=no-tooltip-parent"
            );
            return;
        }

        if (tooltipParent.HasAnyLockedTooltipControllers())
        {
            BppLog.Info("TooltipPreview", "RefreshSkipped reason=tooltip-locked");
            return;
        }

        if (!TooltipPreviewTargetResolver.TryResolveCurrentPrimaryItemTooltip(tooltipParent, out var target))
        {
            BppLog.Info(
                "TooltipPreview",
                "RefreshSkipped reason=no-active-primary-item-tooltip"
            );
            return;
        }

        BppLog.Info(
            "TooltipPreview",
            $"PrimaryTooltipFound card={DescribeCard(target.Card)}"
        );

        var refreshedTooltipData = new CardTooltipData(
            target.Card,
            target.TooltipData.CardTemplate
        );
        tooltipParent.HideCardTooltipController();
        tooltipParent.ShowCardTooltipController(
            target.Controller.transform,
            target.Controller.TooltipOffset,
            refreshedTooltipData
        );

        var scheduled = UpgradePreviewTooltipPatch.TryScheduleUpgradeTooltip(
            target.Controller,
            refreshedTooltipData
        );
        BppLog.Info(
            "TooltipPreview",
            $"UpgradeScheduleAttempted card={DescribeCard(target.Card)} scheduled={scheduled}"
        );

        return;
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
