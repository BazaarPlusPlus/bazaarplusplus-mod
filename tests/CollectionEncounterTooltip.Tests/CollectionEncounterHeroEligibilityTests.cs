using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class CollectionEncounterHeroEligibilityTests
{
    [Fact]
    public void Includes_common_steps_for_a_specific_hero()
    {
        Assert.True(
            CollectionEncounterHeroEligibility.Matches(new[] { EHero.Common }, EHero.Stelle)
        );
    }

    [Fact]
    public void Includes_steps_for_the_current_hero()
    {
        Assert.True(
            CollectionEncounterHeroEligibility.Matches(
                new[] { EHero.Vanessa, EHero.Dooley, EHero.Karnok },
                EHero.Dooley
            )
        );
    }

    [Fact]
    public void Excludes_steps_for_other_heroes()
    {
        Assert.False(CollectionEncounterHeroEligibility.Matches(new[] { EHero.Mak }, EHero.Stelle));
    }

    [Fact]
    public void Keeps_all_steps_when_current_hero_is_unknown()
    {
        Assert.True(CollectionEncounterHeroEligibility.Matches(new[] { EHero.Mak }, null));
    }
}
