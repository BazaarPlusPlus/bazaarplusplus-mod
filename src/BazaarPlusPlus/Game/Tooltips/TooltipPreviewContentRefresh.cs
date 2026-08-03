#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.GameInterop.Tooltips;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class TooltipPreviewContentRefresh
{
    internal static bool TryApply(
        CardController cardController,
        CardTooltipController tooltipController,
        ItemCard card,
        CardTooltipData currentTooltipData,
        TooltipPreviewMode mode,
        TooltipPreviewMode rollbackMode
    )
    {
        if (
            cardController == null
            || tooltipController == null
            || card == null
            || currentTooltipData == null
        )
            return false;

        var originalCanFuse = currentTooltipData.CanFuse;
        try
        {
            ApplyCardPreviewMode(cardController, card, rollbackMode, mode);
            var replacement = CardTooltipDataFactory.Create(
                card,
                currentTooltipData,
                ToRefreshMode(mode)
            );
            replacement.CanFuse = cardController.CanFuse();
            if (NativeCardTooltipContentRefresher.TryApply(tooltipController, replacement))
                return true;
        }
        catch
        {
            TryRestore(
                cardController,
                tooltipController,
                card,
                currentTooltipData,
                mode,
                rollbackMode,
                originalCanFuse
            );
            throw;
        }

        TryRestore(
            cardController,
            tooltipController,
            card,
            currentTooltipData,
            mode,
            rollbackMode,
            originalCanFuse
        );
        return false;
    }

    private static void TryRestore(
        CardController cardController,
        CardTooltipController tooltipController,
        ItemCard card,
        CardTooltipData tooltipData,
        TooltipPreviewMode currentMode,
        TooltipPreviewMode restoreMode,
        bool restoreCanFuse
    )
    {
        try
        {
            ApplyCardPreviewMode(cardController, card, currentMode, restoreMode);
            tooltipData.CanFuse = restoreCanFuse;
            NativeCardTooltipContentRefresher.TryApply(tooltipController, tooltipData);
        }
        catch
        {
            // Preserve the original refresh failure; the caller reports the degraded transition.
        }
    }

    private static void ApplyCardPreviewMode(
        CardController controller,
        ItemCard card,
        TooltipPreviewMode currentMode,
        TooltipPreviewMode nextMode
    )
    {
        if (currentMode == nextMode)
            return;

        if (!card.CanCardUpgrade())
            return;

        if (nextMode == TooltipPreviewMode.Upgrade)
            controller.EnterUpgradePreview();
        else if (currentMode == TooltipPreviewMode.Upgrade)
            controller.ExitUpgradePreview();
    }

    private static TooltipPreviewRefreshMode ToRefreshMode(TooltipPreviewMode mode) =>
        mode switch
        {
            TooltipPreviewMode.Enchant => TooltipPreviewRefreshMode.Enchant,
            TooltipPreviewMode.Upgrade => TooltipPreviewRefreshMode.Upgrade,
            _ => TooltipPreviewRefreshMode.Normal,
        };
}
