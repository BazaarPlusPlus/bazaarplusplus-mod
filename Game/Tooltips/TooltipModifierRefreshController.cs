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
using UnityEngine.InputSystem;

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
    private bool? _lastCtrlRaw;
    private bool? _lastShiftRaw;

    private void Awake()
    {
        BppLog.Info("TooltipPreview", "Controller Awake");
    }

    private void OnEnable()
    {
        BppLog.Info("TooltipPreview", "Controller OnEnable");
    }

    private void Start()
    {
        BppLog.Info("TooltipPreview", "Controller Start");
    }

    internal void Initialize(IBppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        BppLog.Info(
            "TooltipPreview",
            $"Initialized alwaysShowEnchant={(_config.EnchantPreviewAlwaysShowConfig?.Value ?? true)}"
        );
    }

    private void Update()
    {
        ReportRawModifierState();

        var mode = GetCurrentMode();
        if (mode == _lastMode)
            return;

        _lastMode = mode;
        ReportModeChange(mode);
        TryRefreshHoveredCardTooltip(mode);
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

    private void ReportRawModifierState()
    {
        var keyboard = Keyboard.current;
        var ctrlRaw =
            keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        var shiftRaw =
            keyboard != null
            && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

        if (_lastCtrlRaw == ctrlRaw && _lastShiftRaw == shiftRaw)
            return;

        _lastCtrlRaw = ctrlRaw;
        _lastShiftRaw = shiftRaw;

        BppLog.Info(
            "TooltipPreview",
            $"PreviewRaw ctrl={(ctrlRaw ? "down" : "up")} shift={(shiftRaw ? "down" : "up")} mode={GetCurrentMode()}"
        );
    }

    private static void ReportModeChange(TooltipModifierMode mode)
    {
        var keyboard = Keyboard.current;
        var ctrlRaw =
            keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        var shiftRaw =
            keyboard != null
            && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        var enchantHeld = BppHotkeyService.IsHeld(
            BppHotkeyActionId.HoldEnchantPreview,
            keyboard: keyboard
        );
        var upgradeHeld = BppHotkeyService.IsHeld(
            BppHotkeyActionId.HoldUpgradePreview,
            keyboard: keyboard
        );
        var message =
            $"PreviewInput mode={mode} enchant={BppHotkeyService.GetBindingDisplay(BppHotkeyActionId.HoldEnchantPreview)}:{(enchantHeld ? "held" : "up")} upgrade={BppHotkeyService.GetBindingDisplay(BppHotkeyActionId.HoldUpgradePreview)}:{(upgradeHeld ? "held" : "up")} rawCtrl={(ctrlRaw ? "down" : "up")} rawShift={(shiftRaw ? "down" : "up")}";

        BppLog.Info("TooltipPreview", message);
    }

    private static void TryRefreshHoveredCardTooltip(TooltipModifierMode mode)
    {
        var tooltipParent = Data.TooltipParentComponent;
        var lookup = Data.CardAndSkillLookup;
        if (tooltipParent == null || lookup == null)
        {
            BppLog.Info(
                "TooltipPreview",
                $"RefreshSkipped mode={mode} reason={(tooltipParent == null ? "no-tooltip-parent" : "no-card-lookup")}"
            );
            return;
        }

        if (tooltipParent.HasAnyLockedTooltipControllers())
        {
            BppLog.Info("TooltipPreview", $"RefreshSkipped mode={mode} reason=tooltip-locked");
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

            var tooltipController = tooltipParent.GetCardTooltipController(card);
            if (tooltipController == null)
            {
                BppLog.Info(
                    "TooltipPreview",
                    $"RefreshSkipped mode={mode} card={DescribeCard(card)} reason=no-tooltip-controller"
                );
                continue;
            }

            var tooltipData = controller.GetTooltipData();
            if (tooltipData is not CardTooltipData cardTooltipData)
            {
                BppLog.Info(
                    "TooltipPreview",
                    $"RefreshSkipped mode={mode} card={DescribeCard(card)} reason=non-card-tooltip-data"
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

            UpgradePreviewTooltipPatch.TryScheduleUpgradeTooltip(controller, refreshedTooltipData);
            BppLog.Info(
                "TooltipPreview",
                $"RefreshApplied mode={mode} card={DescribeCard(card)} hovered={(controller.IsCursorOverCard ? "cursor" : "hover")}"
            );

            return;
        }

        BppLog.Info("TooltipPreview", $"RefreshSkipped mode={mode} reason=no-hovered-item");
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
