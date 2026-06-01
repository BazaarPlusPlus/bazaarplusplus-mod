#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Encounters;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal static class CollectionSourceOfferPoolCacheKey
{
    public static string Build(MerchantTrainerEntry source, IReadOnlyList<EHero> heroFilters) =>
        string.Join(
            "|",
            source.SourceKey,
            BuildTemplateIdsFingerprint(source.TemplateIds),
            BuildHeroKey(heroFilters)
        );

    private static string BuildTemplateIdsFingerprint(IReadOnlyList<Guid> templateIds) =>
        string.Join(
            "-",
            templateIds.OrderBy(id => id).Select(id => id.ToString("N").Substring(0, 12))
        );

    private static string BuildHeroKey(IReadOnlyList<EHero> heroFilters)
    {
        if (heroFilters.Count == 0)
            return "no-hero-filter";

        return string.Join(
            ",",
            heroFilters
                .Select(hero => hero.ToString())
                .OrderBy(value => value, StringComparer.Ordinal)
        );
    }
}
