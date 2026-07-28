#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CombatReplay.ReportData;

internal enum CombatReportAttributeClass
{
    LiveState,
    Modifier,
    Economy,
    Diagnostic,
    Unknown,
}

/// <summary>
/// Classifies raw combat-sim attributes before they become report events. The replay payload
/// remains the complete source record; the report event stream keeps only transitions that
/// carry user-facing meaning instead of serializing every 50 ms state-clock tick.
/// </summary>
internal static class CombatReportAttributePolicy
{
    internal static CombatReportAttributeClass Classify(EPlayerAttributeType type) =>
        type switch
        {
            EPlayerAttributeType.Burn
            or EPlayerAttributeType.Joy
            or EPlayerAttributeType.Health
            or EPlayerAttributeType.HealthRegen
            or EPlayerAttributeType.Poison
            or EPlayerAttributeType.Shield
            or EPlayerAttributeType.Rage
            or EPlayerAttributeType.Enraged
            or EPlayerAttributeType.EnragedDuration
            or EPlayerAttributeType.Tempo => CombatReportAttributeClass.LiveState,

            EPlayerAttributeType.CritChance
            or EPlayerAttributeType.DamageCrit
            or EPlayerAttributeType.JoyCrit
            or EPlayerAttributeType.HealthMax
            or EPlayerAttributeType.HealAmount
            or EPlayerAttributeType.HealCrit
            or EPlayerAttributeType.ShieldCrit
            or EPlayerAttributeType.FlatDamageReduction
            or EPlayerAttributeType.PercentDamageReduction
            or EPlayerAttributeType.RageMax
            or EPlayerAttributeType.EnragedDurationMax
            or EPlayerAttributeType.TempoGainCooldownMax
            or EPlayerAttributeType.FlatTempoGainCooldownReduction
            or EPlayerAttributeType.PercentTempoGainCooldownReduction =>
                CombatReportAttributeClass.Modifier,

            EPlayerAttributeType.Experience
            or EPlayerAttributeType.Gold
            or EPlayerAttributeType.Income
            or EPlayerAttributeType.Prestige
            or EPlayerAttributeType.Level
            or EPlayerAttributeType.RerollCostModifier => CombatReportAttributeClass.Economy,

            EPlayerAttributeType.Custom_0
            or EPlayerAttributeType.Custom_1
            or EPlayerAttributeType.Custom_2
            or EPlayerAttributeType.Custom_3
            or EPlayerAttributeType.Custom_4
            or EPlayerAttributeType.Custom_5
            or EPlayerAttributeType.Custom_6
            or EPlayerAttributeType.Custom_7
            or EPlayerAttributeType.Custom_8
            or EPlayerAttributeType.Custom_9 => CombatReportAttributeClass.Diagnostic,

            _ => CombatReportAttributeClass.Unknown,
        };

