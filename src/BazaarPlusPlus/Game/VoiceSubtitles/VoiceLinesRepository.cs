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
    private VoiceCatalogState _catalogState = VoiceCatalogState.Loading;
    private VoiceCatalogDegradation? _activeDegradation;
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

            _catalogState = VoiceCatalogState.Loading;
            _activeDegradation = null;
            _warmUpTask = Task.Run(LoadVoiceLinesInBackground);
        }

        BppLog.DebugEvent(VoiceCatalogLogEvents.CatalogStarted, static () => []);
    }

    internal static VoiceLine[] LoadEmbeddedSeed()
    {
        var json =
            LoadEmbeddedSeedJson()
            ?? throw new FileNotFoundException(
                $"Embedded voice subtitle seed '{EmbeddedResourceName}' was not found."
            );
        return VoiceLinesDocument.Parse(json, VoiceCatalogSource.Embedded);
    }

    private void LoadVoiceLinesInBackground()
    {
        try
        {
            var outcome = LoadInitialCatalog();
            if (!ApplyInitialOutcome(outcome))
                return;

            if (outcome.ShouldRefresh)
                TryQueueRefreshFromRemote(GetRefreshReason(outcome));
        }
        catch (Exception ex)
        {
            ReportUnexpectedWarmUpFailure(ex);
        }
    }

    private VoiceCatalogLoadOutcome LoadInitialCatalog()
    {
        var cache = LoadCache();
        if (cache.Kind == VoiceCatalogSourceOutcomeKind.Fresh)
        {
            return VoiceCatalogLoadOutcome.Ready(
                cache.Lines!,
                VoiceCatalogSource.Cache,
                shouldRefresh: false
            );
        }

        if (cache.Kind == VoiceCatalogSourceOutcomeKind.Stale)
        {
            return VoiceCatalogLoadOutcome.Degraded(
                cache.Lines!,
                VoiceCatalogSource.Cache,
                VoiceCatalogSource.Cache,
                VoiceCatalogReasonCode.CacheStale,
                exception: null,
                shouldRefresh: true
            );
        }

        var embedded = LoadEmbedded();
        if (embedded.Kind == VoiceCatalogSourceOutcomeKind.Fresh)
        {
            if (cache.Kind == VoiceCatalogSourceOutcomeKind.Rejected)
            {
                return VoiceCatalogLoadOutcome.Degraded(
                    embedded.Lines!,
                    VoiceCatalogSource.Embedded,
                    VoiceCatalogSource.Cache,
                    cache.ReasonCode ?? VoiceCatalogReasonCode.SourceRejected,
                    cache.Exception,
                    shouldRefresh: true
                );
            }

            return VoiceCatalogLoadOutcome.Ready(
                embedded.Lines!,
                VoiceCatalogSource.Embedded,
                shouldRefresh: true
            );
        }

        var terminal =
            embedded.Kind == VoiceCatalogSourceOutcomeKind.Rejected ? embedded
            : cache.Kind == VoiceCatalogSourceOutcomeKind.Rejected ? cache
            : VoiceCatalogSourceOutcome.Rejected(
                VoiceCatalogSource.Embedded,
                VoiceCatalogReasonCode.NoUsableCatalog
            );
        return VoiceCatalogLoadOutcome.Failed(
            terminal.Source,
            terminal.ReasonCode ?? VoiceCatalogReasonCode.NoUsableCatalog,
            terminal.Exception,
            shouldRefresh: true
        );
    }

    private bool ApplyInitialOutcome(VoiceCatalogLoadOutcome outcome)
    {
        lock (_syncRoot)
        {
            if (_attemptedLoad)
                return false;

            if (outcome.Lines != null)
            {
                VoiceLineCatalog.ReplaceCatalog(outcome.Lines, CatalogName(outcome.CatalogSource));
                _hasLoadedCatalog = true;
            }
            else
            {
                VoiceLineCatalog.Reset();
                _hasLoadedCatalog = false;
            }

            _catalogState = outcome.Kind switch
            {
                VoiceCatalogLoadOutcomeKind.Ready => VoiceCatalogState.Ready,
                VoiceCatalogLoadOutcomeKind.Degraded => VoiceCatalogState.Degraded,
                VoiceCatalogLoadOutcomeKind.Failed => VoiceCatalogState.Failed,
                _ => VoiceCatalogState.Failed,
            };
            _activeDegradation =
                outcome.Kind == VoiceCatalogLoadOutcomeKind.Degraded
                    ? new VoiceCatalogDegradation(
                        outcome.ReasonCode ?? VoiceCatalogReasonCode.SourceRejected,
                        outcome.EventSource
                    )
                    : null;
            _attemptedLoad = true;
        }

        switch (outcome.Kind)
        {
            case VoiceCatalogLoadOutcomeKind.Ready:
                BppLog.InfoEvent(
                    VoiceCatalogLogEvents.CatalogReady,
                    VoiceCatalogLogEvents.CatalogReadySource.Bind(outcome.CatalogSource),
                    VoiceCatalogLogEvents.CatalogReadyLineCount.Bind(outcome.Lines?.Length ?? 0)
                );
                break;
            case VoiceCatalogLoadOutcomeKind.Degraded:
                EmitCatalogDegraded(
                    outcome.ReasonCode ?? VoiceCatalogReasonCode.SourceRejected,
                    outcome.EventSource,
                    outcome.Exception
                );
                break;
            case VoiceCatalogLoadOutcomeKind.Failed:
                EmitCatalogFailed(
                    outcome.ReasonCode ?? VoiceCatalogReasonCode.NoUsableCatalog,
                    outcome.EventSource,
                    outcome.Exception
                );
                break;
        }

        return true;
    }

    private VoiceCatalogSourceOutcome LoadCache()
    {
        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            if (!File.Exists(cacheFilePath))
                return VoiceCatalogSourceOutcome.Missing(VoiceCatalogSource.Cache);

            var now = _utcNow();
            var expiresAtUtc = File.GetLastWriteTimeUtc(cacheFilePath).Add(VoiceLinesCacheDuration);
            var json = File.ReadAllText(cacheFilePath, new UTF8Encoding(false));
            var parsed = DeserializeVoiceLines(json, VoiceCatalogSource.Cache);
            if (parsed.Kind == VoiceCatalogSourceOutcomeKind.Rejected)
                return parsed;

            return now >= expiresAtUtc
                ? VoiceCatalogSourceOutcome.Stale(VoiceCatalogSource.Cache, parsed.Lines!)
                : VoiceCatalogSourceOutcome.Fresh(VoiceCatalogSource.Cache, parsed.Lines!);
        }
        catch (Exception ex)
        {
            return VoiceCatalogSourceOutcome.Rejected(
                VoiceCatalogSource.Cache,
                VoiceCatalogReasonCode.SourceRejected,
                ex
            );
        }
    }

    private VoiceCatalogSourceOutcome LoadEmbedded()
    {
        try
        {
            var json = _loadEmbeddedJson();
            return string.IsNullOrWhiteSpace(json)
                ? VoiceCatalogSourceOutcome.Missing(VoiceCatalogSource.Embedded)
                : DeserializeVoiceLines(json!, VoiceCatalogSource.Embedded);
        }
        catch (Exception ex)
        {
            return VoiceCatalogSourceOutcome.Rejected(
                VoiceCatalogSource.Embedded,
                VoiceCatalogReasonCode.SourceRejected,
                ex
            );
        }
    }

    private async Task<VoiceCatalogRemoteOutcome> LoadRemoteAsync()
    {
        try
        {
            var json = await _downloadJsonAsync(VoiceLinesRemoteUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new VoiceCatalogRemoteOutcome(
                    VoiceCatalogSourceOutcome.Rejected(
                        VoiceCatalogSource.Remote,
                        VoiceCatalogReasonCode.EmptyResponse
                    ),
                    null
                );
            }

            var loaded = DeserializeVoiceLines(json, VoiceCatalogSource.Remote);
            if (loaded.Kind == VoiceCatalogSourceOutcomeKind.Rejected)
                return new VoiceCatalogRemoteOutcome(loaded, null);

            return new VoiceCatalogRemoteOutcome(loaded, TryWriteCache(json));
        }
        catch (Exception ex)
        {
            return new VoiceCatalogRemoteOutcome(
                VoiceCatalogSourceOutcome.Rejected(
                    VoiceCatalogSource.Remote,
                    VoiceCatalogReasonCode.RemoteFailed,
                    ex
                ),
                null
            );
        }
    }

    private static VoiceCatalogSourceOutcome DeserializeVoiceLines(
        string json,
        VoiceCatalogSource source
    )
    {
        try
        {
            return VoiceCatalogSourceOutcome.Fresh(source, VoiceLinesDocument.Parse(json, source));
        }
        catch (Exception ex)
        {
            return VoiceCatalogSourceOutcome.Rejected(
                source,
                VoiceCatalogReasonCode.SourceRejected,
                ex
            );
        }
    }

    private void TryQueueRefreshFromRemote(VoiceCatalogReasonCode reasonCode)
    {
        if (!TryBeginBackgroundRefresh())
            return;

        try
        {
            _queueBackgroundRefresh(RefreshFromRemoteInBackgroundAsync);
            BppLog.DebugEvent(
                VoiceCatalogLogEvents.CatalogRefreshStarted,
                () =>
                    [
                        VoiceCatalogLogEvents.CatalogRefreshStartedReasonCode.Bind(reasonCode),
                        VoiceCatalogLogEvents.CatalogRefreshStartedEndpoint.Bind(
                            VoiceCatalogEndpoint.VoiceCatalog
                        ),
                    ]
            );
        }
        catch (Exception ex)
        {
            EndBackgroundRefresh();
            ReportCatalogDegraded(
                VoiceCatalogReasonCode.RefreshQueueFailed,
                VoiceCatalogSource.Remote,
                ex
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
            var remote = await LoadRemoteAsync().ConfigureAwait(false);
            if (remote.CacheWriteException != null)
                ReportCacheWriteDegraded(remote.CacheWriteException);

            if (
                remote.SourceOutcome.Kind == VoiceCatalogSourceOutcomeKind.Fresh
                && remote.SourceOutcome.Lines != null
            )
            {
                ApplyRemoteCatalog(remote.SourceOutcome.Lines);
                return;
            }

            ReportCatalogDegraded(
                remote.SourceOutcome.ReasonCode ?? VoiceCatalogReasonCode.RemoteFailed,
                VoiceCatalogSource.Remote,
                remote.SourceOutcome.Exception
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
                    _catalogState = VoiceCatalogState.Loading;
                    _activeDegradation = null;
                }
            }
        }
    }

    private void ApplyRemoteCatalog(VoiceLine[] lines)
    {
        VoiceCatalogDegradation? recovered;
        lock (_syncRoot)
        {
            VoiceLineCatalog.ReplaceCatalog(lines, CatalogName(VoiceCatalogSource.Remote));
            _attemptedLoad = true;
            _hasLoadedCatalog = true;
            recovered = _catalogState == VoiceCatalogState.Degraded ? _activeDegradation : null;
            _catalogState = VoiceCatalogState.Ready;
            _activeDegradation = null;
        }

        if (!recovered.HasValue)
            return;

        BppLog.RecoverStorm(
            VoiceCatalogLogEvents.CatalogDegraded,
            VoiceCatalogLogEvents.CatalogDegradedReasonCode.Bind(recovered.Value.ReasonCode),
            VoiceCatalogLogEvents.CatalogDegradedSource.Bind(recovered.Value.Source)
        );
        BppLog.InfoEvent(
            VoiceCatalogLogEvents.CatalogRecovered,
            VoiceCatalogLogEvents.CatalogRecoveredReasonCode.Bind(recovered.Value.ReasonCode),
            VoiceCatalogLogEvents.CatalogRecoveredSource.Bind(recovered.Value.Source),
            VoiceCatalogLogEvents.CatalogRecoveredLineCount.Bind(lines.Length)
        );
    }

    private void ReportCatalogDegraded(
        VoiceCatalogReasonCode reasonCode,
        VoiceCatalogSource source,
        Exception? exception
    )
    {
        lock (_syncRoot)
        {
            if (
                _catalogState == VoiceCatalogState.Degraded
                || _catalogState == VoiceCatalogState.Failed
            )
            {
                return;
            }

            _catalogState = VoiceCatalogState.Degraded;
            _activeDegradation = new VoiceCatalogDegradation(reasonCode, source);
        }

        EmitCatalogDegraded(reasonCode, source, exception);
    }

    private static void EmitCatalogDegraded(
        VoiceCatalogReasonCode reasonCode,
        VoiceCatalogSource source,
        Exception? exception
    )
    {
        var fields = new[]
        {
            VoiceCatalogLogEvents.CatalogDegradedReasonCode.Bind(reasonCode),
            VoiceCatalogLogEvents.CatalogDegradedSource.Bind(source),
            VoiceCatalogLogEvents.CatalogDegradedEndpoint.Bind(VoiceCatalogEndpoint.VoiceCatalog),
        };
        if (exception == null)
            BppLog.WarnEvent(VoiceCatalogLogEvents.CatalogDegraded, fields);
        else
            BppLog.WarnEvent(VoiceCatalogLogEvents.CatalogDegraded, exception, fields);
    }

    private static void EmitCatalogFailed(
        VoiceCatalogReasonCode reasonCode,
        VoiceCatalogSource source,
        Exception? exception
    )
    {
        var fields = new[]
        {
            VoiceCatalogLogEvents.CatalogFailedReasonCode.Bind(reasonCode),
            VoiceCatalogLogEvents.CatalogFailedSource.Bind(source),
        };
        if (exception == null)
            BppLog.ErrorEvent(VoiceCatalogLogEvents.CatalogFailed, fields);
        else
            BppLog.ErrorEvent(VoiceCatalogLogEvents.CatalogFailed, exception, fields);
    }

    private void ReportUnexpectedWarmUpFailure(Exception exception)
    {
        lock (_syncRoot)
        {
            if (_catalogState == VoiceCatalogState.Failed && _attemptedLoad)
                return;

            _catalogState = VoiceCatalogState.Failed;
            _activeDegradation = null;
            _hasLoadedCatalog = false;
            _attemptedLoad = true;
        }

        EmitCatalogFailed(
            VoiceCatalogReasonCode.WarmUpException,
            VoiceCatalogSource.None,
            exception
        );
    }

    private static void ReportCacheWriteDegraded(Exception exception)
    {
        BppLog.WarnEvent(
            VoiceCatalogLogEvents.CatalogCacheDegraded,
            exception,
            VoiceCatalogLogEvents.CatalogCacheDegradedReasonCode.Bind(
                VoiceCatalogReasonCode.WriteFailed
            )
        );
    }

    private Exception? TryWriteCache(string json)
    {
        try
        {
            var cacheFilePath = ResolveCacheFilePath();
            var cacheDirectory = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
                Directory.CreateDirectory(cacheDirectory);

            File.WriteAllText(cacheFilePath, json, new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(cacheFilePath, _utcNow());
            return null;
        }
        catch (Exception ex)
        {
            return ex;
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

    private static string CatalogName(VoiceCatalogSource source) =>
        source switch
        {
            VoiceCatalogSource.Cache => "cache",
            VoiceCatalogSource.Embedded => "embedded",
            VoiceCatalogSource.Remote => "remote",
            _ => "none",
        };

    private static VoiceCatalogReasonCode GetRefreshReason(VoiceCatalogLoadOutcome outcome) =>
        outcome.ReasonCode
        ?? (
            outcome.CatalogSource == VoiceCatalogSource.Embedded
                ? VoiceCatalogReasonCode.CacheMissing
                : VoiceCatalogReasonCode.NoUsableCatalog
        );

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
        var outcome = LoadInitialCatalog();
        return (
            outcome.Lines?.Length ?? 0,
            CatalogName(outcome.CatalogSource),
            outcome.ShouldRefresh
        );
    }

    private static string? ReadEmbeddedSeedJsonForTests() => LoadEmbeddedSeedJson();

    private void ConfigureVoiceLinesRemoteForTests(
        string cacheFilePath,
        Func<DateTime> utcNow,
        Func<string, Task<string>> downloadJsonAsync,
        Action<Func<Task>> queueBackgroundRefresh
    )
    {
        ConfigureVoiceLinesSourcesForTests(
            cacheFilePath,
            utcNow,
            downloadJsonAsync,
            LoadEmbeddedSeedJson,
            queueBackgroundRefresh
        );
    }

    private void ConfigureVoiceLinesSourcesForTests(
        string cacheFilePath,
        Func<DateTime> utcNow,
        Func<string, Task<string>> downloadJsonAsync,
        Func<string?> loadEmbeddedJson,
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
            _catalogState = VoiceCatalogState.Loading;
            _activeDegradation = null;
            _utcNow = utcNow;
            _downloadJsonAsync = downloadJsonAsync;
            _loadEmbeddedJson = loadEmbeddedJson ?? LoadEmbeddedSeedJson;
            _queueBackgroundRefresh = queueBackgroundRefresh ?? QueueBackgroundRefresh;
        }
    }
}
