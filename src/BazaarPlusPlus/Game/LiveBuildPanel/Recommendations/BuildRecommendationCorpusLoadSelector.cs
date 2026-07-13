#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

internal static class BuildRecommendationCorpusLoadSelector
{
    internal static CorpusLoadResult Select(
        CorpusCacheLoadResult cache,
        CorpusTextLoadResult embedded,
        TenWinBuildCorpus? embeddedCorpus
    )
    {
        if (cache.Status == CorpusCacheLoadStatus.Loaded && cache.Corpus != null)
        {
            return new CorpusLoadResult(
                cache.Corpus,
                LiveBuildCorpusSource.Cache,
                cache.Expired ? LiveBuildCorpusReasonCode.StaleCache : null,
                cache.Expired,
                cache.Path,
                Exception: null,
                ShouldRefreshInBackground: cache.Expired
            );
        }

        if (embeddedCorpus != null)
        {
            var fallbackReason = cache.Status switch
            {
                CorpusCacheLoadStatus.Failed => LiveBuildCorpusReasonCode.CacheReadFailed,
                CorpusCacheLoadStatus.Invalid => LiveBuildCorpusReasonCode.CacheInvalid,
                _ => LiveBuildCorpusReasonCode.EmbeddedFallback,
            };
            return new CorpusLoadResult(
                embeddedCorpus,
                LiveBuildCorpusSource.Embedded,
                fallbackReason,
                Expired: false,
                cache.Path,
                cache.Exception,
                ShouldRefreshInBackground: true
            );
        }

        var embeddedReason =
            embedded.Status == CorpusTextLoadStatus.Missing
                ? LiveBuildCorpusReasonCode.EmbeddedMissing
                : LiveBuildCorpusReasonCode.EmbeddedInvalid;
        return new CorpusLoadResult(
            Corpus: null,
            LiveBuildCorpusSource.Unavailable,
            embeddedReason,
            Expired: false,
            cache.Path,
            embedded.Exception,
            ShouldRefreshInBackground: true
        );
    }
}
