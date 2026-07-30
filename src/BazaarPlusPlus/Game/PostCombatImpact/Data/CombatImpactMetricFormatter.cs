#nullable enable

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactMetricFormatter
{
    internal static string Group(CombatImpactGroup group, bool chinese)
    {
        var count = chinese ? $"{group.Count} 次" : $"{group.Count}×";
        return group.AggregateValue.HasValue
            ? $"{count} · {Value(group.AggregateValue.Value, group.Unit, group.ValueIsPartial, group.Kind == CombatImpactKind.AttributeChange)}"
            : count;
    }

    internal static string Target(CombatImpactKind kind, CombatImpactTarget target)
    {
        var count = $"×{target.Count}";
        return target.AggregateValue.HasValue
            ? $"{count} · {Value(target.AggregateValue.Value, target.Unit, target.ValueIsPartial, kind == CombatImpactKind.AttributeChange)}"
            : count;
    }

    internal static string Value(int value, CombatImpactValueUnit unit, bool partial, bool showSign)
    {
        var prefix = partial ? (showSign ? "≈" : "≥") : string.Empty;
        var sign = showSign && value >= 0 ? "+" : string.Empty;
        return unit switch
        {
            CombatImpactValueUnit.Milliseconds => Math.Abs(value) >= 1000
                ? $"{prefix}{sign}{value / 1000f:0.##}s"
                : $"{prefix}{sign}{value}ms",
            CombatImpactValueUnit.PercentagePoints =>
                $"{prefix}{(value >= 0 ? "+" : string.Empty)}{value}%",
            _ => $"{prefix}{sign}{value:N0}",
        };
    }
}
