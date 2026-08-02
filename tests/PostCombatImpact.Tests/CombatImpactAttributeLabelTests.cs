using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactAttributeLabelTests
{
    [Theory]
    [InlineData(
        "HealthMax",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Max Health",
        "最大生命值"
    )]
    [InlineData(
        "HealthMaxIncrease",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Max Health",
        "最大生命值"
    )]
    [InlineData("DamageAmount", (int)CombatImpactEventSurface.AppliedEffect, "Damage", "伤害")]
    [InlineData("CritChance", (int)CombatImpactEventSurface.AppliedEffect, "Crit Chance", "暴击率")]
    [InlineData("BurnApplyAmount", (int)CombatImpactEventSurface.AppliedEffect, "Burn", "燃烧")]
    [InlineData(
        "FlyingTargets",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Flying Targets",
        "起飞目标"
    )]
    [InlineData(
        "DestroyTargets",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Destroy Targets",
        "摧毁目标"
    )]
    [InlineData(
        "ForceUseTargets",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Use Targets",
        "使用目标"
    )]
    [InlineData(
        "PercentDamageReduction",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Damage Reduction",
        "伤害减免"
    )]
    [InlineData(
        "RerollCostModifier",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Reroll Cost",
        "刷新费用"
    )]
    [InlineData(
        "DamageAmount",
        (int)CombatImpactEventSurface.CardAttribute,
        "Damage Gain",
        "伤害增加"
    )]
    [InlineData(
        "RegenApplyAmount",
        (int)CombatImpactEventSurface.CardAttribute,
        "Regen Gain",
        "再生增加"
    )]
    [InlineData(
        "FutureAttributeValue",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Attribute Change",
        "属性变化"
    )]
    public void Internal_keys_use_localized_player_facing_labels(
        string key,
        int surfaceValue,
        string english,
        string chinese
    )
    {
        var surface = (CombatImpactEventSurface)surfaceValue;

        Assert.Equal(
            english,
            CombatImpactAttributeLabel.Resolve(key, surface, changeValue: 1, chinese: false)
        );
        Assert.Equal(
            chinese,
            CombatImpactAttributeLabel.Resolve(key, surface, changeValue: 1, chinese: true)
        );
    }

    [Theory]
    [InlineData("DamageAmount", -5, "Damage Loss", "伤害减少")]
    [InlineData("BurnRemoveAmount", 5, "Burn Removal", "燃烧移除量")]
    [InlineData("ShieldRemoveAmount", -5, "Shield Removal", "护盾移除量")]
    public void Card_attribute_labels_distinguish_direction_without_redundant_removal_gain(
        string key,
        int changeValue,
        string english,
        string chinese
    )
    {
        Assert.Equal(
            english,
            CombatImpactAttributeLabel.Resolve(
                key,
                CombatImpactEventSurface.CardAttribute,
                changeValue,
                chinese: false
            )
        );
        Assert.Equal(
            chinese,
            CombatImpactAttributeLabel.Resolve(
                key,
                CombatImpactEventSurface.CardAttribute,
                changeValue,
                chinese: true
            )
        );
    }
}
