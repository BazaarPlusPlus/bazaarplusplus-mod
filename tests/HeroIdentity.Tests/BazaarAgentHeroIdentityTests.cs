using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.BazaarAgentHost;
using BazaarPlusPlus.GameInterop.Heroes;
using Xunit;

namespace HeroIdentity.Tests;

public sealed class BazaarAgentHeroIdentityTests
{
    [Theory]
    [InlineData("Hero8")]
    [InlineData("TheDragons")]
    [InlineData(" hero8 ")]
    public void Both_alias_inputs_resolve_to_the_current_runtime_hero(string heroId)
    {
        var status = BazaarAgentHeroIdentity.ResolveInput(heroId, out var hero);

        Assert.Equal(BazaarAgentHeroResolveStatus.Resolved, status);
        Assert.True(TheDragonsHeroIdentity.IsTheDragons(hero));
    }

    [Fact]
    public void Alias_input_reports_unavailable_when_runtime_exposes_neither_name()
    {
        var status = BazaarAgentHeroIdentity.ResolveInput("TheDragons", _ => null, out _);

        Assert.Equal(BazaarAgentHeroResolveStatus.Unavailable, status);
    }

    [Fact]
    public void Invalid_input_remains_distinct_from_unavailable_alias()
    {
        Assert.Equal(
            BazaarAgentHeroResolveStatus.Invalid,
            BazaarAgentHeroIdentity.ResolveInput("UnknownHero", out _)
        );
        Assert.Equal(
            BazaarAgentHeroResolveStatus.Invalid,
            BazaarAgentHeroIdentity.ResolveInput(" ", out _)
        );
    }

    [Fact]
    public void Agent_context_uses_canonical_dragons_id_and_preserves_other_heroes()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("Hero8", out var dragons));

        Assert.Equal("TheDragons", BazaarAgentHeroIdentity.ToContextId(dragons));
        Assert.Equal("Vanessa", BazaarAgentHeroIdentity.ToContextId(EHero.Vanessa));
    }
}
