using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactTargetDetailPolicyTests
{
    [Fact]
    public void Regen_targets_are_shown_only_for_card_attribute_changes()
    {
        Assert.False(
            CombatImpactTargetDetailPolicy.ShouldRender(
                CombatImpactKind.AttributeChange,
                "RegenApplyAmount",
                CombatImpactEventSurface.AppliedEffect
            )
        );
        Assert.True(
            CombatImpactTargetDetailPolicy.ShouldRender(
                CombatImpactKind.AttributeChange,
                "RegenApplyAmount",
                CombatImpactEventSurface.CardAttribute
            )
        );
    }

    [Fact]
    public void Direct_damage_does_not_repeat_its_implicit_player_target() =>
        Assert.False(
            CombatImpactTargetDetailPolicy.ShouldRender(
                CombatImpactKind.DirectDamage,
                "DamageAmount",
                CombatImpactEventSurface.AppliedEffect
            )
        );
}
