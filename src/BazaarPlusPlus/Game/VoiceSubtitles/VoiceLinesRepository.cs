#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Http;
using BepInEx;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceLinesRepository
{
    private const string VoiceLinesRemoteUrl =
        "https://bazaarline-installer.bazaarplusplus.com/data/voice-lines.json";
    private const string VoiceLinesCacheFileName = "voice-lines.json";
    private const string EmbeddedResourceName =
        "BazaarPlusPlus.Data.VoiceSubtitles.voice-lines.json";
    private static readonly TimeSpan VoiceLinesCacheDuration = TimeSpan.FromHours(20);
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
    private Func<string, Task<string>> _downloadJsonAsync = DownloadJsonAsync;
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

        if (TryLoadCache(allowExpired: false, out var freshLines))
            return freshLines;

        if (TryLoadCache(allowExpired: true, out var staleLines))
        {
            shouldRefreshInBackground = true;
            VoiceSubtitlesLog.Info(
                "Using expired voice subtitle cache; remote refresh was queued in the background."
            );
            return staleLines;
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

    private bool TryLoadCache(bool allowExpired, out LoadedVoiceLines? loaded)
    {
        loaded = null;

        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            if (!File.Exists(cacheFilePath))
                return false;

            var lastWriteUtc = File.GetLastWriteTimeUtc(cacheFilePath);
            var expiresAtUtc = lastWriteUtc.Add(VoiceLinesCacheDuration);
            if (!allowExpired && _utcNow() >= expiresAtUtc)
                return false;

            var json = File.ReadAllText(cacheFilePath, new UTF8Encoding(false));
            loaded = DeserializeVoiceLines(json, "cache");
            if (loaded == null)
                return false;

            VoiceSubtitlesLog.Info(
                $"Loaded voice subtitles from cache path={cacheFilePath} "
                    + $"expired={_utcNow() >= expiresAtUtc} expiresAtUtc={expiresAtUtc:O}"
            );
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

    private async Task<(LoadedVoiceLines? Loaded, string? Error)> LoadRemoteAsync()
    {
        try
        {
            var json = await _downloadJsonAsync(VoiceLinesRemoteUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return (null, "empty_response");

            var loaded = DeserializeVoiceLines(json, "remote");
            if (loaded == null)
                return (null, "invalid_response");

            TryWriteCache(json);
            VoiceSubtitlesLog.Info(
                $"Loaded voice subtitles from remote url={VoiceLinesRemoteUrl} count={loaded.Value.Lines.Length}"
            );
            return (loaded, null);
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Failed to refresh voice subtitles from {VoiceLinesRemoteUrl}: {ex.Message}"
            );
            return (null, ex.Message);
        }
    }

    private static LoadedVoiceLines? DeserializeVoiceLines(string json, string source)
    {
        try
        {
            var lines = VoiceLinesDocument.Parse(json, source);
            return new LoadedVoiceLines(lines, source);
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
            var (loaded, error) = await LoadRemoteAsync().ConfigureAwait(false);
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

    private void TryWriteCache(string json)
    {
        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            var cacheDirectory = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
                Directory.CreateDirectory(cacheDirectory);

            File.WriteAllText(cacheFilePath, json, new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(cacheFilePath, _utcNow());
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn(
                $"Failed to write voice subtitles cache {ResolveCacheFilePath()}: {ex.Message}"
            );
        }
    }

    private string ResolveCacheFilePath()
    {
        return _cacheFilePath ?? BuildDefaultVoiceLinesCacheFilePath(Paths.GameRootPath);
    }

    private static string BuildDefaultVoiceLinesCacheFilePath(string gameRootPath)
    {
        return Path.Combine(gameRootPath, "BazaarPlusPlusV4", VoiceLinesCacheFileName);
    }

    private static Task<string> DownloadJsonAsync(string url)
    {
        return VoiceLinesHttpClient.GetStringAsync(url);
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
        Func<string, Task<string>> downloadJsonAsync,
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
            _downloadJsonAsync = downloadJsonAsync;
            _loadEmbeddedJson = LoadEmbeddedSeedJson;
            _queueBackgroundRefresh = queueBackgroundRefresh ?? QueueBackgroundRefresh;
        }
    }

    private readonly struct LoadedVoiceLines
    {
        public LoadedVoiceLines(VoiceLine[] lines, string catalogName)
        {
            Lines = lines;
            CatalogName = catalogName;
        }

        public VoiceLine[] Lines { get; }

        public string CatalogName { get; }
    }
}
