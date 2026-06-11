#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using HarmonyLib;
using TheBazaar.UI.Tooltips;
using TMPro;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch(typeof(CardEffectTooltipController), "SetText")]
internal static class CardEffectTooltipFontPatch
{
    private const string Component = "TooltipFont";
    private static readonly FieldInfo? TextObjectField = AccessTools.Field(
        typeof(CardEffectTooltipController),
        "textObject"
    );

    [HarmonyPostfix]
    private static void Postfix(CardEffectTooltipController __instance, string newText)
    {
        try
        {
            TryRoute(__instance, newText);
        }
        catch (Exception ex)
        {
            BppLog.Debug(Component, $"Failed to route tooltip TMP font: {ex.Message}");
        }
    }

    internal static bool TryRoute(CardEffectTooltipController? controller, string? text)
    {
        if (controller == null || TextObjectField == null)
            return false;

        if (TextObjectField.GetValue(controller) is not TMP_Text textObject)
            return false;

        return BppTmpFont.TryApply(textObject, text);
    }
}
