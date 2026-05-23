#nullable enable
using System;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.Tooltips;

internal sealed class TooltipModifierRefreshController : MonoBehaviour
{
    private TooltipPreviewMode _lastMode;
    private IBppConfig? _config;
    private IEncounterStateProbe? _encounterState;

    internal void Initialize(IBppConfig config, IEncounterStateProbe encounterState)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _encounterState = encounterState ?? throw new ArgumentNullException(nameof(encounterState));
    }

    private void Update()
    {
        try
        {
            var mode = TooltipPreviewModePolicy.Resolve(_config, _encounterState);
            if (mode == _lastMode)
                return;

            _lastMode = mode;
            TryRefreshCurrentItemTooltip();
        }
        catch (Exception ex)
        {
            BppLog.Error("TooltipPreview", "Tooltip modifier update failed", ex);
        }
    }

    private static void TryRefreshCurrentItemTooltip()
    {
        var tooltipParent = Data.TooltipParentComponent;
        if (tooltipParent == null)
            return;

        if (tooltipParent.HasAnyLockedTooltipControllers())
            return;

        if (!TryResolveRefreshTarget(tooltipParent, out var target))
            return;

        var refreshedTooltipData = CardTooltipDataFactory.Create(target.Card, target.TooltipData);

        tooltipParent.HideCardTooltipController();
        tooltipParent.ShowCardTooltipController(
            target.Controller.transform,
            target.Controller.TooltipOffset,
            refreshedTooltipData
        );
        UpgradePreviewTooltipPatch.TryScheduleUpgradeTooltip(
            target.Controller,
            refreshedTooltipData
        );
    }

    private static bool TryResolveRefreshTarget(
        TooltipParentComponent tooltipParent,
        out TooltipPreviewTargetResolver.TooltipRefreshTarget target
    )
    {
        if (
            TooltipPreviewTargetResolver.TryResolveCurrentPrimaryItemTooltip(
                tooltipParent,
                out target
            )
        )
            return true;

        var lookup = Data.CardAndSkillLookup;
        if (lookup == null)
        {
            target = default;
            return false;
        }

        foreach (var controller in lookup.CardControllerDictionary.Values)
        {
            if (controller?.CardData is not ItemCard itemCard)
                continue;

            if (!controller.IsCursorOverCard && !controller.IsHovering)
                continue;

            if (tooltipParent.GetCardTooltipController(itemCard) == null)
                continue;

            if (controller.GetTooltipData() is not CardTooltipData tooltipData)
                continue;

            target = new TooltipPreviewTargetResolver.TooltipRefreshTarget(
                controller,
                itemCard,
                tooltipData
            );
            return true;
        }

        target = default;
        return false;
    }
}
