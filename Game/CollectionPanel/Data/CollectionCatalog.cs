#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.DataManagement.Json;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionCatalog
{
    private IReadOnlyList<CollectionCardVm>? _cache;
    private object? _cacheSource;
    private int _cacheSourceTemplateCount;

    public bool TryGetCached(out CollectionCatalogBuildResult result)
    {
        result = EmptyResult(wasCacheHit: false);
        var source = BppStaticDataAccess.TryGet();
        if (source == null || _cache == null)
            return false;

        if (!ReferenceEquals(source, _cacheSource))
        {
            InvalidateCache("static-data-manager-changed");
            return false;
        }

        result = new CollectionCatalogBuildResult(
            _cache,
            _cacheSourceTemplateCount,
            _cacheSourceTemplateCount,
            _cache.Count,
            Math.Max(0, _cacheSourceTemplateCount - _cache.Count),
            wasCacheHit: true
        );
        BppLog.Info(
            "CollectionCatalog",
            $"Catalog cache hit: {result.AcceptedCount} cards from {result.SourceTemplateCount} templates."
        );
        return true;
    }

    public bool TryCreateBuildSession(
        out CollectionCatalogBuildSession? session,
        out string unavailableReason
    )
    {
        session = null;
        unavailableReason = string.Empty;

        var managerObject = BppStaticDataAccess.TryGet();
        if (managerObject is not JsonGameDataManager manager)
        {
            unavailableReason = "static-data-not-ready";
            BppLog.Debug(
                "CollectionCatalog",
                "Static data manager not yet ready; catalog build deferred."
            );
            return false;
        }

        Dictionary<Guid, ITCard>? map;
        try
        {
            map = manager.GetCardMap();
        }
        catch (Exception ex)
        {
            unavailableReason = "get-card-map-threw";
            BppLog.Error("CollectionCatalog", "GetCardMap() threw", ex);
            return false;
        }

        if (map == null)
        {
            unavailableReason = "card-map-null";
            BppLog.Warn("CollectionCatalog", "GetCardMap() returned null.");
            return false;
        }

        session = new CollectionCatalogBuildSession(managerObject, map);
        return true;
    }

    public CollectionCatalogBuildResult Commit(CollectionCatalogBuildSession session)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (!session.IsComplete)
            throw new InvalidOperationException("Catalog build session is not complete.");

        _cache = session.Cards;
        _cacheSource = session.Source;
        _cacheSourceTemplateCount = session.SourceTemplateCount;

        var result = new CollectionCatalogBuildResult(
            session.Cards,
            session.SourceTemplateCount,
            session.ScannedCount,
            session.AcceptedCount,
            session.RejectedCount,
            wasCacheHit: false
        );
        BppLog.Info(
            "CollectionCatalog",
            $"Catalog built: {result.AcceptedCount} cards from {result.SourceTemplateCount} templates, rejected={result.RejectedCount}."
        );
        return result;
    }

    public void InvalidateCache(string reason)
    {
        if (_cache != null)
            BppLog.Info("CollectionCatalog", $"Catalog cache invalidated: reason={reason}.");
        _cache = null;
        _cacheSource = null;
        _cacheSourceTemplateCount = 0;
    }

    private static CollectionCatalogBuildResult EmptyResult(bool wasCacheHit) =>
        new(
            Array.Empty<CollectionCardVm>(),
            sourceTemplateCount: 0,
            scannedCount: 0,
            acceptedCount: 0,
            rejectedCount: 0,
            wasCacheHit: wasCacheHit
        );
}
