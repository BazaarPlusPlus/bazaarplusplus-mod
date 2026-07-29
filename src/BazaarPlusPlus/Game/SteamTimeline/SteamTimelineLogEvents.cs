#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.SteamTimeline;

internal enum SteamTimelineLogReasonCode
{
    ApiException,
    UnsupportedApi,
}

internal enum SteamTimelineLogEventKind
{
    Run,
    Battle,
    Level,
}

internal enum SteamTimelineLogOutcome
{
    Started,
    Completed,
    Interrupted,
    Marked,
}

[BppLogEventSource]
internal static class SteamTimelineLogEvents
{
    internal static readonly BppLogFieldDefinition Operation = PublicLow(0, "operation");
    internal static readonly BppLogFieldDefinition ReasonCode = PublicLow(1, "reason_code");
    internal static readonly BppLogFieldDefinition EventKind = PublicLow(0, "event_kind");
    internal static readonly BppLogFieldDefinition Outcome = PublicLow(1, "outcome");
    internal static readonly BppLogFieldDefinition Level = new(
        2,
        "level",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );

    internal static readonly BppLogEventDefinition Degraded = new(
        BppLogFeatureScope.SteamTimeline,
        "steam_timeline.runtime.degraded",
        [Operation, ReasonCode],
        new BppLogStormPolicy([Operation, ReasonCode])
    );

    internal static readonly BppLogEventDefinition LifecycleChanged = new(
        BppLogFeatureScope.SteamTimeline,
        "steam_timeline.lifecycle.changed",
        [EventKind, Outcome, Level]
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.Low
        );
}
