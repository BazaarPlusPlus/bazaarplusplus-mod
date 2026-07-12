#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.EventPreview;

internal sealed class EventPreviewPlanController : MonoBehaviour
{
    private readonly CollectionEncounterPreviewPlanRegistry _registry = new();
    private readonly CancellationTokenSource _shutdown = new();
    private BppStaticCardMapProvider _cardMapProvider = null!;
    private CollectionEncounterPreviewCacheStore _cacheStore = null!;
    private CollectionEncounterPreviewPlanLoader _loader = null!;
    private string _gameBuild = string.Empty;
    private string _buildChannel = string.Empty;
    private object? _observedSource;
    private bool _initialized;

    public void Initialize(
        IBppServices services,
        BppStaticCardMapProvider cardMapProvider,
        string cachePath
    )
    {
        if (_initialized)
            return;
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        _cardMapProvider =
            cardMapProvider ?? throw new ArgumentNullException(nameof(cardMapProvider));
        _cacheStore = new CollectionEncounterPreviewCacheStore(cachePath);
        var compiler = new CollectionEncounterPreviewPlanCompiler();
        _loader = new CollectionEncounterPreviewPlanLoader(_cacheStore, compiler.Compile);
        _gameBuild = services.GameBuild.RawVersion;
        _buildChannel = services.GameBuild.Channel.ToString();
        _cacheStore.CleanupOrphanedTempFiles();
        EventPreviewPlanRuntime.Install(_registry);
        _initialized = true;
    }

    private void Update()
    {
        if (!_initialized)
            return;

        var source = BppStaticDataAccess.TryGetReadyManagerObject();
        if (source == null || ReferenceEquals(source, _observedSource))
            return;

        _observedSource = source;
        var generation = _registry.BeginGeneration(source);
        var sourceInfo = BppStaticDataAccess.TryCaptureGameDataSourceInfo(source);
        if (sourceInfo == null)
        {
            BppLog.Warn(
                "EncounterPreviewPlan",
                "GameData source paths unavailable; preview plans remain inactive."
            );
            return;
        }

        var token = _shutdown.Token;
        _ = Task.Run(() => LoadOrBuildAsync(source, sourceInfo, generation, token), token);
    }

    private async Task LoadOrBuildAsync(
        object source,
        BppGameDataSourceInfo sourceInfo,
        long generation,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var identity = CollectionEncounterPreviewIdentityResolver.Resolve(
                sourceInfo.ManifestPath,
                sourceInfo.DatabasePath,
                sourceInfo.DataBaseUrl,
                _gameBuild,
                _buildChannel
            );
            cancellationToken.ThrowIfCancellationRequested();
            if (!_registry.IsCurrent(source, generation))
                return;

            var loadResult = await _loader
                .LoadAsync(
                    identity,
                    () => _cardMapProvider.BeginLoad(source),
                    () => BppStaticDataAccess.SnapshotLevelUps(source),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!_registry.IsCurrent(source, generation))
                return;

            if (loadResult.WasCacheHit)
            {
                if (_registry.TryPublish(source, generation, loadResult.Snapshot))
                {
                    BppLog.Info(
                        "EncounterPreviewPlan",
                        $"Cache hit: events={loadResult.Snapshot.EventCount} levelUps={loadResult.Snapshot.LevelUpCount} templates={loadResult.Snapshot.TemplateCount} unsupportedLevelUpParts={loadResult.Snapshot.Coverage.UnsupportedLevelUpPartCount} missingTemplates={loadResult.Snapshot.Coverage.MissingReferencedTemplateCount} bytes={TryGetCacheBytes()} load={loadResult.CacheReadMilliseconds:F2}ms."
                    );
                }
                return;
            }

            var compileResult = loadResult.CompileResult!;

            Exception? persistError = null;
            var persisted = false;
            var writeMs = 0d;
            var published = _registry.TryCommitAndPublish(
                source,
                generation,
                compileResult.Snapshot,
                () =>
                {
                    var writeStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        _cacheStore.Save(identity, compileResult.Snapshot);
                        persisted = true;
                    }
                    catch (Exception ex)
                    {
                        persistError = ex;
                    }
                    writeMs = ElapsedMilliseconds(writeStarted);
                }
            );
            if (!published)
                return;

            if (persistError != null)
            {
                BppLog.Error(
                    "EncounterPreviewPlan",
                    "Preview plans are active in memory, but the persistent cache write failed.",
                    persistError
                );
            }

            BppLog.Info(
                "EncounterPreviewPlan",
                $"Cache rebuilt: reason={loadResult.CacheMissReason} events={compileResult.Snapshot.EventCount} levelUps={compileResult.Snapshot.LevelUpCount} templates={compileResult.Snapshot.TemplateCount} eventFailures={compileResult.Snapshot.Coverage.EventFailureCount} levelUpFailures={compileResult.Snapshot.Coverage.LevelUpFailureCount} unsupportedLevelUpParts={compileResult.Snapshot.Coverage.UnsupportedLevelUpPartCount} missingTemplates={compileResult.Snapshot.Coverage.MissingReferencedTemplateCount} bytes={(persisted ? TryGetCacheBytes() : 0)} compile={loadResult.CompileMilliseconds:F2}ms write={writeMs:F2}ms."
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Plugin teardown or a superseded generation; no publication is allowed afterwards.
        }
        catch (Exception ex)
        {
            BppLog.Error("EncounterPreviewPlan", "Failed to load or build preview plans.", ex);
        }
    }

    private long TryGetCacheBytes()
    {
        try
        {
            return File.Exists(_cacheStore.CachePath)
                ? new FileInfo(_cacheStore.CachePath).Length
                : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;

    private void OnDestroy()
    {
        _shutdown.Cancel();
        _registry.Reset();
        EventPreviewPlanRuntime.Reset(_registry);
        _shutdown.Dispose();
        _initialized = false;
    }
}
