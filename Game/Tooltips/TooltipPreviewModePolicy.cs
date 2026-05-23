#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.Encounter;
using BazaarPlusPlus.Game.Input;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class TooltipPreviewModePolicy
{
    private const PreviewVisibilityMode DefaultMode = PreviewVisibilityMode.AutoOnPedestalChoice;

    internal static TooltipPreviewMode Resolve(
        IBppConfig? config,
        IEncounterStateProbe? encounterState
    )
    {
        if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview))
            return TooltipPreviewMode.Upgrade;
        if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview))
            return TooltipPreviewMode.Enchant;

        var upgradeMode = config?.UpgradePreviewModeConfig?.Value ?? DefaultMode;
        var enchantMode = config?.EnchantPreviewModeConfig?.Value ?? DefaultMode;

        if (upgradeMode == PreviewVisibilityMode.Always)
            return TooltipPreviewMode.Upgrade;
        if (enchantMode == PreviewVisibilityMode.Always)
            return TooltipPreviewMode.Enchant;

        var upgradeAuto = upgradeMode == PreviewVisibilityMode.AutoOnPedestalChoice;
        var enchantAuto = enchantMode == PreviewVisibilityMode.AutoOnPedestalChoice;
        if (!upgradeAuto && !enchantAuto)
            return TooltipPreviewMode.Normal;

        var kind =
            encounterState?.GetCurrent().ChoiceScreenPedestalKind ?? ChoiceScreenPedestalKind.None;
        if (kind == ChoiceScreenPedestalKind.Upgrade && upgradeAuto)
            return TooltipPreviewMode.Upgrade;
        if (kind == ChoiceScreenPedestalKind.Enchant && enchantAuto)
            return TooltipPreviewMode.Enchant;

        return TooltipPreviewMode.Normal;
    }
}
