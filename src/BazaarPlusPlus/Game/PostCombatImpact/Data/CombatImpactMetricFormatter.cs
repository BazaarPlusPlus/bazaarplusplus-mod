#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactMetricFormatter
{
    internal static string CausedSummary(CombatImpactSource source, bool chinese)
    {
        var isSkill = string.Equals(
            source.Entity.TypeLabel,
            "Skill",
            StringComparison.OrdinalIgnoreCase
        );
        if (isSkill || source.UseCount <= 0)
            return string.Empty;

        return chinese
            ? $"使用 {source.UseCount} 次"
            : $"{source.UseCount} use{(source.UseCount == 1 ? string.Empty : "s")}";
    }

    internal static string TriggerSources(CombatImpactGroup group, bool chinese)
    {
        var sources = TriggerSourceValues(group);
        if (string.IsNullOrWhiteSpace(sources))
            return string.Empty;

        return chinese
            ? $"{TriggerSourceLabel(chinese)}{sources}"
            : $"{TriggerSourceLabel(chinese)} {sources}";
    }

    internal static string TriggerSourceLabel(bool chinese) =>
        chinese ? "触发来源：" : "Triggered by:";

    internal static string TriggerSourceValues(CombatImpactGroup group)
    {
        if (
            group.TriggerPresentationState
                is CombatImpactTriggerPresentationState.None
                    or CombatImpactTriggerPresentationState.HiddenSelfOnly
            || group.TriggerSources.Count == 0
        )
            return string.Empty;

        return string.Join(
            " · ",
            group.TriggerSources.Select(trigger =>
                $"{trigger.Entity.Name.Replace('\n', ' ')} ×{trigger.ApplicationCount}"
            )
        );
    }

    internal static string Group(
        CombatImpactGroup group,
        bool chinese,
        string? criticalMarker = null
    )
    {
        var parts = new List<string>();
        if (group.Count > 0 && ShouldShowCount(group.Kind, group.Surface, group.OccurrenceBasis))
            parts.Add(Count(group.Count, group.CriticalCount, chinese, criticalMarker));

        var authoritative = group.AuthoritativeMetric;
        if (authoritative?.Basis == CombatImpactAuthoritativeBasis.TotalAmount)
        {
            var value = Value(
                authoritative.Value,
                authoritative.Unit,
                showSign: ShouldShowSign(group),
                chinese
            );
            parts.Add(chinese ? $"总计 {value}" : $"{value} total");
        }
        else
        {
            if (group.ObservedValue.HasValue)
            {
                parts.Add(
                    ObservedValue(
                        group.ObservedValue.Value,
                        group.Unit,
                        group.ObservedCoverage,
                        ShouldShowSign(group),
                        chinese,
                        group.AmountLedger.ValuedApplicationCount,
                        group.AmountLedger.TotalApplicationCount
                    )
                );
            }

            if (
                authoritative?.Basis == CombatImpactAuthoritativeBasis.ApplicationCount
                && (group.Count == 0 || authoritative.Value != group.Count)
            )
            {
                parts.Add(
                    authoritative.CanReconcileApplicationCount
                        ? chinese
                            ? $"生效 {authoritative.Value} 次"
                            : $"{authoritative.Value} application{(authoritative.Value == 1 ? string.Empty : "s")}"
                        : chinese
                            ? $"影响 {authoritative.Value} 张卡牌"
                            : $"{authoritative.Value} card{(authoritative.Value == 1 ? string.Empty : "s")} affected"
                );
            }
        }

        return string.Join(" · ", parts);
    }

    internal static string PeriodicImpact(
        CombatImpactGroup group,
        bool chinese,
        string? damageMarker = null,
        string? shieldMarker = null
    )
    {
        var impact = group.PeriodicImpact;
        if (impact == null)
            return string.Empty;

        var parts = new List<string>();
        if (impact.HealthAmount > 0)
        {
            var amount = PeriodicAmount(impact.HealthAmount);
            var isRegen =
                group.Kind == CombatImpactKind.AttributeChange
                && group.NativeAttributeKey == "RegenApplyAmount";
            parts.Add(
                isRegen
                    ? chinese
                        ? $"治疗 {amount}"
                        : $"{amount} healed"
                    : string.IsNullOrWhiteSpace(damageMarker)
                        ? chinese
                            ? $"伤害 {amount}"
                            : $"{amount} dmg"
                        : $"{amount} {damageMarker}"
            );
        }

        if (impact.ShieldAmount > 0)
        {
            var amount = PeriodicAmount(impact.ShieldAmount);
            parts.Add(
                string.IsNullOrWhiteSpace(shieldMarker)
                    ? chinese
                        ? $"耗盾 {amount}"
                        : $"{amount} shield consumed"
                    : $"{amount} {shieldMarker}"
            );
        }

        return string.Join(" · ", parts);
    }

    internal static string Target(CombatImpactGroup group, CombatImpactTarget target, bool chinese)
    {
        var count = $"×{target.Count}";
        if (!target.ObservedValue.HasValue)
            return ShouldShowCount(group.Kind, group.Surface, group.OccurrenceBasis)
                ? count
                : string.Empty;

        var value = ObservedValue(
            target.ObservedValue.Value,
            target.Unit,
            target.ObservedCoverage,
            ShouldShowSign(group),
            chinese,
            target.ValuedApplicationCount,
            target.Count
        );
        return ShouldShowCount(group.Kind, group.Surface, group.OccurrenceBasis)
            ? $"{count} · {value}"
            : value;
    }

    internal static string IncomingGroup(
        CombatImpactIncomingGroup group,
        bool chinese,
        string? criticalMarker = null
    )
    {
        var parts = new List<string>();
        if (group.Count > 0 && ShouldShowCount(group.Kind, group.Surface, group.OccurrenceBasis))
            parts.Add(Count(group.Count, group.CriticalCount, chinese, criticalMarker));
        if (group.ObservedValue.HasValue)
        {
            var value = ObservedValue(
                group.ObservedValue.Value,
                group.Unit,
                group.ObservedCoverage,
                ShouldShowSign(group),
                chinese,
                group.ValuedApplicationCount,
                group.Count
            );
            parts.Add(value);
        }

        return string.Join(" · ", parts);
    }

    private static bool ShouldShowCount(
        CombatImpactKind kind,
        CombatImpactEventSurface surface,
        CombatImpactOccurrenceBasis occurrenceBasis
    ) =>
        kind != CombatImpactKind.AttributeChange
        || surface == CombatImpactEventSurface.AppliedEffect
        || occurrenceBasis == CombatImpactOccurrenceBasis.ExplicitExecution;

    private static bool ShouldShowSign(CombatImpactGroup group) =>
        ShouldShowSign(group.Kind, group.Surface, group.NativeAttributeKey);

    private static bool ShouldShowSign(CombatImpactIncomingGroup group) =>
        ShouldShowSign(group.Kind, group.Surface, group.NativeAttributeKey);

    private static bool ShouldShowSign(
        CombatImpactKind kind,
        CombatImpactEventSurface surface,
        string nativeAttributeKey
    ) =>
        kind == CombatImpactKind.AttributeChange
        && !(
            surface == CombatImpactEventSurface.AppliedEffect
            && nativeAttributeKey
                is "RegenApplyAmount"
                    or "TempoApplyAmount"
                    or "TempoRemoveAmount"
                    or "BurnRemoveAmount"
                    or "PoisonRemoveAmount"
                    or "RegenRemoveAmount"
                    or "ShieldRemoveAmount"
                    or "RageRemoveAmount"
        );

    private static string Count(int count, int criticalCount, bool chinese, string? criticalMarker)
    {
        var baseCount = $"×{count}";
        if (criticalCount <= 0 || string.IsNullOrWhiteSpace(criticalMarker))
            return baseCount;
        return chinese
            ? $"{baseCount}（{criticalCount}{criticalMarker}）"
            : $"{baseCount} ({criticalCount}{criticalMarker})";
    }

    internal static string IncomingSource(
        CombatImpactIncomingGroup group,
        CombatImpactIncomingSource source,
        bool chinese
    )
    {
        var count = $"×{source.Count}";
        if (!source.ObservedValue.HasValue)
            return ShouldShowCount(group.Kind, group.Surface, group.OccurrenceBasis)
                ? count
                : string.Empty;

        var value = ObservedValue(
            source.ObservedValue.Value,
            source.Unit,
            source.ObservedCoverage,
            ShouldShowSign(group),
            chinese,
            source.ValuedApplicationCount,
            source.Count
        );
        return ShouldShowCount(group.Kind, group.Surface, group.OccurrenceBasis)
            ? $"{count} · {value}"
            : value;
    }

    internal static string Value(
        int value,
        CombatImpactValueUnit unit,
        bool showSign,
        bool chinese = false
    )
    {
        var sign = showSign && value >= 0 ? "+" : string.Empty;
        return unit switch
        {
            CombatImpactValueUnit.Milliseconds => Duration(value, sign),
            CombatImpactValueUnit.PercentagePoints => $"{sign}{Integer(value)}%",
            CombatImpactValueUnit.Applications => Integer(value),
            _ => $"{sign}{Integer(value)}",
        };
    }

    private static string ObservedValue(
        int value,
        CombatImpactValueUnit unit,
        CombatImpactCoverage coverage,
        bool showSign,
        bool chinese,
        int valuedApplicationCount = 0,
        int totalApplicationCount = 0
    )
    {
        var formatted = Value(value, unit, showSign, chinese);
        return coverage switch
        {
            CombatImpactCoverage.LowerBound => chinese
                ? $"至少 {formatted}"
                : $"at least {formatted}",
            CombatImpactCoverage.Partial
                when valuedApplicationCount > 0
                    && totalApplicationCount >= valuedApplicationCount => chinese
                ? $"已记录 {formatted}（{valuedApplicationCount}/{totalApplicationCount}）"
                : $"{formatted} recorded ({valuedApplicationCount}/{totalApplicationCount})",
            CombatImpactCoverage.Partial => chinese
                ? $"已记录 {formatted}"
                : $"{formatted} recorded",
            _ => formatted,
        };
    }

    private static string Duration(int milliseconds, string sign)
    {
        var seconds = Number(milliseconds / 1000m);
        return $"{sign}{seconds}s";
    }

    private static string Number(decimal value) =>
        decimal.Truncate(value) == value
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Integer(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Integer(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string PeriodicAmount(int value) => Integer(value);
}
