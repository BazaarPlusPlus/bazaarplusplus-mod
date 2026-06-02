#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Encounters;
using BazaarPlusPlus.GameInterop.EncounterOffers;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionSourceOfferPoolCache
{
    private readonly Dictionary<string, EncounterOfferPoolResult> _cache = new(
        StringComparer.Ordinal
    );

    public EncounterOfferPoolResult GetOrResolve(
        MerchantTrainerEntry source,
        IReadOnlyList<EHero> heroFilters,
        IReadOnlyList<CollectionCardVm> catalogCards
    )
    {
        var key = BuildKey(source, heroFilters);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = CollectionSourceRuleOfferPoolResolver.Resolve(
            source,
            heroFilters,
            catalogCards
        );
        if (result.Status == EncounterOfferPoolStatus.Unavailable)
        {
            result = EncounterOfferPoolResolver.ResolveOfferedTemplateIds(
                source.TemplateIds,
                heroFilters
            );
        }

        if (
            result.Status == EncounterOfferPoolStatus.Ready
            || result.Status == EncounterOfferPoolStatus.Unavailable
        )
            _cache[key] = result;
        return result;
    }

    public string BuildKey(MerchantTrainerEntry source, IReadOnlyList<EHero> heroFilters) =>
        CollectionSourceOfferPoolCacheKey.Build(source, heroFilters);

    public void Clear() => _cache.Clear();
}
