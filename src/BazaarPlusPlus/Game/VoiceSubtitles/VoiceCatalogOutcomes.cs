#nullable enable
using System;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal enum VoiceCatalogSource
{
    None,
    Cache,
    Embedded,
    Remote,
}

internal enum VoiceCatalogEndpoint
{
    VoiceCatalog,
}

internal enum VoiceCatalogReasonCode
{
    CacheMissing,
    CacheStale,
    SourceRejected,
    NoUsableCatalog,
    WarmUpException,
    RefreshQueueFailed,
    RemoteFailed,
    EmptyResponse,
    WriteFailed,
}

internal enum VoiceCatalogRowSkipReason
{
    MissingStem,
    DuplicateStem,
    EmptyText,
}

internal enum VoiceCatalogSourceOutcomeKind
{
    Missing,
    Fresh,
    Stale,
    Rejected,
}

internal readonly record struct VoiceCatalogSourceOutcome(
    VoiceCatalogSourceOutcomeKind Kind,
    VoiceCatalogSource Source,
    VoiceLine[]? Lines,
    VoiceCatalogReasonCode? ReasonCode,
    Exception? Exception
)
{
    internal static VoiceCatalogSourceOutcome Missing(VoiceCatalogSource source) =>
        new(VoiceCatalogSourceOutcomeKind.Missing, source, null, null, null);

    internal static VoiceCatalogSourceOutcome Fresh(VoiceCatalogSource source, VoiceLine[] lines) =>
        new(VoiceCatalogSourceOutcomeKind.Fresh, source, lines, null, null);

    internal static VoiceCatalogSourceOutcome Stale(VoiceCatalogSource source, VoiceLine[] lines) =>
        new(
            VoiceCatalogSourceOutcomeKind.Stale,
            source,
            lines,
            VoiceCatalogReasonCode.CacheStale,
            null
        );

    internal static VoiceCatalogSourceOutcome Rejected(
        VoiceCatalogSource source,
        VoiceCatalogReasonCode reasonCode,
        Exception? exception = null
    ) => new(VoiceCatalogSourceOutcomeKind.Rejected, source, null, reasonCode, exception);
}

internal enum VoiceCatalogLoadOutcomeKind
{
    Ready,
    Degraded,
    Failed,
}

internal readonly record struct VoiceCatalogLoadOutcome(
    VoiceCatalogLoadOutcomeKind Kind,
    VoiceLine[]? Lines,
    VoiceCatalogSource CatalogSource,
    VoiceCatalogSource EventSource,
    VoiceCatalogReasonCode? ReasonCode,
    Exception? Exception,
    bool ShouldRefresh
)
{
    internal static VoiceCatalogLoadOutcome Ready(
        VoiceLine[] lines,
        VoiceCatalogSource source,
        bool shouldRefresh
    ) => new(VoiceCatalogLoadOutcomeKind.Ready, lines, source, source, null, null, shouldRefresh);

    internal static VoiceCatalogLoadOutcome Degraded(
        VoiceLine[] lines,
        VoiceCatalogSource catalogSource,
        VoiceCatalogSource eventSource,
        VoiceCatalogReasonCode reasonCode,
        Exception? exception,
        bool shouldRefresh
    ) =>
        new(
            VoiceCatalogLoadOutcomeKind.Degraded,
            lines,
            catalogSource,
            eventSource,
            reasonCode,
            exception,
            shouldRefresh
        );

    internal static VoiceCatalogLoadOutcome Failed(
        VoiceCatalogSource source,
        VoiceCatalogReasonCode reasonCode,
        Exception? exception,
        bool shouldRefresh
    ) =>
        new(
            VoiceCatalogLoadOutcomeKind.Failed,
            null,
            VoiceCatalogSource.None,
            source,
            reasonCode,
            exception,
            shouldRefresh
        );
}

internal readonly record struct VoiceCatalogRemoteOutcome(
    VoiceCatalogSourceOutcome SourceOutcome,
    Exception? CacheWriteException
);
