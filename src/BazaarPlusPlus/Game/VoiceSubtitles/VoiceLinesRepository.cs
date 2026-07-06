#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Http;
using BepInEx;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceLinesRepository
{
    private const string VoiceLinesRemoteUrl =
        "https://bazaarline-installer.bazaarplusplus.com/data/voice-lines.json";
    private const string VoiceLinesCacheFileName = "voice-lines.json";
    private const string VoiceLinesCacheMetadataFileName = "voice-lines.meta.json";
    private const string EmbeddedResourceName =
        "BazaarPlusPlus.Data.VoiceSubtitles.voice-lines.json";
    private static readonly HttpClient VoiceLinesHttpClient = BppHttpClientFactory.Create(
        productVersion: BppPluginVersion.Current,
        userAgentSuffix: "VoiceSubtitlesRepository",
        timeout: TimeSpan.FromSeconds(10)
    );
    private readonly object _syncRoot = new();
    private bool _attemptedLoad;
    private bool _backgroundRefreshInProgress;
    private bool _hasLoadedCatalog;
    private Task? _warmUpTask;
    private string? _cacheFilePath;
    private Func<DateTime> _utcNow = () => DateTime.UtcNow;
    private Func<VoiceLinesRemoteRequest, Task<VoiceLinesRemoteResponse>> _downloadAsync =
        DownloadAsync;
    private Func<string?> _loadEmbeddedJson = LoadEmbeddedSeedJson;
    private Action<Func<Task>> _queueBackgroundRefresh = QueueBackgroundRefresh;

    public void BeginLoad()
    {
        lock (_syncRoot)
        {
            if (_attemptedLoad || _warmUpTask != null)
                return;

            _warmUpTask = Task.Run(LoadVoiceLinesInBackground);
        }

        VoiceSubtitlesLog.Debug("Voice subtitle catalog warm-up task started.");
    }

    internal static VoiceLine[] LoadEmbeddedSeed()
    {
        var json =
            LoadEmbeddedSeedJson()
            ?? throw new FileNotFoundException(
                $"Embedded voice subtitle seed '{EmbeddedResourceName}' was not found."
            );
        var lines = VoiceLinesDocument.Parse(json, EmbeddedResourceName);
        VoiceSubtitlesLog.Info($"Loaded {lines.Length} embedded voice subtitle lines.");
        return lines;
    }

    private void LoadVoiceLinesInBackground()
    {
        LoadedVoiceLines? loaded;
        bool shouldRefreshInBackground;
        try
        {
            loaded = LoadVoiceLines(out shouldRefreshInBackground);
        }
        catch (Exception ex)
        {
            lock (_syncRoot)
            {
                _warmUpTask = null;
            }
            VoiceSubtitlesLog.Warn($"Voice subtitle catalog warm-up failed: {ex.Message}");
            return;
        }

        ApplyLoadedVoiceLines(loaded);
        VoiceSubtitlesLog.Info(
            "Voice subtitle catalog warm-up complete: "
                + (
                    loaded != null
                        ? $"{loaded.Value.Lines.Length} lines source={loaded.Value.CatalogName}"
                        : "no catalog"
                )
        );

        if (shouldRefreshInBackground)
            TryQueueRefreshFromRemote("cache_stale_or_missing");
    }

    private LoadedVoiceLines? LoadVoiceLines(out bool shouldRefreshInBackground)
    {
        shouldRefreshInBackground = false;

        if (TryLoadCache(out var cachedLines))
        {
            shouldRefreshInBackground = true;
            return cachedLines;
        }

        shouldRefreshInBackground = true;
        var embeddedJson = _loadEmbeddedJson();
        return string.IsNullOrWhiteSpace(embeddedJson)
            ? null
            : DeserializeVoiceLines(embeddedJson!, "embedded");
    }

    private void ApplyLoadedVoiceLines(LoadedVoiceLines? loaded)
    {
        lock (_syncRoot)
        {
            if (_attemptedLoad)
                return;

            if (loaded != null)
            {
                VoiceLineCatalog.ReplaceCatalog(loaded.Value.Lines, loaded.Value.CatalogName);
                _hasLoadedCatalog = true;
            }
            else
            {
                VoiceLineCatalog.Reset();
                _hasLoadedCatalog = false;
            }

            _attemptedLoad = true;
        }
    }

    private bool TryLoadCache(out LoadedVoiceLines? loaded)
    {
        loaded = null;

        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            if (!File.Exists(cacheFilePath))
                return false;

            var json = File.ReadAllText(cacheFilePath, new UTF8Encoding(false));
            loaded = DeserializeVoiceLines(json, "cache");
            if (loaded == null)
                return false;

            VoiceSubtitlesLog.Info($"Loaded voice subtitles from cache path={cacheFilePath}");
            return true;
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Failed to read voice subtitles cache {ResolveCacheFilePath()}: {ex.Message}"
            );
            return false;
        }
    }

    private async Task<(LoadedVoiceLines? Loaded, string? Error, bool NotModified)> LoadRemoteAsync()
    {
        var metadata = HasValidCacheForConditionalRefresh() ? TryReadCacheMetadata() : null;
        try
        {
            var response = await _downloadAsync(
                    new VoiceLinesRemoteRequest(
                        VoiceLinesRemoteUrl,
                        metadata?.ETag,
                        metadata?.LastModified
                    )
                )
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                TryWriteCacheMetadata(metadata, response);
                VoiceSubtitlesLog.Info(
                    $"Voice subtitles remote returned 304 Not Modified url={VoiceLinesRemoteUrl}"
                );
                return (null, null, notModified: true);
            }

            if (response.StatusCode != HttpStatusCode.OK)
                return (null, $"http_{(int)response.StatusCode}", notModified: false);

            var json = response.Body;
            if (string.IsNullOrWhiteSpace(json))
                return (null, "empty_response", notModified: false);

            var loaded = DeserializeVoiceLines(json, "remote");
            if (loaded == null)
                return (null, "invalid_response", notModified: false);

            TryWriteCache(json, loaded.Value.ContentHash, metadata, response);
            VoiceSubtitlesLog.Info(
                $"Loaded voice subtitles from remote url={VoiceLinesRemoteUrl} count={loaded.Value.Lines.Length}"
            );
            return (loaded, null, notModified: false);
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Failed to refresh voice subtitles from {VoiceLinesRemoteUrl}: {ex.Message}"
            );
            return (null, ex.Message, notModified: false);
        }
    }

    private static LoadedVoiceLines? DeserializeVoiceLines(string json, string source)
    {
        try
        {
            var lines = VoiceLinesDocument.Parse(json, source);
            return new LoadedVoiceLines(lines, source, VoiceLinesDocument.ComputeContentHash(lines));
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Voice subtitle JSON from {source} was missing or malformed: {ex.Message}"
            );
            return null;
        }
    }

    private void TryQueueRefreshFromRemote(string reason)
    {
        if (!TryBeginBackgroundRefresh())
            return;

        try
        {
            _queueBackgroundRefresh(() => RefreshFromRemoteInBackgroundAsync(reason));
            VoiceSubtitlesLog.Info($"Queued background voice subtitles refresh reason={reason}.");
        }
        catch (Exception ex)
        {
            EndBackgroundRefresh();
            VoiceSubtitlesLog.Warn(
                $"Failed to queue background voice subtitles refresh reason={reason}: {ex.Message}"
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

    private async Task RefreshFromRemoteInBackgroundAsync(string reason)
    {
        try
        {
            var (loaded, error, notModified) = await LoadRemoteAsync().ConfigureAwait(false);
            if (notModified)
            {
                VoiceSubtitlesLog.Info(
                    $"Background voice subtitles refresh found no changes reason={reason}."
                );
                return;
            }

            if (loaded != null)
            {
                lock (_syncRoot)
                {
                    VoiceLineCatalog.ReplaceCatalog(loaded.Value.Lines, loaded.Value.CatalogName);
                    _attemptedLoad = true;
                    _hasLoadedCatalog = true;
                }

                VoiceSubtitlesLog.Info(
                    $"Background voice subtitles refresh succeeded reason={reason}."
                );
                return;
            }

            VoiceSubtitlesLog.Warn(
                $"Background voice subtitles refresh failed reason={reason} error={error ?? "unknown"}."
            );
        }
        finally
        {
            lock (_syncRoot)
            {
                _backgroundRefreshInProgress = false;
                if (!_hasLoadedCatalog)
                {
                    _attemptedLoad = false;
                    _warmUpTask = null;
                }
            }
        }
    }

    private void TryWriteCache(
        string json,
        string contentHash,
        VoiceLinesCacheMetadata? currentMetadata,
        VoiceLinesRemoteResponse remoteResponse
    )
    {
        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            var cacheDirectory = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
                Directory.CreateDirectory(cacheDirectory);

            File.WriteAllText(cacheFilePath, json, new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(cacheFilePath, _utcNow());
            TryWriteCacheMetadata(
                new VoiceLinesCacheMetadata
                {
                    ETag = currentMetadata?.ETag,
                    LastModified = currentMetadata?.LastModified,
                    ContentHash = contentHash,
                },
                remoteResponse
            );
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Failed to write voice subtitles cache {ResolveCacheFilePath()}: {ex.Message}"
            );
        }
    }

    private bool HasValidCacheForConditionalRefresh()
    {
        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            if (!File.Exists(cacheFilePath))
                return false;

            var json = File.ReadAllText(cacheFilePath, new UTF8Encoding(false));
            return DeserializeVoiceLines(json, "cache-conditional") != null;
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Failed to validate voice subtitles cache before conditional refresh {ResolveCacheFilePath()}: {ex.Message}"
            );
            return false;
        }
    }

    private VoiceLinesCacheMetadata? TryReadCacheMetadata()
    {
        try
        {
            var metadataPath = ResolveCacheMetadataFilePath();
            if (!File.Exists(metadataPath))
                return null;

            var json = File.ReadAllText(metadataPath, new UTF8Encoding(false));
            return JsonConvert.DeserializeObject<VoiceLinesCacheMetadata>(json);
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Failed to read voice subtitles cache metadata {ResolveCacheMetadataFilePath()}: {ex.Message}"
            );
            return null;
        }
    }

    private void TryWriteCacheMetadata(
        VoiceLinesCacheMetadata? currentMetadata,
        VoiceLinesRemoteResponse remoteResponse
    )
    {
        try
        {
            var metadataPath = ResolveCacheMetadataFilePath();
            var metadataDirectory = Path.GetDirectoryName(metadataPath);
            if (!string.IsNullOrWhiteSpace(metadataDirectory))
                Directory.CreateDirectory(metadataDirectory);

            var metadata = new VoiceLinesCacheMetadata
            {
                ETag = remoteResponse.ETag ?? currentMetadata?.ETag,
                LastModified = remoteResponse.LastModified ?? currentMetadata?.LastModified,
                ContentHash = currentMetadata?.ContentHash,
                CheckedAtUtc = _utcNow().ToString("O"),
            };
            var json = JsonConvert.SerializeObject(metadata, Formatting.Indented);
            File.WriteAllText(metadataPath, json, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Failed to write voice subtitles cache metadata {ResolveCacheMetadataFilePath()}: {ex.Message}"
            );
        }
    }

    private string ResolveCacheFilePath()
    {
        return _cacheFilePath ?? BuildDefaultVoiceLinesCacheFilePath(Paths.GameRootPath);
    }

    private string ResolveCacheMetadataFilePath()
    {
        var cacheFilePath = ResolveCacheFilePath();
        return Path.Combine(
            Path.GetDirectoryName(cacheFilePath) ?? string.Empty,
            VoiceLinesCacheMetadataFileName
        );
    }

    private static string BuildDefaultVoiceLinesCacheFilePath(string gameRootPath)
    {
        return Path.Combine(gameRootPath, "BazaarPlusPlusV4", VoiceLinesCacheFileName);
    }

    private static async Task<VoiceLinesRemoteResponse> DownloadAsync(VoiceLinesRemoteRequest request)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.Url);
        if (!string.IsNullOrWhiteSpace(request.ETag))
            httpRequest.Headers.TryAddWithoutValidation("If-None-Match", request.ETag);
        else if (!string.IsNullOrWhiteSpace(request.LastModified))
            httpRequest.Headers.TryAddWithoutValidation("If-Modified-Since", request.LastModified);

        using var response = await VoiceLinesHttpClient.SendAsync(httpRequest).ConfigureAwait(false);
        var body =
            response.StatusCode == HttpStatusCode.NotModified
                ? null
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new VoiceLinesRemoteResponse(
            response.StatusCode,
            body,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified?.ToString("R")
                ?? response.Headers.Date?.ToString("R")
        );
    }

    private static void QueueBackgroundRefresh(Func<Task> refresh)
    {
        _ = Task.Run(refresh);
    }

    private static string? LoadEmbeddedSeedJson()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream == null)
            return null;

        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false
        );
        return reader.ReadToEnd();
    }

    private (int Count, string Source, bool ShouldRefresh) LoadVoiceLinesForTests()
    {
        var loaded = LoadVoiceLines(out var shouldRefresh);
        return (loaded?.Lines.Length ?? 0, loaded?.CatalogName ?? "none", shouldRefresh);
    }

    private static string? ReadEmbeddedSeedJsonForTests() => LoadEmbeddedSeedJson();

    private void ConfigureVoiceLinesRemoteForTests(
        string cacheFilePath,
        Func<DateTime> utcNow,
        Func<
            string?,
            string?,
            Task<(HttpStatusCode StatusCode, string? Body, string? ETag, string? LastModified)>
        > downloadAsync,
        Action<Func<Task>> queueBackgroundRefresh
    )
    {
        lock (_syncRoot)
        {
            _attemptedLoad = false;
            _backgroundRefreshInProgress = false;
            _hasLoadedCatalog = false;
            _warmUpTask = null;
            _cacheFilePath = cacheFilePath;
            _utcNow = utcNow;
            _downloadAsync = async request =>
            {
                var response = await downloadAsync(request.ETag, request.LastModified)
                    .ConfigureAwait(false);
                return new VoiceLinesRemoteResponse(
                    response.StatusCode,
                    response.Body,
                    response.ETag,
                    response.LastModified
                );
            };
            _loadEmbeddedJson = LoadEmbeddedSeedJson;
            _queueBackgroundRefresh = queueBackgroundRefresh ?? QueueBackgroundRefresh;
        }
    }

    private Task<(LoadedVoiceLines? Loaded, string? Error, bool NotModified)> LoadRemoteForTests()
    {
        return LoadRemoteAsync();
    }

    private string BuildCacheMetadataFilePathForTests()
    {
        return ResolveCacheMetadataFilePath();
    }

    private readonly struct LoadedVoiceLines
    {
        public LoadedVoiceLines(VoiceLine[] lines, string catalogName, string contentHash)
        {
            Lines = lines;
            CatalogName = catalogName;
            ContentHash = contentHash;
        }

        public VoiceLine[] Lines { get; }

        public string CatalogName { get; }

        public string ContentHash { get; }
    }

    private sealed class VoiceLinesCacheMetadata
    {
        [JsonProperty("etag")]
        public string? ETag { get; set; }

        [JsonProperty("lastModified")]
        public string? LastModified { get; set; }

        [JsonProperty("contentHash")]
        public string? ContentHash { get; set; }

        [JsonProperty("checkedAtUtc")]
        public string? CheckedAtUtc { get; set; }
    }

    private readonly struct VoiceLinesRemoteRequest
    {
        public VoiceLinesRemoteRequest(string url, string? eTag, string? lastModified)
        {
            Url = url;
            ETag = eTag;
            LastModified = lastModified;
        }

        public string Url { get; }

        public string? ETag { get; }

        public string? LastModified { get; }
    }

    private readonly struct VoiceLinesRemoteResponse
    {
        public VoiceLinesRemoteResponse(
            HttpStatusCode statusCode,
            string? body,
            string? eTag,
            string? lastModified
        )
        {
            StatusCode = statusCode;
            Body = body;
            ETag = eTag;
            LastModified = lastModified;
        }

        public HttpStatusCode StatusCode { get; }

        public string? Body { get; }

        public string? ETag { get; }

        public string? LastModified { get; }
    }
}
