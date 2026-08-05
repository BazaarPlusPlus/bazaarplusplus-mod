#nullable enable
using System.Reflection;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Tooltips;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Patches.Tooltips;

[HarmonyPatch]
internal static class UpgradePreviewValuePatch
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method(
            typeof(CardTooltipData),
            "RenderStylizedAttributeValue",
            [
                typeof(ECardAttributeType),
                typeof(float),
                typeof(ECardAttributeType?),
                typeof(ITooltipComponent),
            ]
        );

    [HarmonyPostfix]
    private static void Postfix(
        CardTooltipData __instance,
        ref ECardAttributeType attributeType,
        ref float value,
        ref ECardAttributeType? styleAsAttribute,
        ITooltipComponent? component,
        ref (string? tooltipSegment, bool renderedWithIcon) __result
    )
    {
        if (
            !UpgradePreviewValueRegistry.TryResolveNextValue(
                __instance,
                component,
                out var nextValue
            )
        )
            return;

        // The native method mutates its value parameter into display units before this
        // postfix runs. Only the independently projected value is still in raw units.
        var currentValue = value;
        nextValue = UpgradePreviewValueRegistry.ConvertProjectedValueToRenderedUnits(
            attributeType,
            styleAsAttribute,
            nextValue
        );

        var currentText = UpgradePreviewValueRegistry.Format(currentValue);
        if (UpgradePreviewValueRegistry.HaveSameFormattedValue(currentValue, nextValue))
        {
            __result = styleAsAttribute.HasValue
                ? Data.TooltipTypography.GetAttributeStringWithIcon(
                    styleAsAttribute.Value,
                    currentText
                )
                : (currentText, false);
            return;
        }

        __result = Data.TooltipTypography.GetFusionString(
            styleAsAttribute,
            currentText,
            UpgradePreviewValueRegistry.Format(nextValue)
        );
    }
}
