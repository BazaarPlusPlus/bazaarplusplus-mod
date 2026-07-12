#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.BilingualItemNames;
using BazaarPlusPlus.GameInterop.Localization;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using TheBazaar.Extensions;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

// Native CardTooltipTypeHandler renders the localized title before positioning the tooltip.
// Append the official zh-CN title at that seam so layout includes the subtitle and the active
// game locale never needs to be switched.
[HarmonyPatch(typeof(CardTooltipTypeHandler), "RenderCardUI")]
internal static class BilingualItemNamePatch
{
    [HarmonyPostfix]
    private static void Postfix(CardTooltipController controller, CardTooltipData tooltipData)
    {
        try
        {
            var card = tooltipData.CardInstance;
            var enabled =
                BppPatchHost.Services.Config.EnableBilingualItemNamesConfig?.Value ?? false;
            var currentLanguageIsChinese = LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode);
            if (!enabled || card?.Type != ECardType.Item || currentLanguageIsChinese)
                return;

            var chineseTitle = ChineseTranslationCatalog.TryResolve(
                tooltipData.CardTemplate.Localization?.Title
            );
            var title = BilingualItemNamePresentation.TryBuild(
                controller.headerText?.text,
                chineseTitle,
                enabled,
                isItem: true,
                currentLanguageIsChinese
            );
            if (title == null || controller.headerText == null)
                return;

            BppTmpFont.TryInstallSystemCjkFallback(controller.headerText, chineseTitle);
            controller.headerText.TrySetText(title);
        }
        catch (Exception ex)
        {
            BppLog.Error("BilingualNames", "Failed to append the Chinese item name", ex);
        }
    }
}
