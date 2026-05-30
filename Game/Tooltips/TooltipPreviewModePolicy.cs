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
        // Upgrade preview is hold-Shift only — it has no visibility mode of its own.
        if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview))
            return TooltipPreviewMode.Upgrade;
        if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview))
            return TooltipPreviewMode.Enchant;

        var enchantMode = config?.EnchantPreviewModeConfig?.Value ?? DefaultMode;
        if (enchantMode == PreviewVisibilityMode.Always)
            return TooltipPreviewMode.Enchant;
        if (enchantMode != PreviewVisibilityMode.AutoOnPedestalChoice)
            return TooltipPreviewMode.Normal;

        var kind =
            encounterState?.GetCurrent().ChoiceScreenPedestalKind ?? ChoiceScreenPedestalKind.None;
        return kind == ChoiceScreenPedestalKind.Enchant
            ? TooltipPreviewMode.Enchant
            : TooltipPreviewMode.Normal;
    }
}
