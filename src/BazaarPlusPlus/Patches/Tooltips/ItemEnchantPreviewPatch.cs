#nullable enable
#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

// Renders the enchant preview beneath the native passive-effect block.
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
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(
        CardTooltipController __instance,
        string text,
        List<CardQuestGroupData>? questData
    )
    {
        try
        {
            ItemEnchantPreviewTooltipLifecycle.Hide(__instance);

            // ResetValues is the only native caller that passes null quest data.
            // Bail out before reading CurrentTooltipData, which is still stale then.
            if (questData == null)
                return;

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
        ItemEnchantPreviewTooltipLifecycle.Hide(__instance);

    [HarmonyFinalizer]
    private static void Finalizer(CardTooltipController __instance) =>
        ItemEnchantPreviewTooltipLifecycle.Hide(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ClearCurrentCard))]
internal static class BppTooltipSectionClearPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        ItemEnchantPreviewTooltipLifecycle.Hide(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), "OnDisable")]
internal static class BppTooltipSectionDisablePatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        ItemEnchantPreviewTooltipLifecycle.Hide(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.OnDestroy))]
internal static class BppTooltipSectionDestroyPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance)
    {
        TooltipLayerOverride.SetElevated(__instance, elevated: false);
        BppTooltipSections.ReleaseAll(__instance);
    }
}

internal static class ItemEnchantPreviewTooltipLifecycle
{
    internal static void Hide(CardTooltipController controller)
    {
        BppTooltipSections.Hide(
            controller,
            BppTooltipSectionRenderPatch.EnchantWithNativeSectionKey
        );
        BppTooltipSections.Hide(
            controller,
            BppTooltipSectionRenderPatch.EnchantWithoutNativeSectionKey
        );
        TooltipLayerOverride.SetElevated(controller, elevated: false);
    }
}
