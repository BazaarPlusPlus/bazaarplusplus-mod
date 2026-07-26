#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionHeroSelectionRoster
{
    public static IReadOnlyList<EHero> BaseConcreteHeroes { get; } =
        Array.AsReadOnly(
            new[]
            {
                EHero.Vanessa,
                EHero.Dooley,
                EHero.Pygmalien,
                EHero.Karnok,
                EHero.Mak,
                EHero.Stelle,
                EHero.Jules,
            }
        );

    public static EHero? NormalizeSelection(
        EHero? selectedHero,
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyCollection<EHero> availableHeroes
    )
    {
        if (availableHeroes == null)
            throw new ArgumentNullException(nameof(availableHeroes));
        if (!selectedHero.HasValue || selectedHero.Value == EHero.Common)
            return null;
        if (catalogReadiness != CollectionCatalogReadiness.Accepted)
            return selectedHero;
        return availableHeroes.Contains(selectedHero.Value) ? selectedHero : null;
    }
}
