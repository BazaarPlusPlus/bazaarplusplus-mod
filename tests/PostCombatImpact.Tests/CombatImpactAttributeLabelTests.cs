using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactAttributeLabelTests
{
    [Theory]
    [InlineData("HealthMax", "Max Health")]
    [InlineData("HealthMaxIncrease", "Max Health")]
    [InlineData("DamageAmount", "Damage")]
    [InlineData("CritChance", "Crit Chance")]
    [InlineData("BurnApplyAmount", "Burn")]
    [InlineData("FlyingTargets", "Flying Targets")]
    [InlineData("DestroyTargets", "Destroy Targets")]
    [InlineData("ForceUseTargets", "Use Targets")]
    [InlineData("PercentDamageReduction", "Damage Reduction")]
    [InlineData("RerollCostModifier", "Reroll Cost")]
    public void Internal_attribute_keys_use_player_facing_english_labels(
        string key,
        string expected
    ) =>
        Assert.Equal(
            expected,
            CombatImpactAttributeLabel.Resolve(
                key,
                CombatImpactEventSurface.AppliedEffect,
                chinese: false
            )
        );

    [Theory]
    [InlineData("HealthMax", "最大生命值")]
    [InlineData("DamageAmount", "伤害")]
    [InlineData("CritChance", "暴击率")]
    [InlineData("FlyingTargets", "起飞目标")]
    [InlineData("DestroyTargets", "摧毁目标")]
    [InlineData("PercentDamageReduction", "伤害减免")]
    public void Internal_attribute_keys_use_player_facing_chinese_labels(
        string key,
        string expected
    ) =>
        Assert.Equal(
            expected,
            CombatImpactAttributeLabel.Resolve(
                key,
                CombatImpactEventSurface.AppliedEffect,
                chinese: true
            )
        );

    [Theory]
    [InlineData("DamageAmount", "Damage Gain", "伤害增加")]
    [InlineData("RegenApplyAmount", "Regen Gain", "再生增加")]
    public void Card_attribute_values_are_distinct_from_applied_effects(
        string key,
        string english,
        string chinese
    )
    {
        Assert.Equal(
            english,
            CombatImpactAttributeLabel.Resolve(
                key,
                CombatImpactEventSurface.CardAttribute,
                chinese: false
            )
        );
        Assert.Equal(
            chinese,
            CombatImpactAttributeLabel.Resolve(
                key,
                CombatImpactEventSurface.CardAttribute,
                chinese: true
            )
        );
    }

    [Theory]
    [InlineData("CardModifyAttribute", "Card Attribute Change", "卡牌属性变化")]
    [InlineData("PlayerModifyAttribute", "Player Attribute Change", "玩家属性变化")]
    [InlineData("FutureAttributeValue", "Attribute Change", "属性变化")]
    public void Unresolved_keys_use_safe_generic_labels(string key, string english, string chinese)
    {
        Assert.Equal(
            english,
            CombatImpactAttributeLabel.Resolve(
                key,
                CombatImpactEventSurface.AppliedEffect,
                chinese: false
            )
        );
        Assert.Equal(
            chinese,
            CombatImpactAttributeLabel.Resolve(
                key,
                CombatImpactEventSurface.AppliedEffect,
                chinese: true
            )
        );
    }
}
