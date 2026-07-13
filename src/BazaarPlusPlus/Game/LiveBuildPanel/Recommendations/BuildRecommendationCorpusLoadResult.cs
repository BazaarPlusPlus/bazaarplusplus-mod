#nullable enable
using System;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

internal enum CorpusTextLoadStatus
{
    Missing,
    Loaded,
    Failed,
}

internal readonly record struct CorpusTextLoadResult(
    CorpusTextLoadStatus Status,
    string? Text,
    Exception? Exception
)
{
    internal static CorpusTextLoadResult Missing() => new(CorpusTextLoadStatus.Missing, null, null);

    internal static CorpusTextLoadResult Loaded(string? text) =>
        new(CorpusTextLoadStatus.Loaded, text, null);

    internal static CorpusTextLoadResult Failed(Exception exception) =>
        new(CorpusTextLoadStatus.Failed, null, exception);
}

internal enum CorpusCacheLoadStatus
{
    Missing,
    Loaded,
    Invalid,
    Failed,
}

internal readonly record struct CorpusCacheLoadResult(
    CorpusCacheLoadStatus Status,
    TenWinBuildCorpus? Corpus,
    bool Expired,
    string? Path,
    Exception? Exception
);

internal readonly record struct CorpusLoadResult(
    TenWinBuildCorpus? Corpus,
    LiveBuildCorpusSource Source,
    LiveBuildCorpusReasonCode? ReasonCode,
    bool Expired,
    string? CachePath,
    Exception? Exception,
    bool ShouldRefreshInBackground
)
{
    internal int BuildCount => Corpus?.BuildCount ?? 0;

    internal CorpusDegradation ToDegradation() =>
        new(
            ReasonCode ?? throw new InvalidOperationException("A degradation reason is required."),
            Source,
            BuildCount,
            Expired,
            CachePath,
            Exception
        );
}

internal readonly record struct BuildRecommendationRemoteLoadResult(
    TenWinBuildCorpus? Corpus,
    LiveBuildRefreshFailureReasonCode? FailureReason,
    string? Error,
    Exception? Exception
)
{
    internal static BuildRecommendationRemoteLoadResult Success(TenWinBuildCorpus corpus) =>
        new(corpus, null, null, null);

    internal static BuildRecommendationRemoteLoadResult Failure(
        LiveBuildRefreshFailureReasonCode reason,
        string? error,
        Exception? exception = null
    ) => new(null, reason, error, exception);
}
