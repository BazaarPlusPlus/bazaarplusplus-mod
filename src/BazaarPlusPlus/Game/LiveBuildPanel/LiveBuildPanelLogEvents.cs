#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal enum LiveBuildMountFailureReasonCode
{
    OverlayHostUnavailable,
}

internal enum LiveBuildRefreshResultCode
{
    Updated,
    NoChange,
}

internal enum LiveBuildRefreshFailureReasonCode
{
    RemoteEmptyResponse,
    RemoteInvalidResponse,
    RemoteRequestFailed,
    RefreshException,
}

internal enum LiveBuildCorpusSource
{
    Cache,
    Embedded,
    Remote,
    Unavailable,
}

internal enum LiveBuildCorpusReasonCode
{
    WarmupFailed,
    SynchronousFallback,
    StaleCache,
    EmbeddedFallback,
    EmbeddedMissing,
    EmbeddedInvalid,
    CacheReadFailed,
    CacheInvalid,
    RefreshQueueFailed,
    RemoteRefreshFailed,
}

internal enum LiveBuildCorpusEndpoint
{
    TenWinBuilds,
}

internal enum LiveBuildCacheWriteReasonCode
{
    WriteFailed,
}

[BppLogEventSource]
internal static class LiveBuildPanelLogEvents
{
    internal static readonly BppLogFieldDefinition MountFailedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition MountFailed = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.mount.failed",
        [MountFailedReasonCode]
    );

    internal static readonly BppLogFieldDefinition RefreshSucceededRequestId = Public(
        0,
        "request_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition RefreshSucceededResult = Public(
        1,
        "result",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition RefreshSucceeded = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.refresh.succeeded",
        [RefreshSucceededRequestId, RefreshSucceededResult]
    );

    internal static readonly BppLogFieldDefinition RefreshFailedRequestId = Public(
        0,
        "request_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition RefreshFailedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition RefreshFailed = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.refresh.failed",
        [RefreshFailedRequestId, RefreshFailedReasonCode]
    );

    internal static readonly BppLogEventDefinition CorpusWarmupStarted = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.corpus.warmup_started",
        []
    );

    internal static readonly BppLogFieldDefinition CorpusDegradedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CorpusDegradedSource = Public(
        1,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CorpusDegradedBuildCount = Public(
        2,
        "build_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CorpusDegradedExpired = Public(
        3,
        "expired",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CorpusDegradedCachePath = new(
        4,
        "cache_path",
        BppLogFieldPrivacy.LocalPath,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CorpusDegraded = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.corpus.degraded",
        [
            CorpusDegradedReasonCode,
            CorpusDegradedSource,
            CorpusDegradedBuildCount,
            CorpusDegradedExpired,
            CorpusDegradedCachePath,
        ],
        new BppLogStormPolicy([CorpusDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition CorpusReadySource = Public(
        0,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CorpusReadyBuildCount = Public(
        1,
        "build_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CorpusReady = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.corpus.ready",
        [CorpusReadySource, CorpusReadyBuildCount]
    );

    internal static readonly BppLogFieldDefinition CorpusRefreshQueuedReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CorpusRefreshQueued = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.corpus.refresh_queued",
        [CorpusRefreshQueuedReasonCode]
    );

    internal static readonly BppLogFieldDefinition CorpusRecoveredSource = Public(
        0,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CorpusRecoveredBuildCount = Public(
        1,
        "build_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CorpusRecovered = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.corpus.recovered",
        [CorpusRecoveredSource, CorpusRecoveredBuildCount]
    );

    internal static readonly BppLogFieldDefinition CorpusCacheLoadedBuildCount = Public(
        0,
        "build_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CorpusCacheLoadedExpired = Public(
        1,
        "expired",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CorpusCacheLoadedCachePath = new(
        2,
        "cache_path",
        BppLogFieldPrivacy.LocalPath,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CorpusCacheLoaded = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.corpus.cache_loaded",
        [CorpusCacheLoadedBuildCount, CorpusCacheLoadedExpired, CorpusCacheLoadedCachePath]
    );

    internal static readonly BppLogFieldDefinition CorpusRemoteLoadedEndpoint = Public(
        0,
        "endpoint",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CorpusRemoteLoadedBuildCount = Public(
        1,
        "build_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CorpusRemoteLoaded = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.corpus.remote_loaded",
        [CorpusRemoteLoadedEndpoint, CorpusRemoteLoadedBuildCount]
    );

    internal static readonly BppLogFieldDefinition CacheWriteDegradedPath = new(
        0,
        "path",
        BppLogFieldPrivacy.LocalPath,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CacheWriteDegradedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CacheWriteDegraded = new(
        BppLogFeatureScope.LiveBuildPanel,
        "live_build_panel.corpus.cache_write_degraded",
        [CacheWriteDegradedPath, CacheWriteDegradedReasonCode],
        new BppLogStormPolicy([CacheWriteDegradedReasonCode])
    );

    private static BppLogFieldDefinition Public(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, BppLogFieldPrivacy.Public, correlation, cardinality);
}
