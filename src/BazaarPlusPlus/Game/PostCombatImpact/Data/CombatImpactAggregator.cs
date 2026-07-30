#nullable enable

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactAggregator
{
    internal static CombatImpactReport Aggregate(CombatImpactProjectionInput input)
    {
        var sources = input
            .Events.GroupBy(item => item.SourceId, StringComparer.Ordinal)
            .Select(group => BuildSource(input, group.Key, group.ToArray()))
            .Where(source => source != null)
            .Cast<CombatImpactSource>()
            .OrderByDescending(source => source.TotalCount)
            .ThenBy(source => source.Entity.Order)
            .ThenBy(source => source.Entity.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new CombatImpactReport(sources);
    }

    private static CombatImpactSource? BuildSource(
        CombatImpactProjectionInput input,
        string sourceId,
        IReadOnlyList<CombatImpactEvent> events
    )
    {
        if (!input.Entities.TryGetValue(sourceId, out var sourceEntity))
            return null;
        if (sourceEntity.TypeLabel is not ("Item" or "Skill"))
            return null;

        input.AuthoritativeTotals.TryGetValue(sourceId, out var authoritativeTotals);
        var groups = events
            .GroupBy(item => new GroupKey(
                item.Kind,
                item.NativeAttributeKey ?? NativeKey(item.Kind)
            ))
            .Select(group =>
                BuildGroup(input.Entities, group.Key, group.ToArray(), authoritativeTotals)
            )
            .Where(group => group.Count > 0)
            .OrderBy(group => GroupOrder(group.Kind))
            .ThenBy(group => group.NativeAttributeKey, StringComparer.Ordinal)
            .ToArray();

        if (groups.Length == 0)
            return null;

        return new CombatImpactSource(
            sourceEntity,
            input.UseCounts.GetValueOrDefault(sourceId),
            input.TriggerCounts.GetValueOrDefault(sourceId),
            events.Count(item => item.Kind != CombatImpactKind.Critical),
            groups
        );
    }

    private static CombatImpactGroup BuildGroup(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        GroupKey key,
        IReadOnlyList<CombatImpactEvent> events,
        IReadOnlyDictionary<CombatImpactKind, int>? authoritativeTotals
    )
    {
        var targets = events
            .GroupBy(item => item.TargetId, StringComparer.Ordinal)
            .Select(group => BuildTarget(entities, group.ToArray()))
            .Where(target => target != null)
            .Cast<CombatImpactTarget>()
            .OrderByDescending(target => target.Count)
            .ThenBy(target => target.Entity.Order)
            .ThenBy(target => target.Entity.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var knownValues = events.Where(item => item.Value.HasValue).ToArray();
        var unit = knownValues.FirstOrDefault()?.Unit ?? events[0].Unit;
        int? attributedTotal =
            knownValues.Length == 0
                ? null
                : knownValues.Sum(item =>
                    key.Kind == CombatImpactKind.AttributeChange
                        ? item.Value!.Value
                        : Math.Abs(item.Value!.Value)
                );
        var authoritativeTotal =
            authoritativeTotals != null && authoritativeTotals.TryGetValue(key.Kind, out var total)
                ? Math.Abs(total)
                : (int?)null;
        var aggregateValue = authoritativeTotal ?? attributedTotal;
        var valueIsPartial =
            aggregateValue.HasValue
            && !authoritativeTotal.HasValue
            && knownValues.Length != events.Count;

        return new CombatImpactGroup(
            key.Kind,
            key.NativeAttributeKey,
            events.Count,
            aggregateValue,
            unit,
            valueIsPartial,
            targets
        );
    }

    private static CombatImpactTarget? BuildTarget(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyList<CombatImpactEvent> events
    )
    {
        if (!entities.TryGetValue(events[0].TargetId, out var entity))
            return null;

        var knownValues = events.Where(item => item.Value.HasValue).ToArray();
        var preserveSign = events[0].Kind == CombatImpactKind.AttributeChange;
        return new CombatImpactTarget(
            entity,
            events.Count,
            knownValues.Length == 0
                ? null
                : knownValues.Sum(item =>
                    preserveSign ? item.Value!.Value : Math.Abs(item.Value!.Value)
                ),
            knownValues.FirstOrDefault()?.Unit ?? events[0].Unit,
            knownValues.Length != events.Count
        );
    }

    internal static string NativeKey(CombatImpactKind kind) =>
        kind switch
        {
            CombatImpactKind.DirectDamage => "DamageAmount",
            CombatImpactKind.Burn => "BurnApplyAmount",
            CombatImpactKind.Poison => "PoisonApplyAmount",
            CombatImpactKind.Healing => "HealAmount",
            CombatImpactKind.Shield => "ShieldApplyAmount",
            CombatImpactKind.Charge => "ChargeAmount",
            CombatImpactKind.Haste => "HasteAmount",
            CombatImpactKind.Slow => "SlowAmount",
            CombatImpactKind.Freeze => "FreezeAmount",
            CombatImpactKind.Destroy => "DestroyTargets",
            CombatImpactKind.Critical => "CritChance",
            _ => "Custom_0",
        };

    private static int GroupOrder(CombatImpactKind kind) =>
        kind switch
        {
            CombatImpactKind.DirectDamage => 0,
            CombatImpactKind.Burn => 1,
            CombatImpactKind.Poison => 2,
            CombatImpactKind.Healing => 3,
            CombatImpactKind.Shield => 4,
            CombatImpactKind.Charge => 5,
            CombatImpactKind.Haste => 6,
            CombatImpactKind.Slow => 7,
            CombatImpactKind.Freeze => 8,
            CombatImpactKind.AttributeChange => 9,
            CombatImpactKind.Destroy => 10,
            CombatImpactKind.Critical => 11,
            _ => 99,
        };

    private readonly record struct GroupKey(CombatImpactKind Kind, string NativeAttributeKey);
}
