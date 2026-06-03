#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionHeroScope
{
    public static bool MatchesFilter(CollectionCardVm card, CollectionFilterState filter)
    {
        if (filter.Heroes.Count == 0)
            return true;

        if (card.Type == ECardType.Skill)
        {
            var selectedHero = filter.SelectedHero;
            return selectedHero.HasValue && MatchesSkillHeroScope(card.Heroes, selectedHero.Value);
        }

        return AnyHeroMatch(card.Heroes, filter.Heroes);
    }

    public static bool MatchesSkillHeroScope(IReadOnlyCollection<EHero> cardHeroes, EHero hero)
    {
        return cardHeroes.Count == 1 && Contains(cardHeroes, hero);
    }

    private static bool AnyHeroMatch(
        IReadOnlyCollection<EHero> cardHeroes,
        HashSet<EHero> filterHeroes
    )
    {
        foreach (var hero in cardHeroes)
        {
            if (filterHeroes.Contains(hero))
                return true;
        }
        return false;
    }

    private static bool Contains(IReadOnlyCollection<EHero> values, EHero target)
    {
        foreach (var value in values)
        {
            if (value == target)
                return true;
        }
        return false;
    }
}
