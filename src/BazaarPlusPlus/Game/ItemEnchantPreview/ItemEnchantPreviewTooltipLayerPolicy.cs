#nullable enable
using System;
using BazaarPlusPlus.Infrastructure.UiTokens;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewTooltipLayerPolicy
{
    public const int ElevatedSortingOrder = BppOverlaySorting.NativeTooltipForeground;

    public static bool ShouldElevateForPassiveText(string? passiveTooltipText)
    {
        return !string.IsNullOrEmpty(passiveTooltipText)
            && passiveTooltipText.Contains(
                ItemEnchantPreviewFormatting.PreviewHeaderText,
                StringComparison.Ordinal
            );
    }
}
