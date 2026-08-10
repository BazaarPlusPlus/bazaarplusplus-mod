#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactMetricFormatter
{
    private const long Billion = 1_000_000_000;
    private const long Million = 1_000_000;

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
                $"{trigger.Entity.Name.Replace('\n', ' ')} ×{trigger.ObservedActivationBatchCount}"
            )
        );
    }

    internal static string Group(
        CombatImpactGroup group,
        bool chinese,
        string? criticalMarker = null,
        string? totalMarker = null
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
            parts.Add(
                string.IsNullOrWhiteSpace(totalMarker)
                    ? chinese
                        ? $"总计 {value}"
                        : $"{value} total"
                    : $"{totalMarker}{value}"
            );
        }
        else
        {
            if (group.ObservedValue.HasValue)
            {
                parts.Add(
                    Value(group.ObservedValue.Value, group.Unit, ShouldShowSign(group), chinese)
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
                        : $"{damageMarker}{amount}"
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
                    : $"{shieldMarker}{amount}"
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

        var value = Value(target.ObservedValue.Value, target.Unit, ShouldShowSign(group), chinese);
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
            var value = Value(
                group.ObservedValue.Value,
                group.Unit,
                ShouldShowSign(group),
                chinese
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

        var critical = $"{criticalMarker}{criticalCount}";
        return chinese ? $"{baseCount}（{critical}）" : $"{baseCount} ({critical})";
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

        var value = Value(source.ObservedValue.Value, source.Unit, ShouldShowSign(group), chinese);
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

    private static string Duration(int milliseconds, string sign)
    {
        var seconds = Number(milliseconds / 1000m);
        return $"{sign}{seconds}s";
    }

    private static string Number(decimal value) =>
        decimal.Truncate(value) == value
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Integer(long value)
    {
        if (value is <= -Billion or >= Billion)
            return ScaledInteger(value, Billion, "B");
        if (value is <= -Million or >= Million)
            return ScaledInteger(value, Million, "M");
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string ScaledInteger(long value, long scale, string suffix) =>
        $"{((decimal)value / scale).ToString("0.##", CultureInfo.InvariantCulture)}{suffix}";

    private static string PeriodicAmount(int value) => Integer(value);
}
