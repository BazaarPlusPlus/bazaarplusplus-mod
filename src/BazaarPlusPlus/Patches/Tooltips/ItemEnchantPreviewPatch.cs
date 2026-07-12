#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

// Owns every BPP text section rendered beneath the native passive-effect block.
// One postfix avoids cross-feature HideAll/order races on this pooled controller.
[HarmonyPatch(
    typeof(CardTooltipController),
    nameof(CardTooltipController.RenderPassiveEffectTextBlock)
)]
internal static class BppTooltipSectionRenderPatch
{
    internal const string EnchantWithNativeSectionKey = "enchant-preview-with-native";
    internal const string EnchantWithoutNativeSectionKey = "enchant-preview-without-native";

    private static readonly BppTooltipSections.Style EnchantWithNativeStyle = new()
    {
        SectionTopPaddingScale = 1f,
        SectionBottomPaddingScale = 1.75f,
        SourceBottomPaddingScale = 0.5f,
        ParagraphSpacing = 4f,
        FontScale = 1.2f,
        ShowNativeDivider = true,
        DividerHorizontalInset = 12f,
    };

    private static readonly BppTooltipSections.Style EnchantWithoutNativeStyle = new()
    {
        SectionTopPaddingScale = 1.25f,
        SectionBottomPaddingScale = 1.75f,
        ParagraphSpacing = 4f,
        FontScale = 1.2f,
    };

    [HarmonyPostfix]
    private static void Postfix(CardTooltipController __instance, string text)
    {
        try
        {
            BppTooltipSections.HideAll(__instance);
            TooltipLayerOverride.SetElevated(__instance, elevated: false);
            if (BppTooltipSectionResetState.IsResetting(__instance))
                return;

            var encounterContent = EncounterEventTooltipPatch.BuildContent(__instance);
            if (!string.IsNullOrEmpty(encounterContent))
            {
                BppTooltipSections.TryShow(
                    __instance,
                    EncounterEventTooltipPatch.SectionKey,
                    __instance.passiveEffectParent,
                    encounterContent!
                );
                return;
            }

            var enchantContent = BuildEnchantContent(__instance);
            if (string.IsNullOrEmpty(enchantContent))
                return;

            var hasNativePassiveText = !string.IsNullOrWhiteSpace(text);
            var sectionKey = hasNativePassiveText
                ? EnchantWithNativeSectionKey
                : EnchantWithoutNativeSectionKey;
            var sectionStyle = hasNativePassiveText
                ? EnchantWithNativeStyle
                : EnchantWithoutNativeStyle;

            if (
                BppTooltipSections.TryShow(
                    __instance,
                    sectionKey,
                    __instance.passiveEffectParent,
                    enchantContent!,
                    sectionStyle
                )
            )
                TooltipLayerOverride.SetElevated(__instance, elevated: true);
        }
        catch (Exception ex)
        {
            BppLog.Error("TooltipSection", "Failed to render BPP tooltip section", ex);
        }
    }

    private static string? BuildEnchantContent(CardTooltipController controller)
    {
        if (Data.IsInCombat || controller.CurrentTooltipData is not CardTooltipData tooltipData)
            return null;

        var services = BppPatchHost.Services;
        ChoicePedestalSnapshot? choicePedestal =
            TooltipPreviewModePolicy.ShouldReadChoicePedestal(services.Config)
                ? services.EncounterState?.GetChoicePedestal()
                : null;
        if (
            TooltipPreviewModePolicy.Resolve(services.Config, choicePedestal)
            != TooltipPreviewMode.Enchant
        )
            return null;

        var restrictTo = TooltipPreviewModePolicy.ResolveEnchantRestriction(
            services.Config,
            choicePedestal
        );
        var segments = ItemEnchantPreviewService.BuildPreviewSegments(
            tooltipData.CardInstance,
            restrictTo
        );
        return ItemEnchantPreviewFormatting.BuildSectionText(segments);
    }
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ResetValues))]
internal static class BppTooltipSectionResetPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BppTooltipSectionResetState.Enter(__instance);

    [HarmonyPostfix]
    private static void Postfix(CardTooltipController __instance) =>
        BppTooltipSectionResetState.Exit(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ClearCurrentCard))]
internal static class BppTooltipSectionClearPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BppTooltipSectionLifecycle.Hide(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), "OnDisable")]
internal static class BppTooltipSectionDisablePatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BppTooltipSectionLifecycle.Hide(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.OnDestroy))]
internal static class BppTooltipSectionDestroyPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance)
    {
        BppTooltipSectionResetState.Exit(__instance);
        TooltipLayerOverride.SetElevated(__instance, elevated: false);
        BppTooltipSections.ReleaseAll(__instance);
    }
}

internal static class BppTooltipSectionLifecycle
{
    internal static void Hide(CardTooltipController controller)
    {
        BppTooltipSections.HideAll(controller);
        TooltipLayerOverride.SetElevated(controller, elevated: false);
    }
}

internal static class BppTooltipSectionResetState
{
    private static readonly HashSet<int> ResettingControllers = new();

    internal static void Enter(CardTooltipController controller) =>
        ResettingControllers.Add(controller.GetInstanceID());

    internal static void Exit(CardTooltipController controller) =>
        ResettingControllers.Remove(controller.GetInstanceID());

    internal static bool IsResetting(CardTooltipController controller) =>
        ResettingControllers.Contains(controller.GetInstanceID());
}
