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
    Flying,
    AttributeChange,
    Destroy,
}

internal enum CombatImpactValueUnit
{
    Amount,
    Milliseconds,
    PercentagePoints,
    Applications,
}

internal enum CombatImpactEventSurface
{
    AppliedEffect,
    CardAttribute,
    PlayerAttribute,
}

internal enum CombatImpactValueBasis
{
    None,
    ExactAdjustment,
    ConfiguredActionAmount,
    NetFrameDelta,
}

internal enum CombatImpactOccurrenceBasis
{
    ReconstructedTransition,
    ExplicitExecution,
}

internal enum CombatImpactCoverage
{
    None,
    Exact,
    LowerBound,
    Partial,
}

internal enum CombatImpactAuthoritativeBasis
{
    TotalAmount,
    ApplicationCount,
}

internal enum CombatImpactPeriodicKind
{
    Burn,
    Poison,
    Regen,
}

internal enum CombatImpactPeriodicProof
{
    Exact,
    Constrained,
    Proportional,
}

internal readonly record struct PeriodicImpactKey(string SourceId, CombatImpactPeriodicKind Kind);

internal sealed record CombatImpactPeriodicImpact(
    int HealthAmount,
    int ShieldAmount,
    CombatImpactPeriodicProof Proof,
    string ModelVersion
);

internal sealed record CombatImpactEntity(
    string Id,
    string Name,
    string TypeLabel,
    EHero? Hero,
    int Order,
    Guid TemplateId = default,
    ETier Tier = ETier.Bronze,
    int DisplaySpan = 1,
    EEnchantmentType? EnchantmentType = null,
    IReadOnlyDictionary<ECardAttributeType, int>? Attributes = null,
    ECombatantId? CombatantId = null,
    IReadOnlyDictionary<string, ECardAttributeType>? AbilityAttributeTypesByEffectId = null,
    IReadOnlyDictionary<string, ECardAttributeType>? AuraAttributeTypesByEffectId = null,
    IReadOnlyCollection<string>? ReferenceValuedAuraEffectIds = null
);

internal sealed record CombatImpactEvent(
    CombatImpactKind Kind,
    string SourceId,
    string TargetId,
    int? Value = null,
    CombatImpactValueUnit Unit = CombatImpactValueUnit.Amount,
    string? NativeAttributeKey = null,
    bool IsCritical = false,
    CombatImpactValueBasis ValueBasis = CombatImpactValueBasis.ExactAdjustment
)
{
    internal CombatImpactEventSurface Surface { get; init; } =
        CombatImpactEventSurface.AppliedEffect;

    internal CombatImpactOccurrenceBasis OccurrenceBasis { get; init; } =
        CombatImpactOccurrenceBasis.ReconstructedTransition;

    internal int CriticalCount { get; init; }

    internal int? CriticalValue { get; init; }

    internal int? NonCriticalValue { get; init; }

    internal int? AlternateNonCriticalValue { get; init; }

    internal bool HasCriticalAdjustmentCandidate { get; init; }
}

internal sealed record CombatImpactTarget(
    CombatImpactEntity Entity,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage
);

internal sealed record CombatImpactAuthoritativeMetric
{
    internal CombatImpactAuthoritativeMetric(
        CombatImpactKind kind,
        string nativeAttributeKey,
        int value,
        CombatImpactValueUnit unit,
        CombatImpactAuthoritativeBasis basis
    )
    {
        var valid = basis switch
        {
            CombatImpactAuthoritativeBasis.TotalAmount => unit == CombatImpactValueUnit.Amount,
            CombatImpactAuthoritativeBasis.ApplicationCount => unit
                == CombatImpactValueUnit.Applications,
            _ => false,
        };
        if (!valid)
            throw new ArgumentException(
                $"Authoritative basis {basis} cannot use metric unit {unit}.",
                nameof(unit)
            );

        Kind = kind;
        NativeAttributeKey = nativeAttributeKey;
        Value = value;
        Unit = unit;
        Basis = basis;
    }

