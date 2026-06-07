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
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.BuildRecommendations;

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
        "十勝陣容",
        "十勝陣容"
    );
    private static readonly TimeSpan TenWinBuildsCacheDuration = TimeSpan.FromHours(20);
    private static readonly HttpClient TenWinHttpClient = BppHttpClientFactory.Create(
        productVersion: BppPluginVersion.Current,
        userAgentSuffix: "TenWinBuildRepository",
        timeout: TimeSpan.FromSeconds(10)
    );
    private static readonly object SyncRoot = new();
    private static TenWinBuildCorpus? _corpus;
    private static bool _attemptedLoad;
    private static string? _cacheFilePath;
    private static Func<DateTime> _utcNow = () => DateTime.UtcNow;
    private static Func<string, string> _downloadJson = DownloadJson;
    private static Func<string?> _loadEmbeddedJson = LoadEmbeddedTenWinJson;
    private static Action<Action> _queueBackgroundRefresh = QueueBackgroundRefresh;
    private static bool _backgroundRefreshInProgress;

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
            var staticData = BppStaticDataAccess.TryGet();
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

    // ---- Corpus loading / cache / remote refresh --------------------------

    internal static TenWinBuildCorpus? EnsureCorpus()
    {
        EnsureLoaded();
        return _corpus;
    }

    private static void EnsureLoaded()
    {
        var shouldRefreshInBackground = false;
        lock (SyncRoot)
        {
            if (_attemptedLoad)
                return;

            _attemptedLoad = true;
            _corpus = LoadCorpus(out shouldRefreshInBackground);
        }

        if (shouldRefreshInBackground)
            TryQueueRefreshFromRemote("cache_stale_or_missing");
    }

    private static TenWinBuildCorpus? LoadCorpus(out bool shouldRefreshInBackground)
    {
        shouldRefreshInBackground = false;

        if (TryLoadCache(allowExpired: false, out var freshCorpus))
            return freshCorpus;

        if (TryLoadCache(allowExpired: true, out var staleCorpus))
        {
            shouldRefreshInBackground = true;
            BppLog.Info(
                "BuildRecommendationRepository",
                "Using expired ten-win builds cache; remote refresh was queued in the background."
            );
            return staleCorpus;
        }

        // Cold start with no cache: seed from the bundled corpus (same compact format, same parser)
        // so the panel is never empty offline, and still queue a remote refresh.
        shouldRefreshInBackground = true;
        var embeddedJson = _loadEmbeddedJson();
        return string.IsNullOrWhiteSpace(embeddedJson)
            ? null
            : DeserializeCorpus(embeddedJson!, "embedded");
    }

    private static string? LoadEmbeddedTenWinJson()
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
            {
                BppLog.Warn(
                    "BuildRecommendationRepository",
                    "Embedded ten-win builds seed resource was not found."
                );
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Failed to load embedded ten-win builds seed: {ex.Message}"
            );
            return null;
        }
    }

    internal static bool TryRefreshFinalBuildsFromRemote(out string? error)
    {
        if (!TryLoadRemote(out var remoteCorpus, out error) || remoteCorpus == null)
            return false;

        lock (SyncRoot)
        {
            _corpus = remoteCorpus;
            _attemptedLoad = true;
        }

        return true;
    }

    private static void TryQueueRefreshFromRemote(string reason)
    {
        if (!TryBeginBackgroundRefresh())
            return;

        try
        {
            _queueBackgroundRefresh(() => RefreshFromRemoteInBackground(reason));
            BppLog.Info(
                "BuildRecommendationRepository",
                $"Queued background ten-win builds refresh reason={reason}."
            );
        }
        catch (Exception ex)
        {
            EndBackgroundRefresh();
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Failed to queue background ten-win builds refresh reason={reason}: {ex.Message}"
            );
        }
    }

    private static bool TryBeginBackgroundRefresh()
    {
        lock (SyncRoot)
        {
            if (_backgroundRefreshInProgress)
                return false;

            _backgroundRefreshInProgress = true;
            return true;
        }
    }

    private static void EndBackgroundRefresh()
    {
        lock (SyncRoot)
        {
            _backgroundRefreshInProgress = false;
        }
    }

    private static void RefreshFromRemoteInBackground(string reason)
    {
        try
        {
            if (TryLoadRemote(out var remoteCorpus, out var error) && remoteCorpus != null)
            {
                lock (SyncRoot)
                {
                    _corpus = remoteCorpus;
                    _attemptedLoad = true;
                }

                BppLog.Info(
                    "BuildRecommendationRepository",
                    $"Background ten-win builds refresh succeeded reason={reason}."
                );
                return;
            }

            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Background ten-win builds refresh failed reason={reason} error={error ?? "unknown"}."
            );

            // Cold start with no usable corpus: allow a later query to retry the fetch.
            lock (SyncRoot)
            {
                if (_corpus == null)
                    _attemptedLoad = false;
            }
        }
        finally
        {
            EndBackgroundRefresh();
        }
    }

    private static bool TryLoadCache(bool allowExpired, out TenWinBuildCorpus? corpus)
    {
        corpus = null;

        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            if (!File.Exists(cacheFilePath))
                return false;

            var lastWriteUtc = File.GetLastWriteTimeUtc(cacheFilePath);
            var expiresAtUtc = lastWriteUtc.Add(TenWinBuildsCacheDuration);
            if (!allowExpired && _utcNow() >= expiresAtUtc)
                return false;

            var json = File.ReadAllText(cacheFilePath);
            corpus = DeserializeCorpus(json, "cache");
            if (corpus == null)
                return false;

            BppLog.Info(
                "BuildRecommendationRepository",
                $"Loaded ten-win builds from cache path={cacheFilePath} "
                    + $"expired={_utcNow() >= expiresAtUtc} expiresAtUtc={expiresAtUtc:O}"
            );
            return true;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Failed to read ten-win builds cache {ResolveCacheFilePath()}: {ex.Message}"
            );
            return false;
        }
    }

    private static bool TryLoadRemote(out TenWinBuildCorpus? corpus, out string? error)
    {
        corpus = null;
        error = null;

        try
        {
            var json = _downloadJson(TenWinBuildsRemoteUrl);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "empty_response";
                return false;
            }

            corpus = DeserializeCorpus(json, "remote");
            if (corpus == null)
            {
                error = "invalid_response";
                return false;
            }

            TryWriteCache(json);
            BppLog.Info(
                "BuildRecommendationRepository",
                $"Loaded ten-win builds from remote url={TenWinBuildsRemoteUrl}"
            );
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Failed to refresh ten-win builds from {TenWinBuildsRemoteUrl}: {ex.Message}"
            );
            return false;
        }
    }

    private static TenWinBuildCorpus? DeserializeCorpus(string json, string source)
    {
        var corpus = TenWinBuildCorpus.Parse(json);
        if (corpus == null)
        {
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Ten-win builds JSON from {source} was missing or malformed."
            );
        }

        return corpus;
    }

    private static void TryWriteCache(string json)
    {
        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            var cacheDirectory = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
                Directory.CreateDirectory(cacheDirectory);

            File.WriteAllText(cacheFilePath, json);
            File.SetLastWriteTimeUtc(cacheFilePath, _utcNow());
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BuildRecommendationRepository",
                $"Failed to write ten-win builds cache {ResolveCacheFilePath()}: {ex.Message}"
            );
        }
    }

    private static string ResolveCacheFilePath()
    {
        return _cacheFilePath ?? BuildDefaultTenWinCacheFilePath(BepInEx.Paths.GameRootPath);
    }

    private static string BuildDefaultTenWinCacheFilePath(string gameRootPath)
    {
        return Path.Combine(gameRootPath, "BazaarPlusPlusV4", TenWinBuildsCacheFileName);
    }

    private static string DownloadJson(string url)
    {
        return TenWinHttpClient.GetStringAsync(url).GetAwaiter().GetResult();
    }

    private static void QueueBackgroundRefresh(Action refresh)
    {
        _ = Task.Run(refresh);
    }

    // ---- Test hooks -------------------------------------------------------

    private static void ConfigureTenWinRemoteForTests(
        string cacheFilePath,
        Func<DateTime> utcNow,
        Func<string, string> downloadJson
    )
    {
        lock (SyncRoot)
        {
            _corpus = null;
            _attemptedLoad = false;
            _backgroundRefreshInProgress = false;
            _cacheFilePath = cacheFilePath;
            _utcNow = utcNow;
            _downloadJson = downloadJson;
            _loadEmbeddedJson = () => null;
            _queueBackgroundRefresh = QueueBackgroundRefresh;
        }
    }

    private static void ConfigureTenWinRemoteForTests(
        string cacheFilePath,
        Func<DateTime> utcNow,
        Func<string, string> downloadJson,
        Action<Action> queueBackgroundRefresh
    )
    {
        lock (SyncRoot)
        {
            _corpus = null;
            _attemptedLoad = false;
            _backgroundRefreshInProgress = false;
            _cacheFilePath = cacheFilePath;
            _utcNow = utcNow;
            _downloadJson = downloadJson;
            _loadEmbeddedJson = () => null;
            _queueBackgroundRefresh = queueBackgroundRefresh ?? QueueBackgroundRefresh;
        }
    }

    private static string? ReadEmbeddedSeedForTests() => LoadEmbeddedTenWinJson();

    private static void SetEmbeddedJsonForTests(Func<string?> loadEmbeddedJson)
    {
        lock (SyncRoot)
        {
            _corpus = null;
            _attemptedLoad = false;
            _loadEmbeddedJson = loadEmbeddedJson ?? (() => null);
        }
    }

    private static void ResetTenWinRemoteForTests()
    {
        lock (SyncRoot)
        {
            _corpus = null;
            _attemptedLoad = false;
            _backgroundRefreshInProgress = false;
            _cacheFilePath = null;
            _utcNow = () => DateTime.UtcNow;
            _downloadJson = DownloadJson;
            _loadEmbeddedJson = LoadEmbeddedTenWinJson;
            _queueBackgroundRefresh = QueueBackgroundRefresh;
        }
    }
}
