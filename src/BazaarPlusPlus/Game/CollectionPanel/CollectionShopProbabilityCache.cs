#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.CollectionPanel.DealerModel;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionShopProbabilityCache
{
    private readonly Dictionary<
        string,
        IReadOnlyDictionary<Guid, CollectionDealerCardExplain>
    > _cache = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<Guid, CollectionDealerCardExplain> GetOrResolve(
        string key,
        Func<IReadOnlyDictionary<Guid, CollectionDealerCardExplain>> resolve
    )
    {
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = resolve();
        _cache[key] = result;
        return result;
    }

    public void Clear() => _cache.Clear();
}
