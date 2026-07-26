#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.BazaarAgentHost;

internal enum BazaarAgentHeroResolveStatus
{
    Resolved,
    Invalid,
    Unavailable,
}

internal static class BazaarAgentHeroIdentity
{
    internal static BazaarAgentHeroResolveStatus ResolveInput(string? heroId, out EHero hero) =>
        ResolveInput(heroId, TryResolveExactEnumName, out hero);

    internal static BazaarAgentHeroResolveStatus ResolveInput(
        string? heroId,
        Func<string, EHero?> resolveExactName,
        out EHero hero
    )
    {
        if (resolveExactName == null)
            throw new ArgumentNullException(nameof(resolveExactName));

        hero = default;
        if (string.IsNullOrWhiteSpace(heroId))
            return BazaarAgentHeroResolveStatus.Invalid;

        var trimmed = heroId.Trim();
        if (TheDragonsHeroIdentity.IsAlias(trimmed))
        {
            return TheDragonsHeroIdentity.TryResolve(trimmed, resolveExactName, out hero)
                ? BazaarAgentHeroResolveStatus.Resolved
                : BazaarAgentHeroResolveStatus.Unavailable;
        }

        var resolved =
            Enum.TryParse(trimmed, ignoreCase: true, out EHero parsed)
            && Enum.IsDefined(typeof(EHero), parsed);
        if (!resolved)
            return BazaarAgentHeroResolveStatus.Invalid;

        hero = parsed;
        return BazaarAgentHeroResolveStatus.Resolved;
    }

    internal static string ToContextId(EHero hero) =>
        TheDragonsHeroIdentity.IsTheDragons(hero)
            ? TheDragonsHeroIdentity.CanonicalId
            : hero.ToString();

    private static EHero? TryResolveExactEnumName(string name)
    {
        if (
            !Enum.TryParse(name, ignoreCase: false, out EHero hero)
            || !Enum.IsDefined(typeof(EHero), hero)
        )
        {
            return null;
        }

        return hero;
    }
}
