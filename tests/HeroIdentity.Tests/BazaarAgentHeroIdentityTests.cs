using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop;
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
        var resolution = BazaarAgentHeroIdentity.Resolve(heroId);

        Assert.Equal(BazaarAgentHeroResolutionStatus.Resolved, resolution.Status);
        Assert.True(TheDragonsHeroIdentity.IsTheDragons(resolution.Hero));
    }

    [Fact]
    public void Alias_input_reports_unavailable_when_runtime_exposes_neither_name()
    {
        var resolution = BazaarAgentHeroIdentity.Resolve("TheDragons", _ => null);

        Assert.Equal(BazaarAgentHeroResolutionStatus.Unavailable, resolution.Status);
    }

    [Fact]
    public void Invalid_input_remains_distinct_from_unavailable_alias()
    {
        Assert.Equal(
            BazaarAgentHeroResolutionStatus.Invalid,
            BazaarAgentHeroIdentity.Resolve("UnknownHero").Status
        );
        Assert.Equal(
            BazaarAgentHeroResolutionStatus.Invalid,
            BazaarAgentHeroIdentity.Resolve(" ").Status
        );
    }

    [Fact]
    public void Agent_context_uses_canonical_dragons_id_and_preserves_other_heroes()
    {
        Assert.True(TheDragonsHeroIdentity.TryResolve("Hero8", out var dragons));

        Assert.Equal("TheDragons", BazaarAgentHeroIdentity.ToAgentContextId(dragons));
        Assert.Equal("Vanessa", BazaarAgentHeroIdentity.ToAgentContextId(EHero.Vanessa));
    }
}
