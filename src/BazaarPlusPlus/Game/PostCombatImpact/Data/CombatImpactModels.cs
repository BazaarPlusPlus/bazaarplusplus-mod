#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal enum CombatImpactKind
{
    DirectDamage,
    Burn,
    Poison,
    Healing,
    Shield,
    Charge,
    Haste,
    Slow,
    Freeze,
    AttributeChange,
    Destroy,
    Critical,
}

internal enum CombatImpactValueUnit
{
    Amount,
    Milliseconds,
    PercentagePoints,
}

internal sealed record CombatImpactEntity(
    string Id,
    string Name,
    string TypeLabel,
    string? ArtKey,
    EHero? Hero,
    ECombatantId Owner,
    int Order
);

internal sealed record CombatImpactEvent(
    CombatImpactKind Kind,
    string SourceId,
    string TargetId,
    int? Value = null,
    CombatImpactValueUnit Unit = CombatImpactValueUnit.Amount,
    string? NativeAttributeKey = null
);

internal sealed record CombatImpactTarget(
    CombatImpactEntity Entity,
    int Count,
    int? AggregateValue,
    CombatImpactValueUnit Unit,
    bool ValueIsPartial
);

internal sealed record CombatImpactGroup(
    CombatImpactKind Kind,
    string NativeAttributeKey,
    int Count,
    int? AggregateValue,
    CombatImpactValueUnit Unit,
    bool ValueIsPartial,
    IReadOnlyList<CombatImpactTarget> Targets
);

internal sealed record CombatImpactSource(
    CombatImpactEntity Entity,
    int UseCount,
    int TriggerCount,
    int EffectCount,
    IReadOnlyList<CombatImpactGroup> Groups
)
{
    internal int TotalCount => EffectCount;
}

internal sealed record CombatImpactReport(IReadOnlyList<CombatImpactSource> Sources)
{
    internal static readonly CombatImpactReport Empty = new(Array.Empty<CombatImpactSource>());
}

internal sealed record CombatImpactProjectionInput(
    IReadOnlyDictionary<string, CombatImpactEntity> Entities,
    IReadOnlyList<CombatImpactEvent> Events,
    IReadOnlyDictionary<string, int> UseCounts,
    IReadOnlyDictionary<string, int> TriggerCounts,
    IReadOnlyDictionary<string, IReadOnlyDictionary<CombatImpactKind, int>> AuthoritativeTotals
);
