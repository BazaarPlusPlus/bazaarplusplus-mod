#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public static class RandomHeroPoolPreferences
{
    public static IReadOnlyCollection<string> Sanitize(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? savedPoolHeroIds
    )
    {
        return RandomHeroPoolStateFactory
            .Create(unlockedHeroIds, savedPoolHeroIds)
            .SelectedHeroIds.ToArray();
    }

    public static IReadOnlyCollection<string> MergeUnlockedHeroIds(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? savedPoolHeroIds,
        IEnumerable<string> newlyUnlockedHeroIds
    )
    {
        var newlyUnlockedHeroIdArray = newlyUnlockedHeroIds.ToArray();
        var effectiveUnlockedHeroIds = unlockedHeroIds.Concat(newlyUnlockedHeroIdArray);
        var mergedState = RandomHeroPoolStateFactory.Create(effectiveUnlockedHeroIds, savedPoolHeroIds);
        foreach (var heroId in newlyUnlockedHeroIdArray)
        {
            mergedState = mergedState.SetSelected(heroId, isSelected: true);
        }

        return mergedState.SelectedHeroIds.ToArray();
    }

    public static IReadOnlyCollection<string> MergeWithKnownUnlockedHeroIds(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? savedPoolHeroIds,
        IEnumerable<string>? knownUnlockedHeroIds
    )
    {
        var unlockedHeroIdArray = Sanitize(unlockedHeroIds, unlockedHeroIds).ToArray();
        var knownUnlockedHeroIdArray = knownUnlockedHeroIds?
            .Where(heroId => !string.IsNullOrWhiteSpace(heroId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (knownUnlockedHeroIdArray == null || knownUnlockedHeroIdArray.Length == 0)
        {
            return Sanitize(unlockedHeroIdArray, savedPoolHeroIds);
        }

        var knownUnlockedHeroIdSet = new HashSet<string>(
            knownUnlockedHeroIdArray,
            StringComparer.Ordinal
        );
        var newlyUnlockedHeroIds = unlockedHeroIdArray.Where(
            heroId => !knownUnlockedHeroIdSet.Contains(heroId)
        );
        return MergeUnlockedHeroIds(unlockedHeroIdArray, savedPoolHeroIds, newlyUnlockedHeroIds);
    }
}
