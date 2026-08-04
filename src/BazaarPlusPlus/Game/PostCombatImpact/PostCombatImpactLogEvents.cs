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
    AuxiliaryTooltipShowRetried,
    AuxiliaryTooltipPositionUnavailable,
    TypographyUnavailable,
    PairOpenMissingAuxiliaryFields,
    PairOpenDyingController,
    PairOpenMissingBackground,
    PairOpenBackgroundCloneRejected,
    NativeAuxiliaryDisplaced,
    NativeAuxiliaryHidden,
    NativeAuxiliaryRequeued,
    TooltipRenderException,
    Dismissed,
    RecapHoverObserved,
    StaleRequestDiscarded,
    PendingShowBlocked,
    PendingShowAborted,
    NativeAuxiliaryUnmatched,
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

    internal static readonly BppLogFieldDefinition Card = new(
        1,
        "card",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition Detail = new(
        2,
        "detail",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    /// <summary>
    /// Card-identified interaction trail. Exists because the plain observed trail proved unable
    /// to attribute silent per-card failures: it shows a hover with no outcome but not which card
    /// or which silent gate swallowed it.
    /// </summary>
    internal static readonly BppLogEventDefinition InteractionTraced = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.interaction.traced",
        [ReasonCode, Card, Detail]
    );

    internal static readonly BppLogEventDefinition InteractionDegraded = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.interaction.degraded",
        [ReasonCode],
        new BppLogStormPolicy([ReasonCode])
    );
}
