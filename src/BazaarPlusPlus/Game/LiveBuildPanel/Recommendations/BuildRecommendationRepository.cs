#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>
/// Loads the analyzer-v4 ten-win build corpus (cached locally with a background remote refresh),
/// answers recommendation queries against the live state, and projects matched builds onto
/// renderable item boards. The corpus is a static package — recommendation queries are answered
/// from the local copy and never hit the server.
/// </summary>
internal sealed class BuildRecommendationRepository
{
    private const string TenWinBuildsRemoteUrl =
        "https://bpp-metrics.bazaarplusplus.com/analyzer-v4/mod/tenwin_builds.json";
    private const string TenWinBuildsCacheFileName = "tenwin_builds.json";
    private static readonly LocalizedTextSet FinalBuildLabel = new(
        "Ten-Win Build",
        "十胜阵容",
        "十勝陣容"
    );
    private static readonly TimeSpan TenWinBuildsCacheDuration = TimeSpan.FromHours(20);
    private static readonly HttpClient TenWinHttpClient = BppHttpClientFactory.Create(
        productVersion: BppPluginVersion.Current,
        userAgentSuffix: "TenWinBuildRepository",
        timeout: TimeSpan.FromSeconds(10)
    );
    private readonly object _syncRoot = new();
    private BuildRecommendationCorpusLogState _corpusLogState = new();
    private TenWinBuildCorpus? _corpus;
    private LiveBuildCorpusSource _corpusSource = LiveBuildCorpusSource.Unavailable;
    private bool _attemptedLoad;
    private string? _cacheFilePath;
    private Func<DateTime> _utcNow = () => DateTime.UtcNow;
    private Func<string, Task<string>> _downloadJsonAsync = DownloadJsonAsync;
    private Func<CorpusTextLoadResult> _loadEmbeddedJson = LoadEmbeddedTenWinJson;
    private Action<Func<Task>> _queueBackgroundRefresh = QueueBackgroundRefresh;
    private bool _backgroundRefreshInProgress;
    private Task? _warmUpTask;

    public IReadOnlyList<BuildRecommendation> FindRecommendations(
        string? hero,
        IReadOnlyCollection<Guid> selectedTemplateIds,
        BuildLiveState? liveState = null
    )
    {
        var corpus = EnsureCorpus();
        if (corpus == null)
            return Array.Empty<BuildRecommendation>();

        var matches = corpus.FindBuilds(
            hero,
            selectedTemplateIds ?? Array.Empty<Guid>(),
            liveState ?? BuildLiveState.Empty
        );
        if (matches.Count == 0)
            return Array.Empty<BuildRecommendation>();

        var label = ResolveFinalBuildLabel();
        var results = new List<BuildRecommendation>(matches.Count);
        foreach (var match in matches)
        {
            var board = ProjectBoard(match.Build);
            if (board.Cards.Count == 0)
                continue;

            results.Add(
                new BuildRecommendation
                {
                    ModeLabel = label,
                    MatchedCardCount = match.MatchedSelectedCount,
                    TenWinRunCount = match.Build.Stats.TenWinRunCount,
                    TenWinRateBps = match.Build.Stats.TenWinRateBps,
                    P75TenWinFinalDay = match.Build.Stats.P75TenWinFinalDay,
                    Score = match.Build.Stats.Score,
                    Board = board,
                }
            );
        }

        for (var i = 0; i < results.Count; i++)
        {
            results[i].ResultIndex = i;
            results[i].ResultCount = results.Count;
        }

        return results;
    }

    private static BppItemBoard ProjectBoard(TenWinBuild build)
    {
        var cards = build
            .Layout.Where(item => item.TemplateId != Guid.Empty)
            .OrderBy(item => item.Slot ?? int.MaxValue)
            .Select(ProjectCard)
            .ToArray();

        return BppItemBoardSlotPlanner.Plan(
            new BppItemBoard(
                BppItemBoardId.FinalBuild,
                BppItemBoardType.Reference,
                cards,
                $"tenwin-build:{build.BuildId}"
            )
        );
    }

    private static BppItemBoardCard ProjectCard(TenWinLayoutItem item)
    {
        var size = ResolveCardSize(item.TemplateId, item.Size);
        return new BppItemBoardCard
        {
            TemplateId = item.TemplateId,
            InstanceId = $"tenwin-{(item.Slot?.ToString() ?? "unsocketed")}-{item.TemplateId:N}",
            Order = item.Slot ?? 0,
            Tier = MapTier(item.Tier),
            Size = size,
            Span = BppItemBoardSpan.Resolve(size),
            SourceSocketId = item.Slot.HasValue
                ? (EContainerSocketId?)Math.Clamp(item.Slot.Value, 0, 9)
                : null,
            EnchantmentType = MapEnchant(item.EnchantName),
        };
    }

