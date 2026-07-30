using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactProjectorTests
{
    [Fact]
    public void Projects_uniquely_attributed_damage_and_real_critical_effect()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.PlayerDamage, Player(ECombatantId.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatSimPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Damage,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = -160,
                    IsCrit = true,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.DamageDone] = 160,
            [ECardStats.UseCount] = 1,
        };

        var report = CombatImpactProjector.Project(simulation, Entities());

        var source = Assert.Single(report.Sources);
        Assert.Equal(1, source.UseCount);
        var damage = Assert.Single(
            source.Groups,
            group => group.Kind == CombatImpactKind.DirectDamage
        );
        Assert.Equal(160, damage.AggregateValue);
        Assert.Equal(160, Assert.Single(damage.Targets).AggregateValue);
        var critical = Assert.Single(
            source.Groups,
            group => group.Kind == CombatImpactKind.Critical
        );
        Assert.Equal(160, critical.AggregateValue);
        Assert.Equal("Opponent", Assert.Single(critical.Targets).Entity.Name);
    }

    [Fact]
    public void Ambiguous_same_frame_values_are_not_assigned_to_targets()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.PlayerDamage, Player(ECombatantId.Opponent))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.PlayerDamage, Player(ECombatantId.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatSimPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Damage,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = -200,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.DamageDone] = 200,
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(2, group.Count);
        Assert.Equal(200, group.AggregateValue);
        Assert.False(group.ValueIsPartial);
        var target = Assert.Single(group.Targets);
        Assert.Null(target.AggregateValue);
        Assert.True(target.ValueIsPartial);
    }

    [Fact]
    public void Projects_unique_attribute_change_with_native_attribute_key()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.CardModifyAttribute,
                    new EffectTargetCard { Target = target }
                )
            );
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.CritChance] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.CritChance,
                    PreviousValue = 20,
                    CurrentValue = 22,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal("CritChance", group.NativeAttributeKey);
        Assert.Equal(2, group.AggregateValue);
        Assert.Equal(CombatImpactValueUnit.PercentagePoints, group.Unit);
    }

    [Fact]
    public void Keeps_control_duration_instead_of_treating_native_target_count_as_milliseconds()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.CardSlow,
                    new EffectTargetCard { Target = target }
                )
            );
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.Slow] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.Slow,
                    PreviousValue = 0,
                    CurrentValue = 1950,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.SlowedCardsCount] = 1,
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.Slow, group.Kind);
        Assert.Equal(CombatImpactValueUnit.Milliseconds, group.Unit);
        Assert.Equal(1950, group.AggregateValue);
    }

    [Fact]
    public void Projects_destroy_as_targeted_count_without_invented_value()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.CardDestroy,
                    new EffectTargetCard { Target = InstanceId.TryParse("target") }
                )
            );

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.Destroy, group.Kind);
        Assert.Equal(1, group.Count);
        Assert.Null(group.AggregateValue);
        Assert.Equal("Bread Knife", Assert.Single(group.Targets).Entity.Name);
    }

    private static CombatSimEventEffectExecuted Executed(
        string source,
        EActionCommandType action,
        IEffectTarget target
    ) =>
        new()
        {
            Source = InstanceId.TryParse(source),
            ActionType = action,
            Target = target,
        };

    private static EffectTargetPlayer Player(ECombatantId target) => new() { Target = target };

    private static IReadOnlyDictionary<string, CombatImpactEntity> Entities() =>
        new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            ["source"] = new("source", "Fairies", "Skill", null, null, ECombatantId.Player, 0),
            ["target"] = new("target", "Bread Knife", "Item", null, null, ECombatantId.Opponent, 1),
            [CombatImpactProjector.PlayerId(ECombatantId.Opponent)] = new(
                CombatImpactProjector.PlayerId(ECombatantId.Opponent),
                "Opponent",
                "Hero",
                null,
                null,
                ECombatantId.Opponent,
                2
            ),
        };
}
