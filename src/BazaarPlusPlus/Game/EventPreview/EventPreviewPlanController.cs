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
using BazaarPlusPlus.Infrastructure.Logging;
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
    private int _healthDegraded;

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
            ReportTerminal(
                EventPreviewLogEvents.PlansLoadFailed,
                EventPreviewPlanSource.Unknown,
                EventPreviewPlanReasonCode.SourceInfoUnavailable,
                snapshot: null,
                sizeBytes: 0,
                loadDurationMs: 0,
                compileDurationMs: 0,
                writeDurationMs: 0,
                exception: null
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
                    ReportPublishedTerminal(
                        EventPreviewPlanSource.Cache,
                        loadResult.Snapshot,
                        persistError: null,
                        TryGetCacheBytes(),
                        loadResult.CacheReadMilliseconds,
                        compileDurationMs: 0,
                        writeDurationMs: 0
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

            ReportPublishedTerminal(
                EventPreviewPlanSource.Rebuild,
                compileResult.Snapshot,
                persistError,
                persisted ? TryGetCacheBytes() : 0,
                loadResult.CacheReadMilliseconds,
                loadResult.CompileMilliseconds,
                writeMs
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Plugin teardown or a superseded generation; no publication is allowed afterwards.
        }
        catch (Exception ex)
        {
            if (!_registry.IsCurrent(source, generation))
                return;
            ReportTerminal(
                EventPreviewLogEvents.PlansLoadFailed,
                EventPreviewPlanSource.Unknown,
                EventPreviewPlanReasonCode.LoadException,
                snapshot: null,
                sizeBytes: 0,
                loadDurationMs: 0,
                compileDurationMs: 0,
                writeDurationMs: 0,
                ex
            );
        }
    }

    private void ReportPublishedTerminal(
        EventPreviewPlanSource source,
        CollectionEncounterPreviewSnapshot snapshot,
        Exception? persistError,
        long sizeBytes,
        double loadDurationMs,
        double compileDurationMs,
        double writeDurationMs
    )
    {
        var coverage = snapshot.Coverage;
        var hasPartialCoverage =
            coverage.EventFailureCount > 0
            || coverage.LevelUpFailureCount > 0
            || coverage.UnsupportedLevelUpPartCount > 0
            || coverage.MissingReferencedTemplateCount > 0;
        if (persistError != null || hasPartialCoverage)
        {
            Interlocked.Exchange(ref _healthDegraded, 1);
            ReportTerminal(
                EventPreviewLogEvents.PlansDegraded,
                source,
                persistError != null
                    ? EventPreviewPlanReasonCode.CacheWriteException
                    : EventPreviewPlanReasonCode.PartialCoverage,
                snapshot,
                sizeBytes,
                loadDurationMs,
                compileDurationMs,
                writeDurationMs,
                persistError
            );
            return;
        }

        var recovered = Interlocked.Exchange(ref _healthDegraded, 0) != 0;
        if (recovered)
            BppLog.RecoverStorm(EventPreviewLogEvents.PlansDegraded);
        ReportTerminal(
            recovered ? EventPreviewLogEvents.PlansRecovered : EventPreviewLogEvents.PlansReady,
            source,
            EventPreviewPlanReasonCode.None,
            snapshot,
            sizeBytes,
            loadDurationMs,
            compileDurationMs,
            writeDurationMs,
            exception: null
        );
    }

    private void ReportTerminal(
        BppLogEventDefinition definition,
        EventPreviewPlanSource source,
        EventPreviewPlanReasonCode reasonCode,
        CollectionEncounterPreviewSnapshot? snapshot,
        long sizeBytes,
        double loadDurationMs,
        double compileDurationMs,
        double writeDurationMs,
        Exception? exception
    )
    {
        if (
            ReferenceEquals(definition, EventPreviewLogEvents.PlansLoadFailed)
            || ReferenceEquals(definition, EventPreviewLogEvents.PlansDegraded)
        )
        {
            Interlocked.Exchange(ref _healthDegraded, 1);
        }

        var coverage = snapshot?.Coverage;
        var fields = new[]
        {
            EventPreviewLogEvents.Source.Bind(source),
            EventPreviewLogEvents.ReasonCode.Bind(reasonCode),
            EventPreviewLogEvents.EventCount.Bind(snapshot?.EventCount ?? 0),
            EventPreviewLogEvents.LevelUpCount.Bind(snapshot?.LevelUpCount ?? 0),
            EventPreviewLogEvents.TemplateCount.Bind(snapshot?.TemplateCount ?? 0),
            EventPreviewLogEvents.EventFailureCount.Bind(coverage?.EventFailureCount ?? 0),
            EventPreviewLogEvents.LevelUpFailureCount.Bind(coverage?.LevelUpFailureCount ?? 0),
            EventPreviewLogEvents.UnsupportedLevelUpPartCount.Bind(
                coverage?.UnsupportedLevelUpPartCount ?? 0
            ),
            EventPreviewLogEvents.MissingTemplateCount.Bind(
                coverage?.MissingReferencedTemplateCount ?? 0
            ),
            EventPreviewLogEvents.SizeBytes.Bind(Math.Max(0, sizeBytes)),
            EventPreviewLogEvents.LoadDurationMs.Bind(ToMilliseconds(loadDurationMs)),
            EventPreviewLogEvents.CompileDurationMs.Bind(ToMilliseconds(compileDurationMs)),
            EventPreviewLogEvents.WriteDurationMs.Bind(ToMilliseconds(writeDurationMs)),
            EventPreviewLogEvents.CachePath.Bind(_cacheStore.CachePath),
        };
        if (ReferenceEquals(definition, EventPreviewLogEvents.PlansLoadFailed))
        {
            if (exception == null)
                BppLog.ErrorEvent(definition, fields);
            else
                BppLog.ErrorEvent(definition, exception, fields);
            return;
        }
        if (ReferenceEquals(definition, EventPreviewLogEvents.PlansDegraded))
        {
            if (exception == null)
                BppLog.WarnEvent(definition, fields);
            else
                BppLog.WarnEvent(definition, exception, fields);
            return;
        }

        BppLog.InfoEvent(definition, fields);
    }

    private static int ToMilliseconds(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, (int)Math.Round(value));

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
        Interlocked.Exchange(ref _healthDegraded, 0);
    }
}
