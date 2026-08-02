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
        "CardModifyAttribute",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Card Attribute Change",
        "卡牌属性变化"
    )]
    [InlineData(
        "PlayerModifyAttribute",
        (int)CombatImpactEventSurface.AppliedEffect,
        "Player Attribute Change",
        "玩家属性变化"
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

        Assert.Equal(english, CombatImpactAttributeLabel.Resolve(key, surface, chinese: false));
        Assert.Equal(chinese, CombatImpactAttributeLabel.Resolve(key, surface, chinese: true));
    }
}
