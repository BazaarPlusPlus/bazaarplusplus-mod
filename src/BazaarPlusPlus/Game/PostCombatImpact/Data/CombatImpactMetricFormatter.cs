#nullable enable
using System.Globalization;
using BazaarGameShared.Domain.Core.Types;

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
        if (isSkill && source.ObservedActivationBatchCount > 0)
        {
            parts.Add(
                chinese
                    ? $"观测触发 {source.ObservedActivationBatchCount} 批"
                    : $"{source.ObservedActivationBatchCount} observed trigger batch{(source.ObservedActivationBatchCount == 1 ? string.Empty : "es")}"
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
        var sources = TriggerSourceValues(group, chinese);
        if (string.IsNullOrWhiteSpace(sources))
            return string.Empty;

        return chinese
            ? $"{TriggerSourceLabel(chinese)}{sources}"
            : $"{TriggerSourceLabel(chinese)} {sources}";
    }

    internal static string TriggerSourceLabel(bool chinese) =>
        chinese ? "触发来源：" : "Triggered by:";

    internal static string TriggerSourceValues(CombatImpactGroup group, bool chinese)
    {
        if (
            group.TriggerPresentationState
            is CombatImpactTriggerPresentationState.None
                or CombatImpactTriggerPresentationState.HiddenSelfOnly
        )
            return string.Empty;
        if (
            group.TriggerPresentationState
            == CombatImpactTriggerPresentationState.BreakdownUnavailable
        )
            return chinese ? "明细不可用" : "breakdown unavailable";

        var attributed = group.TriggerSources.Sum(source => source.ApplicationCount);
        var parts = new List<string>
        {
            chinese
                ? $"已归因 {attributed}/{group.Count}"
                : $"{attributed}/{group.Count} attributed",
        };
        parts.AddRange(
            group.TriggerSources.Select(trigger =>
                $"{trigger.Entity.Name.Replace('\n', ' ')} ×{trigger.ApplicationCount}"
            )
        );
        AddRemainder(
            parts,
            group.UnattributedTriggerApplicationCount,
            chinese ? "未解析" : "unresolved"
        );
        AddRemainder(
            parts,
            group.TriggerFallbackApplicationCount,
            chinese ? "来源回退" : "source fallback"
        );
        AddRemainder(
            parts,
            group.NoTriggerEvidenceApplicationCount,
            chinese ? "无触发记录" : "no trigger evidence"
        );
        AddRemainder(
            parts,
            group.NotApplicableTriggerApplicationCount,
            chinese ? "非触发" : "not trigger-scoped"
        );
        return string.Join(" · ", parts);
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
            AddAmountResidual(parts, group.AmountLedger, chinese);
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

        var accountingAnomalies = source.Groups.Count(group =>
            group.ApplicationLedger.ControlStatus == CombatImpactControlStatus.OverObserved
            || group.AmountLedger.ControlStatus == CombatImpactControlStatus.OverObserved
        );
        if (accountingAnomalies > 0)
        {
            disclosures.Add(
                chinese
                    ? $"* 对账异常：{accountingAnomalies} 个指标的明细超过权威总数。"
                    : $"* Accounting mismatch: {accountingAnomalies} metric{(accountingAnomalies == 1 ? string.Empty : "s")} exceeded the authoritative total."
            );
        }

        return disclosures;
    }

    internal static IReadOnlyList<string> ReceivedDisclosures(
        CombatImpactReceived? received,
        bool chinese
    )
    {
        if (received == null)
            return [];

        var unresolvedSourceCount = received.Groups.Sum(group => group.UnresolvedSourceCount);
        if (unresolvedSourceCount == 0)
            return [];

        return
        [
            chinese
                ? $"* 明细不完整：{unresolvedSourceCount} 个效果缺少来源数据。"
                : $"* Partial breakdown: {unresolvedSourceCount} effect{(unresolvedSourceCount == 1 ? string.Empty : "s")} had no source data.",
        ];
    }

    internal static IReadOnlyList<string> PeriodicResidualDisclosures(
        IReadOnlyList<PeriodicAttributionGap> residuals,
        bool chinese
    )
    {
        return residuals
            .Where(residual => residual.HealthAmount > 0 || residual.ShieldAmount > 0)
            .GroupBy(residual => (residual.Combatant, residual.Kind))
            .OrderBy(group => group.Key.Combatant)
            .ThenBy(group => group.Key.Kind)
            .Select(group =>
            {
                var health = group.Sum(residual => residual.HealthAmount);
                var shield = group.Sum(residual => residual.ShieldAmount);
                var amounts = new List<string>();
                if (health > 0)
                    amounts.Add(
                        chinese ? $"{Integer(health)} 生命值" : $"{Integer(health)} health"
                    );
                if (shield > 0)
                    amounts.Add(chinese ? $"{Integer(shield)} 护盾" : $"{Integer(shield)} shield");
                var combatant = group.Key.Combatant switch
                {
                    ECombatantId.Player => chinese ? "我方" : "Player",
                    ECombatantId.Opponent => chinese ? "对手" : "Opponent",
                    _ => group.Key.Combatant.ToString(),
                };
                var kind = group.Key.Kind switch
                {
                    CombatImpactPeriodicKind.Burn => chinese ? "灼烧" : "Burn",
                    CombatImpactPeriodicKind.Poison => chinese ? "剧毒" : "Poison",
                    CombatImpactPeriodicKind.Regen => chinese ? "恢复" : "Regen",
                    _ => group.Key.Kind.ToString(),
                };
                return chinese
                    ? $"* 本场未归因：{combatant}·{kind} {string.Join("、", amounts)}。"
                    : $"* Combat-wide unattributed: {combatant} {kind}, {string.Join(" and ", amounts)}.";
            })
            .ToArray();
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

    private static void AddRemainder(List<string> parts, int count, string label)
    {
        if (count > 0)
            parts.Add($"{label} ×{count}");
    }

    private static void AddAmountResidual(
        List<string> parts,
        CombatImpactAmountLedger ledger,
        bool chinese
    )
    {
        if (ledger.AuthoritativeTotal.HasValue && !ledger.ObservedAmount.HasValue)
        {
            parts.Add(chinese ? "明细不可用" : "breakdown unavailable");
            return;
        }

        if (ledger.ResidualAmount is not > 0)
            return;

        var value = Integer(ledger.ResidualAmount.Value);
        parts.Add(
            ledger.ResidualCoverage == CombatImpactResidualCoverage.UpperBound
                ? chinese
                    ? $"至多 {value} 未归因"
                    : $"up to {value} unattributed"
                : chinese
                    ? $"{value} 未归因"
                    : $"{value} unattributed"
        );
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