    private static ECardSize ResolveCardSize(Guid templateId, int? size)
    {
        return size switch
        {
            1 => ECardSize.Small,
            2 => ECardSize.Medium,
            3 => ECardSize.Large,
            // Out-of-range/absent payload size: fall back to the game's authoritative card size.
            _ => ResolveCardSizeFromStaticData(templateId),
        };
    }

    private static ECardSize ResolveCardSizeFromStaticData(Guid templateId)
    {
        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var template = BppStaticDataAccess.GetCardTemplate(staticData, templateId);
            return template?.Size switch
            {
                ECardSize.Small => ECardSize.Small,
                ECardSize.Medium => ECardSize.Medium,
                ECardSize.Large => ECardSize.Large,
                _ => ECardSize.Small,
            };
        }
        catch
        {
            return ECardSize.Small;
        }
    }

    private static ETier MapTier(int? tier)
    {
        // Layout tier is the analyzer's mod value 1..5 (Bronze..Legendary); ETier is 0..4.
        var normalized = tier.GetValueOrDefault();
        if (normalized > 0)
            normalized--;

        normalized = Math.Clamp(normalized, (int)ETier.Bronze, (int)ETier.Legendary);
        return (ETier)normalized;
    }

    private static EEnchantmentType? MapEnchant(string? enchantName)
    {
        if (string.IsNullOrWhiteSpace(enchantName))
            return null;

        return Enum.TryParse<EEnchantmentType>(enchantName, true, out var type)
            ? type
            : (EEnchantmentType?)null;
    }

    private static string ResolveFinalBuildLabel() => L.Resolve(FinalBuildLabel);

    /// <summary>
    /// Snapshot of the currently loaded corpus's provenance (analyzer emission time, build/hero
    /// counts) for status surfaces; null while no corpus is loaded.
    /// </summary>
    public TenWinCorpusSummary? GetCorpusSummary()
    {
        var corpus = EnsureCorpus();
        return corpus == null
            ? (TenWinCorpusSummary?)null
            : new TenWinCorpusSummary(
                corpus.GeneratedAtUtc,
                corpus.BuildCount,
                corpus.HeroCount,
                corpus.HeroBuildCounts
            );
    }

    // ---- Corpus loading / cache / remote refresh --------------------------

    private TenWinBuildCorpus? EnsureCorpus()
    {
        EnsureLoaded();
        return _corpus;
    }

    /// <summary>
    /// Starts loading the ten-win build corpus on a background thread so the first panel open
    /// does not block the Unity main thread on file I/O and JSON parsing. Idempotent.
    /// Call from the panel's Awake() / Initialize() as early as possible.
    /// </summary>
    public void BeginCorpusLoad()
    {
        lock (_syncRoot)
        {
            if (_attemptedLoad || _warmUpTask != null)
                return;

            _warmUpTask = Task.Run(LoadCorpusInBackground);
        }
        _corpusLogState.ReportWarmupStarted();
    }

    private void LoadCorpusInBackground()
    {
        CorpusLoadResult result;
        try
        {
            result = LoadCorpus();
        }
        catch (Exception ex)
        {
            // This runs in a never-awaited Task: an unhandled throw would fault _warmUpTask and,
            // because both load guards short-circuit on "_warmUpTask != null", silently brick the
            // corpus for the whole session. Clear the marker so EnsureLoaded's synchronous fallback
            // (or a later cold-start re-arm) can still recover.
            lock (_syncRoot)
            {
                _warmUpTask = null;
            }
            _corpusLogState.ReportDegraded(
                new CorpusDegradation(
                    LiveBuildCorpusReasonCode.WarmupFailed,
                    LiveBuildCorpusSource.Unavailable,
                    0,
                    Expired: false,
                    CachePath: null,
                    ex
                )
            );
            return;
        }

        var installed = false;
        lock (_syncRoot)
        {
            if (!_attemptedLoad)
            {
                _corpus = result.Corpus;
                _corpusSource = result.Source;
                _attemptedLoad = true;
                installed = true;
            }
        }

        if (!installed)
            return;

        ReportInitialLoad(result, synchronousFallback: false);
        if (result.ShouldRefreshInBackground)
            TryQueueRefreshFromRemote(result.ReasonCode!.Value);
    }

    private void EnsureLoaded()
    {
        lock (_syncRoot)
        {
            // Warm-up task running or already finished — either way, don't block.
            if (_attemptedLoad || _warmUpTask != null)
                return;
        }

        // BeginCorpusLoad() was never called (unexpected path). Load synchronously as a
        // last-resort fallback.
        var result = LoadCorpus();
        var installed = false;
        lock (_syncRoot)
        {
            if (!_attemptedLoad)
            {
                _corpus = result.Corpus;
                _corpusSource = result.Source;
                _attemptedLoad = true;
                installed = true;
            }
        }

        if (!installed)
            return;

        ReportInitialLoad(result, synchronousFallback: true);
        if (result.ShouldRefreshInBackground)
            TryQueueRefreshFromRemote(result.ReasonCode!.Value);
    }

    private CorpusLoadResult LoadCorpus()
    {
        var cache = TryLoadCache();
        if (cache.Status == CorpusCacheLoadStatus.Loaded && cache.Corpus != null)
        {
            _corpusLogState.ReportCacheLoaded(cache.Corpus.BuildCount, cache.Expired, cache.Path!);
            return BuildRecommendationCorpusLoadSelector.Select(
                cache,
                CorpusTextLoadResult.Missing(),
                embeddedCorpus: null
            );
        }

        // Cold start with no cache: seed from the bundled corpus (same compact format, same parser)
        // so the panel is never empty offline, and still queue a remote refresh.
        var embedded = _loadEmbeddedJson();
        TenWinBuildCorpus? embeddedCorpus = null;
        if (
            embedded.Status == CorpusTextLoadStatus.Loaded
            && !string.IsNullOrWhiteSpace(embedded.Text)
        )
        {
            try
            {
                embeddedCorpus = TenWinBuildCorpus.Parse(embedded.Text!);
            }
            catch (Exception ex)
            {
                embedded = CorpusTextLoadResult.Failed(ex);
            }
        }

        return BuildRecommendationCorpusLoadSelector.Select(cache, embedded, embeddedCorpus);
    }

    private static CorpusTextLoadResult LoadEmbeddedTenWinJson()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name =>
                    name.EndsWith(TenWinBuildsCacheFileName, StringComparison.OrdinalIgnoreCase)
                );
            if (resourceName == null)
                return CorpusTextLoadResult.Missing();

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return CorpusTextLoadResult.Missing();

            using var reader = new StreamReader(stream);
            return CorpusTextLoadResult.Loaded(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            return CorpusTextLoadResult.Failed(ex);
        }
    }

    internal async Task<BuildRecommendationRemoteRefreshResult> TryRefreshFinalBuildsFromRemoteAsync()
    {
        var result = await LoadRemoteAsync().ConfigureAwait(false);
        if (result.Corpus == null)
        {
            return BuildRecommendationRemoteRefreshResult.Failure(
                result.FailureReason ?? LiveBuildRefreshFailureReasonCode.RefreshException,
                result.Error,
                result.Exception
            );
        }

        lock (_syncRoot)
        {
            _corpus = result.Corpus;
            _corpusSource = LiveBuildCorpusSource.Remote;
            _attemptedLoad = true;
        }
        // D46 is the sole Info/Error owner for a manual refresh. Clear any corpus degradation
        // episode and central storm keys without also emitting corpus.recovered.
        _corpusLogState.ResetDegradedSilently();

        return BuildRecommendationRemoteRefreshResult.Success();
    }

    private void ReportInitialLoad(CorpusLoadResult result, bool synchronousFallback)
    {
        if (result.ReasonCode.HasValue)
        {
            _corpusLogState.ReportDegraded(result.ToDegradation());
            return;
        }

        if (synchronousFallback)
        {
            _corpusLogState.ReportDegraded(
                new CorpusDegradation(
                    LiveBuildCorpusReasonCode.SynchronousFallback,
                    result.Source,
                    result.BuildCount,
                    result.Expired,
                    result.CachePath,
                    result.Exception
                )
            );
            return;
        }

        _corpusLogState.ReportReady(result.Source, result.BuildCount);
    }

    private void TryQueueRefreshFromRemote(LiveBuildCorpusReasonCode reason)
    {
        if (!TryBeginBackgroundRefresh())
            return;

        try
        {
            _queueBackgroundRefresh(RefreshFromRemoteInBackgroundAsync);
            _corpusLogState.ReportRefreshQueued(reason);
        }
        catch (Exception ex)
        {
            EndBackgroundRefresh();
            LiveBuildCorpusSource source;
            int buildCount;
            lock (_syncRoot)
            {
                source = _corpusSource;
                buildCount = _corpus?.BuildCount ?? 0;
            }
            _corpusLogState.ReportDegraded(
                new CorpusDegradation(
                    LiveBuildCorpusReasonCode.RefreshQueueFailed,
                    source,
                    buildCount,
                    Expired: false,
                    CachePath: null,
                    ex
                )
            );
        }
    }

    private bool TryBeginBackgroundRefresh()
    {
        lock (_syncRoot)
        {
            if (_backgroundRefreshInProgress)
                return false;

            _backgroundRefreshInProgress = true;
            return true;
        }
    }

    private void EndBackgroundRefresh()
    {
        lock (_syncRoot)
        {
            _backgroundRefreshInProgress = false;
        }
    }

    private async Task RefreshFromRemoteInBackgroundAsync()
    {
        try
        {
            var result = await LoadRemoteAsync().ConfigureAwait(false);
            if (result.Corpus != null)
            {
                lock (_syncRoot)
                {
                    _corpus = result.Corpus;
                    _corpusSource = LiveBuildCorpusSource.Remote;
                    _attemptedLoad = true;
                }

                _corpusLogState.ReportRecovered(
                    LiveBuildCorpusSource.Remote,
                    result.Corpus.BuildCount
                );
                return;
            }

            LiveBuildCorpusSource source;
            int buildCount;
            lock (_syncRoot)
            {
                source = _corpusSource;
                buildCount = _corpus?.BuildCount ?? 0;
            }
            _corpusLogState.ReportDegraded(
                new CorpusDegradation(
                    LiveBuildCorpusReasonCode.RemoteRefreshFailed,
                    source,
                    buildCount,
                    Expired: false,
                    CachePath: null,
                    result.Exception
                )
            );
        }
        finally
        {
            // Atomically clear the in-progress flag and, on a cold start with no usable corpus,
            // re-arm the one-shot load so a later query retries (and can re-queue) the fetch.
            // Doing both under one lock avoids a window where a racing query re-arms the load
            // while the refresh is still marked in-progress and so suppresses its own re-queue.
            lock (_syncRoot)
            {
                _backgroundRefreshInProgress = false;
                if (_corpus == null)
                {
                    _attemptedLoad = false;
                    // Clear the warm-up marker too; otherwise both load guards keep short-circuiting
                    // on "_warmUpTask != null" and this re-arm can never actually retry the load.
                    _warmUpTask = null;
                }
            }
        }
    }

    private CorpusCacheLoadResult TryLoadCache()
    {
        string? cacheFilePath = null;
        try
        {
            cacheFilePath = ResolveCacheFilePath();
            if (!File.Exists(cacheFilePath))
            {
                return new CorpusCacheLoadResult(
                    CorpusCacheLoadStatus.Missing,
                    Corpus: null,
                    Expired: false,
                    cacheFilePath,
                    Exception: null
                );
            }

            var lastWriteUtc = File.GetLastWriteTimeUtc(cacheFilePath);
            var expiresAtUtc = lastWriteUtc.Add(TenWinBuildsCacheDuration);
            var json = File.ReadAllText(cacheFilePath);
            var corpus = TenWinBuildCorpus.Parse(json);
            if (corpus == null)
            {
                return new CorpusCacheLoadResult(
                    CorpusCacheLoadStatus.Invalid,
                    Corpus: null,
                    Expired: false,
                    cacheFilePath,
                    Exception: null
                );
            }

            return new CorpusCacheLoadResult(
                CorpusCacheLoadStatus.Loaded,
                corpus,
                Expired: _utcNow() >= expiresAtUtc,
                cacheFilePath,
                Exception: null
            );
        }
        catch (Exception ex)
        {
            return new CorpusCacheLoadResult(
                CorpusCacheLoadStatus.Failed,
                Corpus: null,
                Expired: false,
                cacheFilePath,
                ex
            );
        }
    }

    private async Task<BuildRecommendationRemoteLoadResult> LoadRemoteAsync()
    {
        try
        {
            var json = await _downloadJsonAsync(TenWinBuildsRemoteUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return BuildRecommendationRemoteLoadResult.Failure(
                    LiveBuildRefreshFailureReasonCode.RemoteEmptyResponse,
                    "empty_response"
                );
            }

            var corpus = TenWinBuildCorpus.Parse(json);
            if (corpus == null)
            {
                return BuildRecommendationRemoteLoadResult.Failure(
                    LiveBuildRefreshFailureReasonCode.RemoteInvalidResponse,
                    "invalid_response"
                );
            }

            TryWriteCache(json);
            _corpusLogState.ReportRemoteLoaded(corpus.BuildCount);
            return BuildRecommendationRemoteLoadResult.Success(corpus);
        }
        catch (Exception ex)
        {
            return BuildRecommendationRemoteLoadResult.Failure(
                LiveBuildRefreshFailureReasonCode.RemoteRequestFailed,
                ex.Message,
                ex
            );
        }
    }

    private void TryWriteCache(string json)
    {
        string? cacheFilePath = null;
        try
        {
            cacheFilePath = ResolveCacheFilePath();
            var cacheDirectory = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
                Directory.CreateDirectory(cacheDirectory);

            File.WriteAllText(cacheFilePath, json);
            File.SetLastWriteTimeUtc(cacheFilePath, _utcNow());
            _corpusLogState.ReportCacheWriteRecovered();
        }
        catch (Exception ex)
        {
            _corpusLogState.ReportCacheWriteDegraded(cacheFilePath, ex);
        }
    }

    private string ResolveCacheFilePath()
    {
        return _cacheFilePath ?? BuildDefaultTenWinCacheFilePath(BepInEx.Paths.GameRootPath);
    }

    private static string BuildDefaultTenWinCacheFilePath(string gameRootPath)
    {
        return Path.Combine(gameRootPath, "BazaarPlusPlusV4", TenWinBuildsCacheFileName);
    }

    private static Task<string> DownloadJsonAsync(string url)
    {
        return TenWinHttpClient.GetStringAsync(url);
    }

    private static void QueueBackgroundRefresh(Func<Task> refresh)
    {
        _ = Task.Run(refresh);
    }

    // ---- Test hooks -------------------------------------------------------

    private void ConfigureTenWinRemoteForTests(
        string cacheFilePath,
        Func<DateTime> utcNow,
        Func<string, Task<string>> downloadJsonAsync
    )
    {
        lock (_syncRoot)
        {
            _corpus = null;
            _corpusSource = LiveBuildCorpusSource.Unavailable;
            _attemptedLoad = false;
            _backgroundRefreshInProgress = false;
            _warmUpTask = null;
            _cacheFilePath = cacheFilePath;
            _utcNow = utcNow;
            _downloadJsonAsync = downloadJsonAsync;
            _loadEmbeddedJson = CorpusTextLoadResult.Missing;
            _queueBackgroundRefresh = QueueBackgroundRefresh;
            _corpusLogState = new BuildRecommendationCorpusLogState();
        }
    }

    private void ConfigureTenWinRemoteForTests(
        string cacheFilePath,
        Func<DateTime> utcNow,
        Func<string, Task<string>> downloadJsonAsync,
        Action<Func<Task>> queueBackgroundRefresh
    )
    {
        lock (_syncRoot)
        {
            _corpus = null;
            _corpusSource = LiveBuildCorpusSource.Unavailable;
            _attemptedLoad = false;
            _backgroundRefreshInProgress = false;
            _warmUpTask = null;
            _cacheFilePath = cacheFilePath;
            _utcNow = utcNow;
            _downloadJsonAsync = downloadJsonAsync;
            _loadEmbeddedJson = CorpusTextLoadResult.Missing;
            _queueBackgroundRefresh = queueBackgroundRefresh ?? QueueBackgroundRefresh;
            _corpusLogState = new BuildRecommendationCorpusLogState();
        }
    }

    private static string? ReadEmbeddedSeedForTests() => LoadEmbeddedTenWinJson().Text;

    private void SetEmbeddedJsonForTests(Func<string?> loadEmbeddedJson)
    {
        lock (_syncRoot)
        {
            _corpus = null;
            _corpusSource = LiveBuildCorpusSource.Unavailable;
            _attemptedLoad = false;
            _warmUpTask = null;
            _loadEmbeddedJson = () =>
            {
                try
                {
                    var json = loadEmbeddedJson?.Invoke();
                    return json == null
                        ? CorpusTextLoadResult.Missing()
                        : CorpusTextLoadResult.Loaded(json);
                }
                catch (Exception ex)
                {
                    return CorpusTextLoadResult.Failed(ex);
                }
            };
            _corpusLogState = new BuildRecommendationCorpusLogState();
        }
    }

    private void ResetTenWinRemoteForTests()
    {
        lock (_syncRoot)
        {
            _corpus = null;
            _corpusSource = LiveBuildCorpusSource.Unavailable;
            _attemptedLoad = false;
            _backgroundRefreshInProgress = false;
            _warmUpTask = null;
            _cacheFilePath = null;
            _utcNow = () => DateTime.UtcNow;
            _downloadJsonAsync = DownloadJsonAsync;
            _loadEmbeddedJson = LoadEmbeddedTenWinJson;
            _queueBackgroundRefresh = QueueBackgroundRefresh;
            _corpusLogState = new BuildRecommendationCorpusLogState();
        }
    }
}
