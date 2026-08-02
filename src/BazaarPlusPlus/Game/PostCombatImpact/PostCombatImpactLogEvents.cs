#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal enum PostCombatImpactReasonCode
{
    ProjectionException,
    Shown,
    ShownWithoutAttributedImpact,
    RuntimeUnavailable,
    TooltipDataUnavailable,
    SourceIdUnavailable,
    PrimaryTooltipCreateTimedOut,
    AuxiliaryTooltipCreateTimedOut,
    AuxiliaryTooltipContentUnavailable,
    AuxiliaryTooltipPositionUnavailable,
    NativeAuxiliaryDisplaced,
    NativeAuxiliaryHidden,
    NativeAuxiliaryRequeued,
    TooltipRenderException,
    Dismissed,
    RecapHoverObserved,
    StaleRequestDiscarded,
    PairPlacementOverflowed,
    PairPlacementTooNarrow,
    PairTopAlignmentAdjusted,
    PrimaryGeometrySettleTimedOut,
    PairGeometrySettleTimedOut,
    PerspectiveReceived,
    PerspectiveCaused,
    EntityPreviewUnavailable,
    EntityPreviewCreateTimedOut,
}

internal enum PostCombatImpactHoverExitOrigin
{
    RecapPointerExit,
    RecapDisabled,
    SkillPointerExit,
}

[BppLogEventSource]
internal static class PostCombatImpactLogEvents
{
    internal static readonly BppLogFieldDefinition ReasonCode = new(
        0,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogEventDefinition ProjectionDegraded = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.projection.degraded",
        [ReasonCode],
        new BppLogStormPolicy([ReasonCode])
    );

    internal static readonly BppLogEventDefinition InteractionObserved = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.interaction.observed",
        [ReasonCode]
    );

    internal static readonly BppLogEventDefinition InteractionDegraded = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.interaction.degraded",
        [ReasonCode],
        new BppLogStormPolicy([ReasonCode])
    );
}
