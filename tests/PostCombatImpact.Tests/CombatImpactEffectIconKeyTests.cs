using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactEffectIconKeyTests
{
    [Theory]
    [InlineData("ChargeAmount", "Charge")]
    [InlineData("ChargeTargets", "Charge")]
    [InlineData("PercentChargeReduction", "Charge")]
    [InlineData("HasteAmount", "Haste")]
    [InlineData("HasteTargets", "Haste")]
    [InlineData("PercentHasteReduction", "Haste")]
    [InlineData("SlowAmount", "Slow")]
    [InlineData("SlowTargets", "Slow")]
    [InlineData("PercentSlowReduction", "Slow")]
    [InlineData("FreezeAmount", "Freeze")]
    [InlineData("FreezeTargets", "Freeze")]
    [InlineData("PercentFreezeReduction", "Freeze")]
    [InlineData("RepairTargets", "Repair")]
    [InlineData("TransformTargets", "Transform")]
    [InlineData("UpgradeTargets", "Upgrade")]
    [InlineData("FlyingTargets", "Flying")]
    [InlineData("EnchantTargets", "Enchant")]
    [InlineData("EnchantRemoveTargets", "Enchant")]
    [InlineData("DestroyImmunity", "Destroy")]
    [InlineData("DamageAmount", "Damage")]
    [InlineData("BurnRemoveAmount", "Burn")]
    [InlineData("PoisonRemoveAmount", "Poison")]
    [InlineData("RegenRemoveAmount", "Regen")]
    [InlineData("ShieldRemoveAmount", "Shield")]
    [InlineData("HealthMaxIncrease", "Healing")]
    [InlineData("RageRemoveAmount", "Rage")]
    [InlineData("TempoCost", "Tempo")]
    [InlineData("FlatTempoCostReduction", "Tempo")]
    [InlineData("PercentTempoCostReduction", "Tempo")]
    [InlineData("TempoGainCooldownMax", "Tempo")]
    public void Attribute_keys_use_native_keyword_icon_keys(string attributeKey, string iconKey)
    {
        Assert.Equal(iconKey, CombatImpactEffectIconKey.ResolveAttribute(attributeKey, null));
    }

    [Fact]
    public void Enchant_variant_uses_variant_icon_key()
    {
        Assert.Equal(
            "Fiery",
            CombatImpactEffectIconKey.Resolve(
                CombatImpactKind.AttributeChange,
                "EnchantTargets:Fiery"
            )
        );
    }

    [Fact]
    public void Destroy_kind_uses_destroy_icon_key()
    {
        Assert.Equal(
            "Destroy",
            CombatImpactEffectIconKey.Resolve(CombatImpactKind.Destroy, "DestroyTargets")
        );
    }
}
