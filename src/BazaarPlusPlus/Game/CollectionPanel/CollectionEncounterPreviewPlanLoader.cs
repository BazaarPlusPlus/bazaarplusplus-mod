#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Game;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionEncounterPreviewPlanLoadResult
{
    public CollectionEncounterPreviewPlanLoadResult(
        CollectionEncounterPreviewSnapshot snapshot,
        bool wasCacheHit,
        string cacheMissReason,
        CollectionEncounterPreviewCompileResult? compileResult,
        double cacheReadMilliseconds,
        double compileMilliseconds
    )
    {
        Snapshot = snapshot;
        WasCacheHit = wasCacheHit;
        CacheMissReason = cacheMissReason;
        CompileResult = compileResult;
        CacheReadMilliseconds = cacheReadMilliseconds;
        CompileMilliseconds = compileMilliseconds;
    }

    public CollectionEncounterPreviewSnapshot Snapshot { get; }

    public bool WasCacheHit { get; }

    public string CacheMissReason { get; }

    public CollectionEncounterPreviewCompileResult? CompileResult { get; }

    public double CacheReadMilliseconds { get; }

    public double CompileMilliseconds { get; }
}

internal sealed class CollectionEncounterPreviewPlanLoader
{
    private readonly CollectionEncounterPreviewCacheStore _cacheStore;
    private readonly Func<
        IReadOnlyDictionary<Guid, ITCard>,
        IReadOnlyDictionary<int, TLevelUp>,
        CollectionEncounterPreviewCompileResult
    > _compile;

    public CollectionEncounterPreviewPlanLoader(
        CollectionEncounterPreviewCacheStore cacheStore,
        Func<
            IReadOnlyDictionary<Guid, ITCard>,
            IReadOnlyDictionary<int, TLevelUp>,
            CollectionEncounterPreviewCompileResult
        > compile
    )
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _compile = compile ?? throw new ArgumentNullException(nameof(compile));
    }

    public async Task<CollectionEncounterPreviewPlanLoadResult> LoadAsync(
        CollectionEncounterPreviewCacheIdentity identity,
        Func<Task<Dictionary<Guid, ITCard>?>> loadCardMap,
        Func<Dictionary<int, TLevelUp>?> loadLevelUps,
        CancellationToken cancellationToken
    )
    {
        if (identity == null)
            throw new ArgumentNullException(nameof(identity));
        if (loadCardMap == null)
            throw new ArgumentNullException(nameof(loadCardMap));
        if (loadLevelUps == null)
            throw new ArgumentNullException(nameof(loadLevelUps));

        var cacheStarted = Stopwatch.GetTimestamp();
        if (_cacheStore.TryLoad(identity, out var cached, out var missReason))
        {
            return new CollectionEncounterPreviewPlanLoadResult(
                cached!,
                wasCacheHit: true,
                cacheMissReason: string.Empty,
                compileResult: null,
                ElapsedMilliseconds(cacheStarted),
                compileMilliseconds: 0
            );
        }

        var cacheReadMilliseconds = ElapsedMilliseconds(cacheStarted);
        cancellationToken.ThrowIfCancellationRequested();
        var map = await loadCardMap().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (map == null)
            throw new InvalidOperationException("The shared static card map load returned null.");
        var levelUps = loadLevelUps();
        if (levelUps == null)
            throw new InvalidOperationException("The static level-up snapshot returned null.");

        var compileStarted = Stopwatch.GetTimestamp();
        var compileResult = _compile(map, levelUps);
        return new CollectionEncounterPreviewPlanLoadResult(
            compileResult.Snapshot,
            wasCacheHit: false,
            missReason,
            compileResult,
            cacheReadMilliseconds,
            ElapsedMilliseconds(compileStarted)
        );
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;
}
