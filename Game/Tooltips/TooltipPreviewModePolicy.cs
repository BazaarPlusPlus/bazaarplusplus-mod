#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
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
        ChoicePedestalSnapshot? choicePedestal = ShouldReadChoicePedestal(config)
            ? encounterState?.GetChoicePedestal()
            : null;
        return Resolve(config, choicePedestal);
    }

    internal static TooltipPreviewMode Resolve(
        IBppConfig? config,
        ChoicePedestalSnapshot? choicePedestal
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

        return choicePedestal?.Kind == ChoiceScreenPedestalKind.Enchant
            ? TooltipPreviewMode.Enchant
            : TooltipPreviewMode.Normal;
    }

    internal static IReadOnlyList<string>? ResolveEnchantRestriction(
        IBppConfig? config,
        ChoicePedestalSnapshot? choicePedestal
    )
    {
        if (!ShouldReadChoicePedestal(config))
            return null;
        return choicePedestal?.IsEnchantChoice == true
            ? choicePedestal.Value.EnchantmentTypeNames
            : null;
    }

    internal static bool ShouldReadChoicePedestal(IBppConfig? config)
    {
        if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview))
            return false;
        if (BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview))
            return false;
        return (config?.EnchantPreviewModeConfig?.Value ?? DefaultMode)
            == PreviewVisibilityMode.AutoOnPedestalChoice;
    }
}
