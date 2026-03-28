#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public sealed class RandomHeroPoolSelector
{
    public string SelectHero(IReadOnlyList<string> candidateHeroIds, int randomIndex)
    {
        if (candidateHeroIds is null)
        {
            throw new ArgumentNullException(nameof(candidateHeroIds));
        }

        if (candidateHeroIds.Count == 0)
        {
            throw new InvalidOperationException("Random hero pool cannot be empty.");
        }

        if ((uint)randomIndex >= (uint)candidateHeroIds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(randomIndex));
        }

        return candidateHeroIds[randomIndex];
    }
}