    internal CombatImpactKind Kind { get; }

    internal string NativeAttributeKey { get; }

    internal int Value { get; }

    internal CombatImpactValueUnit Unit { get; }

    internal CombatImpactAuthoritativeBasis Basis { get; }
}

internal sealed record CombatImpactGroup(
    CombatImpactKind Kind,
    string NativeAttributeKey,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage,
    CombatImpactAuthoritativeMetric? AuthoritativeMetric,
    int UnresolvedTargetCount,
    IReadOnlyList<CombatImpactTarget> Targets
)
{
    internal CombatImpactEventSurface Surface { get; init; } =
        CombatImpactEventSurface.AppliedEffect;

    internal CombatImpactOccurrenceBasis OccurrenceBasis { get; init; } =
        CombatImpactOccurrenceBasis.ReconstructedTransition;

    internal int CriticalCount { get; init; }

    internal int? CriticalObservedValue { get; init; }

    internal bool HasMixedValueDirections { get; init; }

    internal IReadOnlyList<CombatImpactTriggerSource> TriggerSources { get; init; } = [];

    internal CombatImpactPeriodicImpact? PeriodicImpact { get; init; }

    internal bool HasDivergentTargetCoverage =>
        UnresolvedTargetCount > 0
        || AuthoritativeMetric is { Basis: CombatImpactAuthoritativeBasis.TotalAmount } total
            && (!ObservedValue.HasValue || Unit != total.Unit || ObservedValue.Value != total.Value)
        || AuthoritativeMetric
            is { Basis: CombatImpactAuthoritativeBasis.ApplicationCount } applications
            && Count != applications.Value;
}

internal sealed record CombatImpactTriggerSource(CombatImpactEntity Entity, int Count);

internal sealed record CombatImpactSource(
    CombatImpactEntity Entity,
    int UseCount,
    int EffectCount,
    IReadOnlyList<CombatImpactGroup> Groups
)
{
    internal int TotalCount => EffectCount;

    internal IReadOnlyList<CombatImpactTriggerSource> TriggerSources { get; init; } = [];

    internal int TriggerCount => TriggerSources.Sum(source => source.Count);
}

internal sealed record CombatImpactIncomingSource(
    CombatImpactEntity Entity,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage
);

internal sealed record CombatImpactIncomingGroup(
    CombatImpactKind Kind,
    string NativeAttributeKey,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage,
    IReadOnlyList<CombatImpactIncomingSource> Sources
)
{
    internal CombatImpactEventSurface Surface { get; init; } =
        CombatImpactEventSurface.AppliedEffect;

    internal CombatImpactOccurrenceBasis OccurrenceBasis { get; init; } =
        CombatImpactOccurrenceBasis.ReconstructedTransition;

    internal int CriticalCount { get; init; }

    internal int? CriticalObservedValue { get; init; }

    internal bool HasMixedValueDirections { get; init; }
}

internal sealed record CombatImpactReceived(
    CombatImpactEntity Entity,
    int EffectCount,
    IReadOnlyList<CombatImpactIncomingGroup> Groups
)
{
    internal int TotalCount => EffectCount;
}

internal sealed record CombatImpactReport(
    IReadOnlyList<CombatImpactSource> Sources,
    IReadOnlyList<CombatImpactReceived> Received
)
{
    internal static readonly CombatImpactReport Empty = new(
        Array.Empty<CombatImpactSource>(),
        Array.Empty<CombatImpactReceived>()
    );
}

internal sealed record CombatImpactProjectionInput(
    IReadOnlyDictionary<string, CombatImpactEntity> Entities,
    IReadOnlyList<CombatImpactEvent> Events,
    IReadOnlyDictionary<string, int> UseCounts,
    IReadOnlyDictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>> AuthoritativeMetrics
);
