#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal enum PostCombatImpactReasonCode
{
    ProjectionException,
    NativeBackRejected,
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

    internal static readonly BppLogEventDefinition CloseDegraded = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.close.degraded",
        [ReasonCode],
        new BppLogStormPolicy([ReasonCode])
    );
}
