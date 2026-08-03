#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactMetricFormatter
{
    internal static string CausedSummary(CombatImpactSource source, bool chinese)
    {
        var parts = new List<string>();
        var isSkill = string.Equals(
            source.Entity.TypeLabel,
            "Skill",
            StringComparison.OrdinalIgnoreCase
        );
        if (isSkill && source.TriggerCount > 0)
        {
            parts.Add(
                chinese
                    ? $"触发 {source.TriggerCount} 次"
                    : $"{source.TriggerCount} trigger{(source.TriggerCount == 1 ? string.Empty : "s")}"
            );
        }
        else if (!isSkill && source.UseCount > 0)
        {
            parts.Add(
                chinese
                    ? $"使用 {source.UseCount} 次"
                    : $"{source.UseCount} use{(source.UseCount == 1 ? string.Empty : "s")}"
            );
        }

        return string.Join(" · ", parts);
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

    internal static string TriggerSourceValues(CombatImpactGroup group) =>
        string.Join(
            " · ",
            group.TriggerSources.Select(trigger =>
                $"{trigger.Entity.Name.Replace('\n', ' ')} ×{trigger.Count}"
            )
        );

    internal static string Group(
        CombatImpactGroup group,
        bool chinese,
        string? criticalMarker = null
    )
    {
        var parts = new List<string>();
        if (group.Count > 0 && ShouldShowCount(group.Kind, group.Surface))
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
                    Value(group.ObservedValue.Value, group.Unit, ShouldShowSign(group), chinese)
                );
            }

            if (
                authoritative?.Basis == CombatImpactAuthoritativeBasis.ApplicationCount
                && (group.Count == 0 || authoritative.Value != group.Count)
            )
            {
                parts.Add(
                    chinese
                        ? $"生效 {authoritative.Value} 次"
                        : $"{authoritative.Value} application{(authoritative.Value == 1 ? string.Empty : "s")}"
                );
            }
        }

        return string.Join(" · ", parts);
    }

    internal static string Target(CombatImpactGroup group, CombatImpactTarget target, bool chinese)
    {
        var count = $"×{target.Count}";
        if (!target.ObservedValue.HasValue)
            return ShouldShowCount(group.Kind, group.Surface) ? count : string.Empty;

        var value = Value(target.ObservedValue.Value, target.Unit, ShouldShowSign(group), chinese);
        var needsObservedBasis = group.HasDivergentTargetCoverage;
        if (needsObservedBasis)
            value = chinese ? $"已记录 {value}" : $"{value} recorded";
        return ShouldShowCount(group.Kind, group.Surface) ? $"{count} · {value}" : value;
    }

    internal static string IncomingGroup(
        CombatImpactIncomingGroup group,
        bool chinese,
        string? criticalMarker = null
    )
    {
        var parts = new List<string>();
        if (group.Count > 0 && ShouldShowCount(group.Kind, group.Surface))
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

    private static bool ShouldShowCount(CombatImpactKind kind, CombatImpactEventSurface surface) =>
        kind != CombatImpactKind.AttributeChange
        || surface == CombatImpactEventSurface.AppliedEffect;

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
                is "TempoRemoveAmount"
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
            return ShouldShowCount(group.Kind, group.Surface) ? count : string.Empty;

        var value = Value(source.ObservedValue.Value, source.Unit, ShouldShowSign(group), chinese);
        return ShouldShowCount(group.Kind, group.Surface) ? $"{count} · {value}" : value;
    }

    internal static IReadOnlyList<string> CausedDisclosures(
        CombatImpactSource? source,
        bool chinese
    )
    {
        if (source == null)
            return [];

        var disclosures = new List<string>();
        var unresolvedTargetCount = source.Groups.Sum(group => group.UnresolvedTargetCount);
        if (unresolvedTargetCount > 0)
        {
            disclosures.Add(
                chinese
                    ? $"* 明细不完整：{unresolvedTargetCount} 个效果缺少目标数据。"
                    : $"* Partial breakdown: {unresolvedTargetCount} effect{(unresolvedTargetCount == 1 ? string.Empty : "s")} had no target data."
            );
        }

        if (source.Groups.Any(HasReconstructedDetail))
            disclosures.Add(ReconstructionDisclosure(chinese));
        return disclosures;
    }

    internal static IReadOnlyList<string> ReceivedDisclosures(
        CombatImpactReceived? received,
        bool chinese
    )
    {
        if (received == null || !received.Groups.Any(HasReconstructedDetail))
            return [];
        return [ReconstructionDisclosure(chinese)];
    }

    private static bool HasReconstructedDetail(CombatImpactGroup group) =>
        IsEstimated(group.ObservedCoverage)
        || group.Targets.Any(target => IsEstimated(target.ObservedCoverage))
        || group.AuthoritativeMetric is { Basis: CombatImpactAuthoritativeBasis.TotalAmount } total
            && (
                !group.ObservedValue.HasValue
                || group.Unit != total.Unit
                || group.ObservedValue.Value != total.Value
            )
        || group.AuthoritativeMetric
            is { Basis: CombatImpactAuthoritativeBasis.ApplicationCount } applications
            && group.Count != applications.Value;

    private static bool HasReconstructedDetail(CombatImpactIncomingGroup group) =>
        IsEstimated(group.ObservedCoverage)
        || group.Sources.Any(source => IsEstimated(source.ObservedCoverage));

    private static bool IsEstimated(CombatImpactCoverage coverage) =>
        coverage is CombatImpactCoverage.LowerBound or CombatImpactCoverage.Partial;

    private static string ReconstructionDisclosure(bool chinese) =>
        chinese
            ? "* 部分明细及持续时间由战斗事件推算，可能与游戏总计不完全一致。"
            : "* Some details and durations are reconstructed from combat events and may not match the game totals exactly.";

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

    private static string Integer(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
