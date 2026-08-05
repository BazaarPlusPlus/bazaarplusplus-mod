#nullable enable
using System.Runtime.CompilerServices;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class UpgradePreviewValueRegistry
{
    private static readonly ConditionalWeakTable<
        CardTooltipData,
        UpgradePreviewValueProjection
    > Projections = new();

    internal static void Register(
        CardTooltipData tooltipData,
        UpgradePreviewValueProjection projection
    ) => Projections.Add(tooltipData, projection);

    internal static bool TryResolveNextValue(
        CardTooltipData tooltipData,
        ITooltipComponent? component,
        out float value
    )
    {
        value = default;
        return tooltipData != null
            && Projections.TryGetValue(tooltipData, out var projection)
            && projection.TryResolve(component, out value);
    }

    internal static bool TryResolveEffectiveCooldowns(
        CardTooltipData tooltipData,
        out float currentSeconds,
        out float upgradedSeconds
    )
    {
        currentSeconds = default;
        upgradedSeconds = default;
        return tooltipData != null
            && Projections.TryGetValue(tooltipData, out var projection)
            && projection.TryResolveEffectiveCooldowns(out currentSeconds, out upgradedSeconds);
    }

    internal static string Format(float value) =>
        value.IsDecimal() ? value.GetDecimalValueString() : value.ToString();

    internal static bool HaveSameFormattedValue(float currentValue, float projectedValue) =>
        string.Equals(Format(currentValue), Format(projectedValue), StringComparison.Ordinal);

    internal static float ConvertProjectedValueToRenderedUnits(
        ECardAttributeType attributeType,
        ECardAttributeType? styleAsAttribute,
        float projectedValue
    ) =>
        (
            styleAsAttribute?.RequiresConversionToSeconds()
            ?? attributeType.RequiresConversionToSeconds()
        )
            ? TooltipExtensions.MillisecondsToSeconds(projectedValue)
            : projectedValue;
}