    internal static CombatReportAttributeClass Classify(ECardAttributeType type) =>
        type switch
        {
            ECardAttributeType.Cooldown
            or ECardAttributeType.Haste
            or ECardAttributeType.Slow
            or ECardAttributeType.Freeze
            or ECardAttributeType.Flying
            or ECardAttributeType.Heated
            or ECardAttributeType.Chilled
            or ECardAttributeType.CooldownDisabled => CombatReportAttributeClass.LiveState,

            ECardAttributeType.Ammo
            or ECardAttributeType.AmmoMax
            or ECardAttributeType.ReloadAmount
            or ECardAttributeType.ReloadTargets
            or ECardAttributeType.CooldownMax
            or ECardAttributeType.ChargeAmount
            or ECardAttributeType.ChargeTargets
            or ECardAttributeType.HasteAmount
            or ECardAttributeType.HasteTargets
            or ECardAttributeType.SlowAmount
            or ECardAttributeType.SlowTargets
            or ECardAttributeType.FreezeAmount
            or ECardAttributeType.FreezeTargets
            or ECardAttributeType.BurnApplyAmount
            or ECardAttributeType.BurnRemoveAmount
            or ECardAttributeType.PoisonApplyAmount
            or ECardAttributeType.PoisonRemoveAmount
            or ECardAttributeType.Multicast
            or ECardAttributeType.Lifesteal
            or ECardAttributeType.CritChance
            or ECardAttributeType.DamageAmount
            or ECardAttributeType.DamageCrit
            or ECardAttributeType.HealAmount
            or ECardAttributeType.HealCrit
            or ECardAttributeType.JoyApplyAmount
            or ECardAttributeType.JoyRemoveAmount
            or ECardAttributeType.JoyCrit
            or ECardAttributeType.ShieldApplyAmount
            or ECardAttributeType.ShieldRemoveAmount
            or ECardAttributeType.ShieldCrit
            or ECardAttributeType.ForceUseTargets
            or ECardAttributeType.EnchantTargets
            or ECardAttributeType.UpgradeTargets
            or ECardAttributeType.DisableTargets
            or ECardAttributeType.RepairTargets
            or ECardAttributeType.BurnCrit
            or ECardAttributeType.PoisonCrit
            or ECardAttributeType.DestroyTargets
            or ECardAttributeType.RegenApplyAmount
            or ECardAttributeType.RegenRemoveAmount
            or ECardAttributeType.RegenCrit
            or ECardAttributeType.TransformTargets
            or ECardAttributeType.FlatCooldownReduction
            or ECardAttributeType.PercentCooldownReduction
            or ECardAttributeType.EnchantRemoveTargets
            or ECardAttributeType.FlyingTargets
            or ECardAttributeType.PercentChargeReduction
            or ECardAttributeType.PercentHasteReduction
            or ECardAttributeType.PercentSlowReduction
            or ECardAttributeType.PercentFreezeReduction
            or ECardAttributeType.DestroyImmunity
            or ECardAttributeType.RageApplyAmount
            or ECardAttributeType.RageRemoveAmount
            or ECardAttributeType.TempoCost
            or ECardAttributeType.FlatTempoCostReduction
            or ECardAttributeType.PercentTempoCostReduction => CombatReportAttributeClass.Modifier,

            ECardAttributeType.BuyPrice or ECardAttributeType.SellPrice =>
                CombatReportAttributeClass.Economy,

            ECardAttributeType.Counter
            or ECardAttributeType.Custom_0
            or ECardAttributeType.Custom_1
            or ECardAttributeType.Custom_2
            or ECardAttributeType.Custom_3
            or ECardAttributeType.Custom_4
            or ECardAttributeType.Custom_5
            or ECardAttributeType.Custom_6
            or ECardAttributeType.Custom_7
            or ECardAttributeType.Custom_8
            or ECardAttributeType.QuestCompletedCount
            or ECardAttributeType.Quest_1
            or ECardAttributeType.Quest_2
            or ECardAttributeType.Quest_3
            or ECardAttributeType.Quest_4
            or ECardAttributeType.Quest_5
            or ECardAttributeType.Quest_6
            or ECardAttributeType.Quest_7
            or ECardAttributeType.Quest_8
            or ECardAttributeType.Quest_9
            or ECardAttributeType.Quest_10
            or ECardAttributeType.Quest_11
            or ECardAttributeType.Quest_12 => CombatReportAttributeClass.Diagnostic,

            _ => CombatReportAttributeClass.Unknown,
        };

    internal static bool ShouldProjectCardTransition(ECardAttributeType type)
    {
        var attributeClass = Classify(type);
        return attributeClass
            is CombatReportAttributeClass.Modifier
                or CombatReportAttributeClass.Economy;
    }

    internal static bool IsPlayerChartMetric(EPlayerAttributeType type) =>
        type
            is EPlayerAttributeType.Health
                or EPlayerAttributeType.Rage
                or EPlayerAttributeType.HealthRegen
                or EPlayerAttributeType.Shield
                or EPlayerAttributeType.Burn
                or EPlayerAttributeType.Poison;

    internal static bool ShouldProjectPlayerTransition(
        EPlayerAttributeType type,
        bool hasExplicitHealthAdjustment
    )
    {
        var attributeClass = Classify(type);
        if (attributeClass == CombatReportAttributeClass.LiveState)
        {
            return type == EPlayerAttributeType.Health && !hasExplicitHealthAdjustment;
        }

        return attributeClass
            is CombatReportAttributeClass.Modifier
                or CombatReportAttributeClass.Economy;
    }
}
