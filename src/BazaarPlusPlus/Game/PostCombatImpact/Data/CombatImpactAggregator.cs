#nullable enable

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactAggregator
{
    internal static CombatImpactReport Aggregate(CombatImpactProjectionInput input)
    {
        var sourceIds = new HashSet<string>(
            input.Events.Select(item => item.SourceId),
            StringComparer.Ordinal
        );
        sourceIds.UnionWith(input.AuthoritativeMetrics.Keys);

        var sources = sourceIds
            .Select(sourceId => BuildSource(input, sourceId))
            .Where(source => source != null)
            .Cast<CombatImpactSource>()
            .OrderByDescending(source => source.TotalCount)
            .ThenBy(source => source.Entity.Order)
            .ThenBy(source => source.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Entity.Id, StringComparer.Ordinal)
            .ToArray();

        var received = input
            .Events.Select(item => item.TargetId)
            .Distinct(StringComparer.Ordinal)
            .Select(targetId => BuildReceived(input, targetId))
            .Where(target => target != null)
            .Cast<CombatImpactReceived>()
            .OrderByDescending(target => target.TotalCount)
            .ThenBy(target => target.Entity.Order)
            .ThenBy(target => target.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Entity.Id, StringComparer.Ordinal)
            .ToArray();

        return new CombatImpactReport(sources, received);
    }

    private static CombatImpactSource? BuildSource(
        CombatImpactProjectionInput input,
        string sourceId
    )
    {
        if (!input.Entities.TryGetValue(sourceId, out var sourceEntity))
            return null;
        if (sourceEntity.TypeLabel is not ("Item" or "Skill"))
            return null;

        var events = input
            .Events.Where(item => string.Equals(item.SourceId, sourceId, StringComparison.Ordinal))
            .ToArray();
        var authoritativeMetrics =
            input.AuthoritativeMetrics.GetValueOrDefault(sourceId)
            ?? Array.Empty<CombatImpactAuthoritativeMetric>();
        var groupKeys = new HashSet<GroupKey>(
            events.Select(item => Key(item.Kind, item.NativeAttributeKey, item.Surface))
        );
        groupKeys.UnionWith(
            authoritativeMetrics.Select(metric => new GroupKey(
                metric.Kind,
                metric.NativeAttributeKey,
                CombatImpactEventSurface.AppliedEffect
            ))
        );

        var groups = groupKeys
            .Select(key =>
                BuildGroup(
                    input.Entities,
                    key,
                    events
                        .Where(item => Key(item.Kind, item.NativeAttributeKey, item.Surface) == key)
                        .ToArray(),
                    authoritativeMetrics.FirstOrDefault(metric =>
                        key.Surface == CombatImpactEventSurface.AppliedEffect
                        && metric.Kind == key.Kind
                        && string.Equals(
                            metric.NativeAttributeKey,
                            key.NativeAttributeKey,
                            StringComparison.Ordinal
                        )
                    )
                )
            )
            .ToList();
        var orderedGroups = groups
            .Where(group => group.Count > 0 || group.AuthoritativeMetric != null)
            .OrderBy(group => GroupOrder(group.Kind))
            .ThenBy(group => group.NativeAttributeKey, StringComparer.Ordinal)
            .ThenBy(group => group.Surface)
            .ToArray();
        if (orderedGroups.Length == 0)
            return null;

        return new CombatImpactSource(
            sourceEntity,
            input.UseCounts.GetValueOrDefault(sourceId),
            events.Length,
            orderedGroups
        );
    }

    private static CombatImpactGroup BuildGroup(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        GroupKey key,
        IReadOnlyList<CombatImpactEvent> events,
        CombatImpactAuthoritativeMetric? authoritativeMetric
    )
    {
        var targets = events
            .GroupBy(item => item.TargetId, StringComparer.Ordinal)
            .Select(group => BuildTarget(entities, group.ToArray()))
            .Where(target => target != null)
            .Cast<CombatImpactTarget>()
            .OrderByDescending(target => target.Count)
            .ThenBy(target => target.Entity.Order)
            .ThenBy(target => target.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Entity.Id, StringComparer.Ordinal)
            .ToArray();
        var unresolvedTargetCount = Math.Max(0, events.Count - targets.Sum(target => target.Count));

        var observed = BuildObserved(events);
        var coverage = observed.Coverage;
        if (
            authoritativeMetric?.Basis == CombatImpactAuthoritativeBasis.TotalAmount
            && observed.Value.HasValue
            && (
                observed.Unit != authoritativeMetric.Unit
                || observed.Value.Value != authoritativeMetric.Value
            )
        )
            coverage = CombatImpactCoverage.Partial;

        var critical = BuildCritical(events);
        return new CombatImpactGroup(
            key.Kind,
            key.NativeAttributeKey,
            events.Count,
            observed.Value,
            observed.Value.HasValue
                ? observed.Unit
                : events.FirstOrDefault()?.Unit
                    ?? authoritativeMetric?.Unit
                    ?? CombatImpactValueUnit.Amount,
            coverage,
            authoritativeMetric,
            unresolvedTargetCount,
            targets
        )
        {
            Surface = key.Surface,
            CriticalCount = critical.Count,
            CriticalObservedValue = critical.Observed.Value,
            HasMixedValueDirections = HasMixedValueDirections(events),
        };
    }

    private static CombatImpactReceived? BuildReceived(
        CombatImpactProjectionInput input,
        string targetId
    )
    {
        if (!input.Entities.TryGetValue(targetId, out var targetEntity))
            return null;
        if (targetEntity.TypeLabel is not ("Item" or "Skill"))
            return null;

        var events = input
            .Events.Where(item => string.Equals(item.TargetId, targetId, StringComparison.Ordinal))
            .ToArray();
        var groups = events
            .GroupBy(item => Key(item.Kind, item.NativeAttributeKey, item.Surface))
            .Select(group => BuildIncomingGroup(input.Entities, group.Key, group.ToArray()))
            .ToList();

        var orderedGroups = groups
            .OrderBy(group => GroupOrder(group.Kind))
            .ThenBy(group => group.NativeAttributeKey, StringComparer.Ordinal)
            .ThenBy(group => group.Surface)
            .ToArray();
        if (orderedGroups.Length == 0)
            return null;

        return new CombatImpactReceived(targetEntity, events.Length, orderedGroups);
    }

    private static CombatImpactIncomingGroup BuildIncomingGroup(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        GroupKey key,
        IReadOnlyList<CombatImpactEvent> events
    )
    {
        var sources = events
            .GroupBy(item => item.SourceId, StringComparer.Ordinal)
            .Select(group => BuildIncomingSource(entities, group.ToArray()))
            .Where(source => source != null)
            .Cast<CombatImpactIncomingSource>()
            .OrderByDescending(source => source.Count)
            .ThenBy(source => source.Entity.Order)
            .ThenBy(source => source.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Entity.Id, StringComparer.Ordinal)
            .ToArray();
        var observed = BuildObserved(events);

        var critical = BuildCritical(events);
        return new CombatImpactIncomingGroup(
            key.Kind,
            key.NativeAttributeKey,
            events.Count,
            observed.Value,
            observed.Unit,
            observed.Coverage,
            sources
        )
        {
            Surface = key.Surface,
            CriticalCount = critical.Count,
            CriticalObservedValue = critical.Observed.Value,
            HasMixedValueDirections = HasMixedValueDirections(events),
        };
    }

    private static bool HasMixedValueDirections(IReadOnlyList<CombatImpactEvent> events) =>
        events.Any(item => item.Value is > 0) && events.Any(item => item.Value is < 0);

    private static CriticalAggregate BuildCritical(IReadOnlyList<CombatImpactEvent> events)
    {
        var criticalCount = events.Sum(item =>
            item.CriticalCount > 0 ? item.CriticalCount
            : item.IsCritical ? 1
            : 0
        );
        if (criticalCount == 0)
            return new CriticalAggregate(
                0,
                new ObservedAggregate(
                    null,
                    events.FirstOrDefault()?.Unit ?? CombatImpactValueUnit.Amount,
                    CombatImpactCoverage.None
                )
            );

        var contributing = events
            .Where(item => item.CriticalCount > 0 || item.IsCritical)
            .ToArray();
        var units = contributing.Select(item => item.Unit).Distinct().ToArray();
        var values = contributing
            .Select(item => item.CriticalValue ?? (item.IsCritical ? item.Value : null))
            .ToArray();
        var hasEveryValue = values.All(value => value.HasValue);
        int? value = values.Any(item => item.HasValue)
            ? SaturatingSum(values.Where(item => item.HasValue).Select(item => item!.Value))
            : null;
        return new CriticalAggregate(
            criticalCount,
            new ObservedAggregate(
                value,
                units.Length == 1 ? units[0] : CombatImpactValueUnit.Amount,
                !value.HasValue ? CombatImpactCoverage.None
                    : hasEveryValue && units.Length == 1 ? CombatImpactCoverage.Exact
                    : CombatImpactCoverage.Partial
            )
        );
    }

    private static CombatImpactIncomingSource? BuildIncomingSource(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyList<CombatImpactEvent> events
    )
    {
        if (!entities.TryGetValue(events[0].SourceId, out var entity))
            return null;

        var observed = BuildObserved(events);
        return new CombatImpactIncomingSource(
            entity,
            events.Count,
            observed.Value,
            observed.Unit,
            observed.Coverage
        );
    }

    private static CombatImpactTarget? BuildTarget(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyList<CombatImpactEvent> events
    )
    {
        if (!entities.TryGetValue(events[0].TargetId, out var entity))
            return null;

        var observed = BuildObserved(events);
        return new CombatImpactTarget(
            entity,
            events.Count,
            observed.Value,
            observed.Unit,
            observed.Coverage
        );
    }

    private static ObservedAggregate BuildObserved(IReadOnlyList<CombatImpactEvent> events)
    {
        var fallbackUnit = events.FirstOrDefault()?.Unit ?? CombatImpactValueUnit.Amount;
        var knownValues = events.Where(item => item.Value.HasValue).ToArray();
        if (knownValues.Length == 0)
            return new ObservedAggregate(null, fallbackUnit, CombatImpactCoverage.None);

        var units = knownValues.Select(item => item.Unit).Distinct().ToArray();
        if (units.Length != 1)
            return new ObservedAggregate(null, fallbackUnit, CombatImpactCoverage.Partial);

        var coverage =
            knownValues.Length != events.Count ? CombatImpactCoverage.Partial
            : knownValues.Any(item => item.ValueBasis == CombatImpactValueBasis.None)
                ? CombatImpactCoverage.Partial
            : knownValues.Any(item => item.ValueBasis == CombatImpactValueBasis.NetFrameDelta)
                ? CombatImpactCoverage.LowerBound
            : CombatImpactCoverage.Exact;
        return new ObservedAggregate(SumValues(knownValues), units[0], coverage);
    }

    private static GroupKey Key(
        CombatImpactKind kind,
        string? nativeAttributeKey,
        CombatImpactEventSurface surface
    ) => new(kind, nativeAttributeKey ?? NativeKey(kind), surface);

    private static int SumValues(IEnumerable<CombatImpactEvent> events)
    {
        long value = 0;
        foreach (var item in events)
            value += item.Value!.Value;
        return value > int.MaxValue ? int.MaxValue
            : value < int.MinValue ? int.MinValue
            : (int)value;
    }

    private static int SaturatingSum(IEnumerable<int> values)
    {
        long total = 0;
        foreach (var value in values)
            total += value;
        return total > int.MaxValue ? int.MaxValue
            : total < int.MinValue ? int.MinValue
            : (int)total;
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
            CombatImpactKind.Flying => "Flying",
            CombatImpactKind.Destroy => "DestroyTargets",
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
            CombatImpactKind.Flying => 9,
            CombatImpactKind.AttributeChange => 10,
            CombatImpactKind.Destroy => 11,
            _ => 99,
        };

    private readonly record struct GroupKey(
        CombatImpactKind Kind,
        string NativeAttributeKey,
        CombatImpactEventSurface Surface
    );

    private readonly record struct ObservedAggregate(
        int? Value,
        CombatImpactValueUnit Unit,
        CombatImpactCoverage Coverage
    );

    private readonly record struct CriticalAggregate(int Count, ObservedAggregate Observed);
}
