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
    public void Every_native_action_has_an_explicit_display_or_ignore_classification()
    {
        Assert.All(
            Enum.GetValues<EActionCommandType>(),
            action =>
                Assert.True(
                    CombatImpactProjector.HasExplicitDisplayClassification(action),
                    $"{action} must be explicitly displayed or ignored."
                )
        );
    }

    [Fact]
    public void Entity_owner_uses_native_combatant_id_with_player_fallback()
    {
        Assert.Equal(ECombatantId.Player, CombatImpactEntityOwnerResolver.Resolve(null));
        Assert.Equal(
            ECombatantId.Player,
            CombatImpactEntityOwnerResolver.Resolve(ECombatantId.Player)
        );
        Assert.Equal(
            ECombatantId.Opponent,
            CombatImpactEntityOwnerResolver.Resolve(ECombatantId.Opponent)
        );
    }

    [Fact]
    public void Projects_uniquely_attributed_damage_and_derived_critical_presentation()
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
        Assert.Equal(160, damage.ObservedValue);
        Assert.Equal(160, damage.AuthoritativeMetric?.Value);
        Assert.Equal(160, Assert.Single(damage.Targets).ObservedValue);
        Assert.Equal(1, damage.CriticalCount);
        Assert.Equal(160, damage.CriticalObservedValue);
        Assert.Equal("Opponent", Assert.Single(damage.Targets).Entity.Name);
        Assert.Equal(1, source.EffectCount);
    }

    [Fact]
    public void Merges_observed_burn_and_poison_with_authoritative_totals()
    {
        var cases = new[]
        {
            (
                EActionCommandType.PlayerBurnApply,
                EPlayerAttributeType.Burn,
                ECardStats.BurnAdded,
                CombatImpactKind.Burn,
                "BurnApplyAmount"
            ),
            (
                EActionCommandType.PlayerPoisonApply,
                EPlayerAttributeType.Poison,
                ECardStats.PoisonAdded,
                CombatImpactKind.Poison,
                "PoisonApplyAmount"
            ),
        };
        foreach (var (action, attribute, statistic, expectedKind, canonicalKey) in cases)
        {
            var simulation = new CombatSim();
            simulation
                .Frames[0]
                .Events.Add(Executed("source", action, Player(ECombatantId.Opponent)));
            simulation.Frames[0].OpponentUpdates = new CombatSimPlayerUpdate
            {
                Attributes =
                {
                    [attribute] = new CombatSimPlayerAttributeUpdate
                    {
                        AttributeType = attribute,
                        PreviousValue = 0,
                        CurrentValue = 69,
                    },
                },
            };
            simulation.CardStats["source"] = new Dictionary<ECardStats, int> { [statistic] = 76 };

            var source = Assert.Single(
                CombatImpactProjector.Project(simulation, Entities()).Sources
            );
            var group = Assert.Single(source.Groups);

            Assert.Equal(expectedKind, group.Kind);
            Assert.Equal(canonicalKey, group.NativeAttributeKey);
            Assert.Equal(69, group.ObservedValue);
            Assert.Equal(76, group.AuthoritativeMetric?.Value);
            Assert.Equal(69, Assert.Single(group.Targets).ObservedValue);
        }
    }

    [Theory]
    [InlineData(
        EActionCommandType.PlayerBurnApply,
        EPlayerAttributeType.Burn,
        ECardAttributeType.BurnApplyAmount,
        ECardStats.BurnAdded
    )]
    [InlineData(
        EActionCommandType.PlayerPoisonApply,
        EPlayerAttributeType.Poison,
        ECardAttributeType.PoisonApplyAmount,
        ECardStats.PoisonAdded
    )]
    [InlineData(
        EActionCommandType.PlayerRegenApply,
        EPlayerAttributeType.HealthRegen,
        ECardAttributeType.RegenApplyAmount,
        ECardStats.RegenAdded
    )]
    public void Recovers_crit_count_for_crit_capable_player_effects_missing_native_markers(
        EActionCommandType action,
        EPlayerAttributeType playerAttribute,
        ECardAttributeType sourceAttribute,
        ECardStats statistic
    )
    {
        var simulation = new CombatSim();
        simulation.Frames.Clear();
        var previous = 0;
        foreach (var appliedValue in new[] { 4, 8 })
        {
            var frame = new CombatSimFrame();
            frame.Events.Add(Executed("source", action, Player(ECombatantId.Player)));
            frame.PlayerUpdates = new CombatSimPlayerUpdate
            {
                Attributes =
                {
                    [playerAttribute] = new CombatSimPlayerAttributeUpdate
                    {
                        AttributeType = playerAttribute,
                        PreviousValue = previous,
                        CurrentValue = previous + appliedValue,
                    },
                },
            };
            simulation.Frames.Add(frame);
            previous += appliedValue;
        }
        simulation.CardStats["source"] = new Dictionary<ECardStats, int> { [statistic] = 12 };

        var group = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector
                        .Project(simulation, EntitiesWithSourceAttribute(sourceAttribute, 4))
                        .Sources
                )
                .Groups
        );

        Assert.Equal(
            action switch
            {
                EActionCommandType.PlayerBurnApply => CombatImpactKind.Burn,
                EActionCommandType.PlayerPoisonApply => CombatImpactKind.Poison,
                EActionCommandType.PlayerRegenApply => CombatImpactKind.AttributeChange,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
            },
            group.Kind
        );
        Assert.Equal(2, group.Count);
        Assert.Equal(1, group.CriticalCount);
        Assert.Equal(8, group.CriticalObservedValue);
    }

    [Fact]
    public void Recovers_dynamic_zarlic_burn_crits_from_historical_apply_values()
    {
        var simulation = new CombatSim();
        simulation.Frames.Clear();
        simulation.Frames.Add(SourceAttributeFrame(ECardAttributeType.BurnApplyAmount, 4, 5));
        AddPlayerEffectFrames(simulation, EPlayerAttributeType.Burn, 0, 10, 14);
        simulation.Frames.Add(SourceAttributeFrame(ECardAttributeType.BurnApplyAmount, 5, 6));
        AddPlayerEffectFrames(simulation, EPlayerAttributeType.Burn, 14, 25);
        simulation.Frames.Add(SourceAttributeFrame(ECardAttributeType.BurnApplyAmount, 6, 7));
        AddPlayerEffectFrames(simulation, EPlayerAttributeType.Burn, 25, 39, 53);
        simulation.Frames.Add(SourceAttributeFrame(ECardAttributeType.BurnApplyAmount, 7, 8));
        AddPlayerEffectFrames(simulation, EPlayerAttributeType.Burn, 53, 69);
        simulation.Frames.Add(SourceAttributeFrame(ECardAttributeType.BurnApplyAmount, 8, 9));
        AddPlayerEffectFrames(simulation, EPlayerAttributeType.Burn, 69, 87);
        var sameFrameGain = PlayerEffectFrame(
            EActionCommandType.PlayerBurnApply,
            EPlayerAttributeType.Burn,
            87,
            105
        );
        SetSourceAttributeUpdate(sameFrameGain, ECardAttributeType.BurnApplyAmount, 9, 10);
        simulation.Frames.Add(sameFrameGain);
        AddPlayerEffectFrames(simulation, EPlayerAttributeType.Burn, 105, 125, 145);
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.BurnAdded] = 147,
        };

        var burn = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector
                        .Project(
                            simulation,
                            EntitiesWithSourceAttribute(ECardAttributeType.BurnApplyAmount, 4)
                        )
                        .Sources
                )
                .Groups
        );

        Assert.Equal(10, burn.Count);
        Assert.Equal(145, burn.ObservedValue);
        Assert.Equal(147, burn.AuthoritativeMetric?.Value);
        Assert.Equal(9, burn.CriticalCount);
        Assert.Equal(142, burn.CriticalObservedValue);
    }

    [Fact]
    public void Does_not_guess_crit_count_when_dynamic_apply_values_have_ambiguous_subsets()
    {
        var simulation = new CombatSim();
        simulation.Frames.Clear();
        simulation.Frames.Add(
            PlayerEffectFrame(EActionCommandType.PlayerBurnApply, EPlayerAttributeType.Burn, 0, 1)
        );
        simulation.Frames.Add(SourceAttributeFrame(ECardAttributeType.BurnApplyAmount, 1, 2));
        simulation.Frames.Add(
            PlayerEffectFrame(EActionCommandType.PlayerBurnApply, EPlayerAttributeType.Burn, 1, 3)
        );
        simulation.Frames.Add(SourceAttributeFrame(ECardAttributeType.BurnApplyAmount, 2, 3));
        simulation.Frames.Add(
            PlayerEffectFrame(EActionCommandType.PlayerBurnApply, EPlayerAttributeType.Burn, 3, 6)
        );
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.BurnAdded] = 9,
        };

        var burn = Assert.Single(
            Assert
                .Single(
                    CombatImpactProjector
                        .Project(
                            simulation,
                            EntitiesWithSourceAttribute(ECardAttributeType.BurnApplyAmount, 1)
                        )
                        .Sources
                )
                .Groups
        );

        Assert.Equal(0, burn.CriticalCount);
        Assert.Null(burn.CriticalObservedValue);
    }

    [Fact]
    public void Generic_player_attribute_changes_use_the_unique_same_frame_transition()
    {
        var cases = new[]
        {
            (EPlayerAttributeType.HealthRegen, ECardStats.RegenAdded, "RegenApplyAmount"),
            (EPlayerAttributeType.Rage, ECardStats.RageAdded, "RageApplyAmount"),
        };
        foreach (var (attribute, statistic, canonicalKey) in cases)
        {
            var simulation = new CombatSim();
            simulation
                .Frames[0]
                .Events.Add(
                    Executed(
                        "source",
                        EActionCommandType.PlayerModifyAttribute,
                        Player(ECombatantId.Player)
                    )
                );
            simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
            {
                Attributes =
                {
                    [attribute] = new CombatSimPlayerAttributeUpdate
                    {
                        AttributeType = attribute,
                        PreviousValue = 0,
                        CurrentValue = 12,
                    },
                },
            };
            simulation.CardStats["source"] = new Dictionary<ECardStats, int> { [statistic] = 20 };

            var source = Assert.Single(
                CombatImpactProjector.Project(simulation, Entities()).Sources
            );
            var concrete = Assert.Single(source.Groups, group => group.Count == 1);
            var authoritative = Assert.Single(
                source.Groups,
                group => group.AuthoritativeMetric != null
            );
            var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));

            Assert.Equal(CombatImpactKind.AttributeChange, concrete.Kind);
            Assert.Equal(CombatImpactEventSurface.PlayerAttribute, concrete.Surface);
            Assert.Equal(attribute.ToString(), concrete.NativeAttributeKey);
            Assert.Equal(12, concrete.ObservedValue);
            Assert.NotNull(concrete.ObservedUnit);
            Assert.Equal(CombatImpactCoverage.LowerBound, concrete.ObservedCoverage);
            Assert.Equal(canonicalKey, authoritative.NativeAttributeKey);
            Assert.Equal(CombatImpactEventSurface.AppliedEffect, authoritative.Surface);
            Assert.Equal(20, authoritative.AuthoritativeMetric?.Value);
            Assert.Equal(12, fact.Value);
            Assert.NotNull(fact.Unit);
            Assert.Equal(CombatImpactValueBasis.NetFrameDelta, fact.ValueBasis);
            Assert.False(fact.UnknownReason.HasFlag(CombatImpactUnknownReason.ValueNotQuantified));
        }
    }

    [Fact]
    public void Generic_player_modifier_ignores_live_health_when_resolving_max_health()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.PlayerModifyAttribute,
                    Player(ECombatantId.Player)
                )
            );
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.Health] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.Health,
                    PreviousValue = 5000,
                    CurrentValue = 6128,
                },
                [EPlayerAttributeType.HealthMax] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.HealthMax,
                    PreviousValue = 5000,
                    CurrentValue = 6128,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal("HealthMax", group.NativeAttributeKey);
        Assert.Equal(1128, group.ObservedValue);
    }

    [Fact]
    public void Card_stats_map_to_explicit_authoritative_metric_domains()
    {
        var simulation = new CombatSim();
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.DamageDone] = 1,
            [ECardStats.ShieldAdded] = 2,
            [ECardStats.HealAdded] = 3,
            [ECardStats.PoisonAdded] = 4,
            [ECardStats.BurnAdded] = 5,
            [ECardStats.HastedCardsCount] = 6,
            [ECardStats.SlowedCardsCount] = 7,
            [ECardStats.FrozenCardsCount] = 8,
            [ECardStats.RegenAdded] = 9,
            [ECardStats.RageAdded] = 10,
            [ECardStats.UseCount] = 11,
        };

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);
        var metrics = source
            .Groups.Select(group => group.AuthoritativeMetric)
            .Where(metric => metric != null)
            .Cast<CombatImpactAuthoritativeMetric>()
            .ToArray();

        Assert.Equal(11, source.UseCount);
        Assert.Equal(10, metrics.Length);
        Assert.Equal(
            7,
            metrics.Count(metric =>
                metric.Basis == CombatImpactAuthoritativeBasis.TotalAmount
                && metric.Unit == CombatImpactValueUnit.Amount
            )
        );
        Assert.Equal(
            3,
            metrics.Count(metric =>
                metric.Basis == CombatImpactAuthoritativeBasis.ApplicationCount
                && metric.Unit == CombatImpactValueUnit.Applications
            )
        );
    }

    [Fact]
    public void Sums_matching_health_adjustments_and_retains_the_critical_subset()
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
                    AttributeChanged = EPlayerHealthChangeType.Shield,
                    Amount = -60,
                    IsCrit = true,
                },
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Damage,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = -100,
                    IsCrit = false,
                },
            },
        };

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);
        var damage = Assert.Single(source.Groups);

        Assert.Equal(160, damage.ObservedValue);
        Assert.Equal(1, damage.CriticalCount);
        Assert.Equal(60, damage.CriticalObservedValue);
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
        Assert.Null(group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.None, group.ObservedCoverage);
        Assert.Equal(200, group.AuthoritativeMetric?.Value);
        var target = Assert.Single(group.Targets);
        Assert.Null(target.ObservedValue);
        Assert.Equal(CombatImpactCoverage.None, target.ObservedCoverage);
    }

    [Fact]
    public void Generic_card_attribute_change_retains_occurrence_without_borrowing_transition()
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
        Assert.Equal(CombatImpactEventSurface.CardAttribute, group.Surface);
        Assert.Equal(ECardAttributeType.CritChance.ToString(), group.NativeAttributeKey);
        Assert.Equal(1, group.Count);
        Assert.Equal(2, group.ObservedValue);
        Assert.Equal(CombatImpactValueUnit.PercentagePoints, group.ObservedUnit);
        Assert.Equal(CombatImpactCoverage.LowerBound, group.ObservedCoverage);
        var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));
        Assert.Equal(2, fact.Value);
        Assert.Equal(CombatImpactValueUnit.PercentagePoints, fact.Unit);
        Assert.Equal(CombatImpactValueBasis.NetFrameDelta, fact.ValueBasis);
    }

    [Fact]
    public void Generic_card_modifier_ignores_live_cooldown_when_resolving_damage_amount()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.CardModifyAttribute, CardTarget("target"))
            );
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.Cooldown] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.Cooldown,
                    PreviousValue = 5000,
                    CurrentValue = 4950,
                },
                [ECardAttributeType.DamageAmount] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    PreviousValue = 0,
                    CurrentValue = 187,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal("DamageAmount", group.NativeAttributeKey);
        Assert.Equal(187, group.ObservedValue);
    }

    [Fact]
    public void Generic_custom_attribute_change_is_not_presented_as_a_count_only_group()
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
                [ECardAttributeType.Custom_0] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.Custom_0,
                    PreviousValue = 0,
                    CurrentValue = 3,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());
        Assert.Empty(report.Sources);
        var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));

        Assert.Equal(3, fact.Value);
        Assert.Equal(CombatImpactValueBasis.NetFrameDelta, fact.ValueBasis);
    }

    [Fact]
    public void Generic_attribute_change_with_multiple_transitions_is_omitted_as_ambiguous()
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
                [ECardAttributeType.DamageAmount] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    PreviousValue = 40,
                    CurrentValue = 50,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());

        Assert.Empty(report.Sources);
        Assert.Null(
            Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities())).Value
        );
    }

    [Theory]
    [InlineData(EActionCommandType.FlyingStart)]
    [InlineData(EActionCommandType.FlyingStop)]
    public void Flying_actions_are_presented_with_exact_source_and_target(EActionCommandType action)
    {
        var simulation = new CombatSim();
        simulation.Frames[0].Events.Add(Executed("source", action, CardTarget("target")));

        var report = CombatImpactProjector.Project(simulation, Entities());
        var source = Assert.Single(report.Sources);
        var group = Assert.Single(source.Groups);

        Assert.Equal(CombatImpactKind.Flying, group.Kind);
        Assert.Equal("Flying", group.NativeAttributeKey);
        Assert.Equal("target", Assert.Single(group.Targets).Entity.Id);
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
        Assert.Equal(1950, group.ObservedValue);
        Assert.Equal(1, group.AuthoritativeMetric?.Value);
        Assert.Equal(CombatImpactValueUnit.Applications, group.AuthoritativeMetric?.Unit);
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

        var report = CombatImpactProjector.Project(simulation, Entities());
        var group = Assert.Single(Assert.Single(report.Sources).Groups);

        Assert.Equal(CombatImpactKind.Destroy, group.Kind);
        Assert.Equal(1, group.Count);
        Assert.Null(group.ObservedValue);
        Assert.Equal("Bread Knife", Assert.Single(group.Targets).Entity.Name);
        var received = Assert.Single(report.Received);
        var incoming = Assert.Single(received.Groups);
        Assert.Equal(CombatImpactKind.Destroy, incoming.Kind);
        Assert.Equal(1, incoming.Count);
        Assert.Null(incoming.ObservedValue);
        Assert.Equal("Fairies", Assert.Single(incoming.Sources).Entity.Name);
    }

    [Fact]
    public void Merges_disable_and_destroy_occurrences_in_both_perspectives()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.CardDisable,
                    new EffectTargetCard { Target = InstanceId.TryParse("target") }
                )
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.CardDestroy,
                    new EffectTargetCard { Target = InstanceId.TryParse("target") }
                )
            );

        var report = CombatImpactProjector.Project(simulation, Entities());
        var caused = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(CombatImpactKind.Destroy, caused.Kind);
        Assert.Equal(
            CombatImpactAggregator.NativeKey(CombatImpactKind.Destroy),
            caused.NativeAttributeKey
        );
        Assert.Equal(2, caused.Count);
        var causedTarget = Assert.Single(caused.Targets);
        Assert.Equal(2, causedTarget.Count);
        Assert.Null(caused.ObservedValue);
        Assert.Null(caused.ObservedUnit);
        Assert.Equal(CombatImpactCoverage.None, caused.ObservedCoverage);
        Assert.Null(caused.AuthoritativeMetric);
        Assert.Equal("2×", CombatImpactMetricFormatter.Group(caused, chinese: false));
        Assert.Equal(
            "×2",
            CombatImpactMetricFormatter.Target(caused, causedTarget, chinese: false)
        );

        Assert.All(
            CombatImpactProjector.ProjectFacts(simulation, Entities()),
            fact =>
            {
                Assert.Null(fact.Value);
                Assert.Null(fact.Unit);
                Assert.Equal(CombatImpactValueBasis.None, fact.ValueBasis);
                Assert.Equal(CombatImpactUnknownReason.ValueNotQuantified, fact.UnknownReason);
            }
        );

        var received = Assert.Single(report.Received);
        var incoming = Assert.Single(received.Groups);
        Assert.Equal(CombatImpactKind.Destroy, incoming.Kind);
        Assert.Equal(2, incoming.Count);
        Assert.Equal(2, Assert.Single(incoming.Sources).Count);
        Assert.Null(incoming.ObservedValue);
        Assert.Null(incoming.ObservedUnit);
        Assert.Equal(CombatImpactCoverage.None, incoming.ObservedCoverage);
        Assert.Equal("2×", CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: false));
        Assert.Equal(
            "×2",
            CombatImpactMetricFormatter.IncomingSource(
                incoming,
                Assert.Single(incoming.Sources),
                chinese: false
            )
        );
    }

    [Fact]
    public void Falls_back_to_exact_trigger_source_when_direct_source_is_not_an_item_or_skill()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "effect",
                    EActionCommandType.PlayerDamage,
                    Player(ECombatantId.Opponent),
                    triggerSource: "source"
                )
            );
        simulation.Frames[0].OpponentUpdates = Damage(-40);

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);

        Assert.Equal("Fairies", source.Entity.Name);
        Assert.Equal(40, Assert.Single(source.Groups).ObservedValue);
    }

    [Fact]
    public void Facts_preserve_unclassified_and_unresolved_executions_before_display_filtering()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectExecuted
                {
                    Source = InstanceId.TryParse("effect"),
                    TriggerSource = InstanceId.TryParse("source"),
                    ActionType = EActionCommandType.GameModifyTime,
                    Target = null!,
                }
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.CardDestroy,
                    CardTarget("missing-target"),
                    triggerSource: "trigger"
                )
            );

        var report = CombatImpactProjector.Project(simulation, Entities());

        Assert.Equal(2, CombatImpactProjector.ProjectFacts(simulation, Entities()).Count);
        var unclassified = CombatImpactProjector.ProjectFacts(simulation, Entities())[0];
        Assert.Equal(0, unclassified.FrameIndex);
        Assert.Equal(0, unclassified.FrameEventIndex);
        Assert.Equal(EActionCommandType.GameModifyTime, unclassified.Action);
        Assert.Equal("effect", unclassified.DirectSourceId);
        Assert.Equal("source", unclassified.TriggerSourceId);
        Assert.Equal("source", unclassified.AttributedSourceId);
        Assert.Equal(CombatImpactSourceAttribution.Trigger, unclassified.SourceAttribution);
        Assert.Equal(CombatImpactTargetKind.Unknown, unclassified.TargetKind);
        Assert.Null(unclassified.TargetId);
        Assert.Null(unclassified.Value);
        Assert.Null(unclassified.Unit);
        Assert.Equal(CombatImpactValueBasis.None, unclassified.ValueBasis);
        Assert.Equal(
            CombatImpactUnknownReason.UnresolvedTarget
                | CombatImpactUnknownReason.ValueNotQuantified,
            unclassified.UnknownReason
        );

        var unresolvedTarget = CombatImpactProjector.ProjectFacts(simulation, Entities())[1];
        Assert.Equal(1, unresolvedTarget.FrameEventIndex);
        Assert.Equal(EActionCommandType.CardDestroy, unresolvedTarget.Action);
        Assert.Equal("source", unresolvedTarget.AttributedSourceId);
        Assert.Equal(CombatImpactSourceAttribution.Direct, unresolvedTarget.SourceAttribution);
        Assert.Equal(CombatImpactTargetKind.Card, unresolvedTarget.TargetKind);
        Assert.Equal("missing-target", unresolvedTarget.TargetId);
        Assert.Equal(
            CombatImpactUnknownReason.UnresolvedTarget
                | CombatImpactUnknownReason.ValueNotQuantified,
            unresolvedTarget.UnknownReason
        );

        var displayed = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(CombatImpactKind.Destroy, displayed.Kind);
        Assert.Equal(1, displayed.UnresolvedTargetCount);
    }

    [Fact]
    public void Facts_disclose_missing_source_but_keep_exact_known_target()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "effect",
                    EActionCommandType.CardHaste,
                    CardTarget("target"),
                    triggerSource: "missing"
                )
            );

        var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));

        Assert.Equal("effect", fact.DirectSourceId);
        Assert.Equal("missing", fact.TriggerSourceId);
        Assert.Null(fact.AttributedSourceId);
        Assert.Equal(CombatImpactSourceAttribution.Unattributed, fact.SourceAttribution);
        Assert.Equal(CombatImpactTargetKind.Card, fact.TargetKind);
        Assert.Equal("target", fact.TargetId);
        Assert.Equal(
            CombatImpactUnknownReason.UnattributedSource
                | CombatImpactUnknownReason.ValueNotQuantified,
            fact.UnknownReason
        );
    }

    [Fact]
    public void Keeps_valid_direct_source_when_trigger_source_is_also_an_activity_entity()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.PlayerDamage,
                    Player(ECombatantId.Opponent),
                    triggerSource: "trigger"
                )
            );
        simulation.Frames[0].OpponentUpdates = Damage(-40);

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);

        Assert.Equal("Fairies", source.Entity.Name);
    }

    [Fact]
    public void Player_heal_does_not_consume_regen_adjustments()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.PlayerHeal, Player(ECombatantId.Player))
            );
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Regen,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = 50,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.Healing, group.Kind);
        Assert.Null(group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.None, group.ObservedCoverage);
    }

    [Fact]
    public void Player_heal_resolves_matching_positive_health_adjustment()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.PlayerHeal, Player(ECombatantId.Player))
            );
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Heal,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = 50,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(50, group.ObservedValue);
        Assert.False(group.ObservedValueIsPartial);
    }

    [Fact]
    public void Counts_trigger_events_with_the_same_activity_source_fallback_rule()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectTriggered
                {
                    Source = InstanceId.TryParse("effect"),
                    TriggerSource = InstanceId.TryParse("source"),
                }
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "effect",
                    EActionCommandType.PlayerDamage,
                    Player(ECombatantId.Opponent),
                    triggerSource: "source"
                )
            );
        simulation.Frames[0].OpponentUpdates = Damage(-40);

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);

        Assert.Equal(1, source.TriggerCount);
    }

    [Fact]
    public void Omits_effect_when_neither_direct_nor_trigger_source_is_an_activity_entity()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "effect",
                    EActionCommandType.PlayerDamage,
                    Player(ECombatantId.Opponent),
                    triggerSource: "missing"
                )
            );
        simulation.Frames[0].OpponentUpdates = Damage(-40);

        Assert.Empty(CombatImpactProjector.Project(simulation, Entities()).Sources);
    }

    [Fact]
    public void Synthetic_zarlic_shape_keeps_self_only_haste_and_separate_application_count()
    {
        const string zarlic = "zarlic";
        const string decoy = "decoy";
        var simulation = new CombatSim();
        simulation.Frames.Clear();
        for (var index = 0; index < 10; index++)
        {
            var frame = new CombatSimFrame();
            frame.Events.Add(
                Executed(
                    zarlic,
                    EActionCommandType.CardHaste,
                    CardTarget(zarlic),
                    triggerSource: zarlic
                )
            );
            var target = InstanceId.TryParse(zarlic);
            frame.CardUpdates[target] = new CombatSimCardUpdate
            {
                CardInstanceId = target,
                Attributes =
                {
                    [ECardAttributeType.Haste] = new CombatSimCardAttributeUpdate
                    {
                        AttributeType = ECardAttributeType.Haste,
                        PreviousValue = 0,
                        CurrentValue = 950,
                    },
                },
            };
            var decoyTarget = InstanceId.TryParse(decoy);
            frame.CardUpdates[decoyTarget] = new CombatSimCardUpdate
            {
                CardInstanceId = decoyTarget,
                Attributes =
                {
                    [ECardAttributeType.Haste] = new CombatSimCardAttributeUpdate
                    {
                        AttributeType = ECardAttributeType.Haste,
                        PreviousValue = 1000,
                        CurrentValue = 950,
                    },
                },
            };
            simulation.Frames.Add(frame);
        }
        simulation.CardStats[zarlic] = new Dictionary<ECardStats, int>
        {
            [ECardStats.HastedCardsCount] = 10,
            [ECardStats.UseCount] = 10,
        };
        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            [zarlic] = new(zarlic, "Zarlic", "Item", null, null, ECombatantId.Player, 0),
            [decoy] = new(decoy, "Other Food", "Item", null, null, ECombatantId.Player, 1),
        };

        var report = CombatImpactProjector.Project(simulation, entities);
        var source = Assert.Single(report.Sources);
        var haste = Assert.Single(source.Groups);

        Assert.Equal(10, source.UseCount);
        Assert.Equal(10, source.EffectCount);
        Assert.Equal(10, haste.Count);
        Assert.Equal(9500, haste.ObservedValue);
        Assert.Equal(CombatImpactValueUnit.Milliseconds, haste.Unit);
        Assert.Equal(10, haste.AuthoritativeMetric?.Value);
        Assert.Equal(
            CombatImpactAuthoritativeBasis.ApplicationCount,
            haste.AuthoritativeMetric?.Basis
        );
        var targetRow = Assert.Single(haste.Targets);
        Assert.Equal("Zarlic", targetRow.Entity.Name);
        Assert.Equal(10, targetRow.Count);
        Assert.Equal(9500, targetRow.ObservedValue);

        var received = Assert.Single(report.Received);
        var incoming = Assert.Single(received.Groups);
        Assert.Equal(CombatImpactKind.Haste, incoming.Kind);
        Assert.Equal(10, incoming.Count);
        Assert.Equal(9500, incoming.ObservedValue);
        Assert.Equal(10, Assert.Single(incoming.Sources).Count);
    }

    [Fact]
    public void Preserves_multiple_exact_targets_in_one_effect_group()
    {
        var simulation = new CombatSim();
        var first = InstanceId.TryParse("target");
        var second = InstanceId.TryParse("target-2");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardHaste, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardHaste, CardTarget("target-2")));
        simulation.Frames[0].CardUpdates[first] = HasteUpdate(first, 950);
        simulation.Frames[0].CardUpdates[second] = HasteUpdate(second, 1950);

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(2, group.Targets.Count);
        Assert.Equal(2, group.Count);
        Assert.Contains(group.Targets, target => target.Entity.Id == "target");
        Assert.Contains(group.Targets, target => target.Entity.Id == "target-2");
        Assert.Equal(
            950,
            Assert.Single(group.Targets, target => target.Entity.Id == "target").ObservedValue
        );
        Assert.Equal(
            1950,
            Assert.Single(group.Targets, target => target.Entity.Id == "target-2").ObservedValue
        );
    }

    [Fact]
    public void Positive_same_frame_haste_from_another_source_does_not_fan_out_targets()
    {
        var simulation = new CombatSim();
        var first = InstanceId.TryParse("target");
        var second = InstanceId.TryParse("target-2");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardHaste, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(Executed("trigger", EActionCommandType.CardHaste, CardTarget("target-2")));
        simulation.Frames[0].CardUpdates[first] = HasteUpdate(first, 950);
        simulation.Frames[0].CardUpdates[second] = HasteUpdate(second, 1950);

        var report = CombatImpactProjector.Project(simulation, Entities());
        var firstGroup = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
        );
        var secondGroup = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "trigger").Groups
        );

        var firstTarget = Assert.Single(firstGroup.Targets);
        Assert.Equal("target", firstTarget.Entity.Id);
        Assert.Equal(950, firstTarget.ObservedValue);
        var secondTarget = Assert.Single(secondGroup.Targets);
        Assert.Equal("target-2", secondTarget.Entity.Id);
        Assert.Equal(1950, secondTarget.ObservedValue);
    }

    [Fact]
    public void Mixed_frame_ambiguity_drops_only_the_duplicated_action_target_value()
    {
        var simulation = new CombatSim();
        var first = InstanceId.TryParse("target");
        var second = InstanceId.TryParse("target-2");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardHaste, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(Executed("trigger", EActionCommandType.CardHaste, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardHaste, CardTarget("target-2")));
        simulation.Frames[0].CardUpdates[first] = HasteUpdate(first, 950);
        simulation.Frames[0].CardUpdates[second] = HasteUpdate(second, 1950);

        var report = CombatImpactProjector.Project(simulation, Entities());
        var fairies = Assert.Single(report.Sources, source => source.Entity.Id == "source");
        var trigger = Assert.Single(report.Sources, source => source.Entity.Id == "trigger");
        var fairiesGroup = Assert.Single(fairies.Groups);
        var triggerGroup = Assert.Single(trigger.Groups);

        Assert.Equal(2, fairiesGroup.Count);
        Assert.Equal(1950, fairiesGroup.ObservedValue);
        Assert.True(fairiesGroup.ObservedValueIsPartial);
        Assert.Null(
            Assert
                .Single(fairiesGroup.Targets, target => target.Entity.Id == "target")
                .ObservedValue
        );
        Assert.Equal(
            1950,
            Assert
                .Single(fairiesGroup.Targets, target => target.Entity.Id == "target-2")
                .ObservedValue
        );
        Assert.Null(triggerGroup.ObservedValue);
        Assert.Equal(CombatImpactCoverage.None, triggerGroup.ObservedCoverage);
    }

    [Fact]
    public void Facts_never_split_one_same_frame_transition_between_contested_executions()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardHaste, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(Executed("trigger", EActionCommandType.CardHaste, CardTarget("target")));
        simulation.Frames[0].CardUpdates[target] = HasteUpdate(target, 950);

        var report = CombatImpactProjector.Project(simulation, Entities());

        Assert.Equal(2, CombatImpactProjector.ProjectFacts(simulation, Entities()).Count);
        Assert.All(
            CombatImpactProjector.ProjectFacts(simulation, Entities()),
            fact =>
            {
                Assert.Null(fact.Value);
                Assert.Null(fact.Unit);
                Assert.Equal(CombatImpactValueBasis.None, fact.ValueBasis);
                Assert.True(
                    fact.UnknownReason.HasFlag(CombatImpactUnknownReason.ValueNotQuantified)
                );
            }
        );
        Assert.All(
            report.Sources.SelectMany(source => source.Groups),
            group => Assert.Null(group.ObservedValue)
        );
    }

    [Fact]
    public void Fact_uses_observed_net_status_delta_when_decay_shares_the_frame()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardHaste, CardTarget("target")));
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.Haste] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.Haste,
                    PreviousValue = 1000,
                    CurrentValue = 1950,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());
        var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));

        Assert.Equal(950, fact.Value);
        Assert.Equal(CombatImpactValueUnit.Milliseconds, fact.Unit);
        Assert.Equal(CombatImpactValueBasis.NetFrameDelta, fact.ValueBasis);
        Assert.Equal(CombatImpactUnknownReason.None, fact.UnknownReason);
        Assert.Equal(950, Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
    }

    [Fact]
    public void Missing_target_update_keeps_occurrence_without_inventing_value()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardHaste, CardTarget("target")));

        var report = CombatImpactProjector.Project(simulation, Entities());
        var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));
        var group = Assert.Single(Assert.Single(report.Sources).Groups);

        Assert.Null(fact.Value);
        Assert.Null(fact.Unit);
        Assert.Equal(CombatImpactValueBasis.None, fact.ValueBasis);
        Assert.True(fact.UnknownReason.HasFlag(CombatImpactUnknownReason.ValueNotQuantified));
        Assert.Equal(1, group.Count);
        Assert.Null(group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.None, group.ObservedCoverage);
    }

    [Fact]
    public void Generic_action_without_a_concrete_attribute_is_not_presented()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.PlayerModifyAttribute,
                    Player(ECombatantId.Player)
                )
            );
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Heal,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = 80,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());
        var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));
        Assert.Null(fact.Value);
        Assert.Null(fact.Unit);
        Assert.Equal(CombatImpactValueBasis.None, fact.ValueBasis);
        Assert.Empty(report.Sources);
    }

    [Fact]
    public void Charge_does_not_treat_ordinary_cooldown_decay_as_an_effect_amount()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardCharge, CardTarget("target")));
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.Cooldown] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.Cooldown,
                    PreviousValue = 5000,
                    CurrentValue = 4000,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());
        var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));
        var group = Assert.Single(Assert.Single(report.Sources).Groups);

        Assert.Null(fact.Value);
        Assert.Null(fact.Unit);
        Assert.Equal(CombatImpactValueBasis.None, fact.ValueBasis);
        Assert.Equal(1, group.Count);
        Assert.Null(group.ObservedValue);
    }

    [Fact]
    public void Exact_health_adjustment_is_marked_as_exact_evidence()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.PlayerDamage, Player(ECombatantId.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = Damage(-40);

        var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));

        Assert.Equal(40, fact.Value);
        Assert.Equal(CombatImpactValueUnit.Amount, fact.Unit);
        Assert.Equal(CombatImpactValueBasis.ExactAdjustment, fact.ValueBasis);
        Assert.Equal(CombatImpactUnknownReason.None, fact.UnknownReason);
    }

    [Fact]
    public void Explicit_player_attribute_actions_preserve_signed_net_delta()
    {
        var cases = new[]
        {
            (EActionCommandType.PlayerRegenApply, EPlayerAttributeType.HealthRegen, 0, 12, 12),
            (EActionCommandType.PlayerRageApply, EPlayerAttributeType.Rage, 0, 9, 9),
            (
                EActionCommandType.PlayerMaxHealthIncrease,
                EPlayerAttributeType.HealthMax,
                100,
                120,
                20
            ),
            (
                EActionCommandType.PlayerMaxHealthDecrease,
                EPlayerAttributeType.HealthMax,
                120,
                100,
                -20
            ),
        };
        foreach (var (action, attribute, previous, current, expected) in cases)
        {
            var simulation = new CombatSim();
            simulation
                .Frames[0]
                .Events.Add(Executed("source", action, Player(ECombatantId.Player)));
            simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
            {
                Attributes =
                {
                    [attribute] = new CombatSimPlayerAttributeUpdate
                    {
                        AttributeType = attribute,
                        PreviousValue = previous,
                        CurrentValue = current,
                    },
                },
            };

            var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));

            Assert.Equal(expected, fact.Value);
            Assert.Equal(CombatImpactValueUnit.Amount, fact.Unit);
            Assert.Equal(CombatImpactValueBasis.NetFrameDelta, fact.ValueBasis);
            Assert.Equal(CombatImpactUnknownReason.None, fact.UnknownReason);
        }
    }

    [Fact]
    public void Applied_regen_and_card_regen_gain_from_one_source_remain_distinct()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.PlayerRegenApply, Player(ECombatantId.Player))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.CardModifyAttribute, CardTarget("target"))
            );
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.HealthRegen] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.HealthRegen,
                    PreviousValue = 0,
                    CurrentValue = 338,
                },
            },
        };
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.RegenApplyAmount] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.RegenApplyAmount,
                    PreviousValue = 0,
                    CurrentValue = 16,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.RegenAdded] = 338,
        };

        var groups = Assert
            .Single(CombatImpactProjector.Project(simulation, Entities()).Sources)
            .Groups;

        var applied = Assert.Single(
            groups,
            group => group.Surface == CombatImpactEventSurface.AppliedEffect
        );
        Assert.Equal("RegenApplyAmount", applied.NativeAttributeKey);
        Assert.Equal(338, applied.AuthoritativeMetric?.Value);
        Assert.Equal(0, applied.CriticalCount);

        var cardGain = Assert.Single(
            groups,
            group => group.Surface == CombatImpactEventSurface.CardAttribute
        );
        Assert.Equal("RegenApplyAmount", cardGain.NativeAttributeKey);
        Assert.Null(cardGain.AuthoritativeMetric);
        Assert.Equal(16, cardGain.ObservedValue);
        Assert.Equal(0, cardGain.CriticalCount);
        Assert.Equal("target", Assert.Single(cardGain.Targets).Entity.Id);
    }

    [Fact]
    public void Ordinary_regen_gain_never_inherits_applied_regen_critical_recovery()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.CardModifyAttribute, CardTarget("target"))
            );
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.RegenApplyAmount] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.RegenApplyAmount,
                    PreviousValue = 0,
                    CurrentValue = 4,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.RegenAdded] = 8,
        };

        var groups = Assert
            .Single(
                CombatImpactProjector
                    .Project(
                        simulation,
                        EntitiesWithSourceAttribute(ECardAttributeType.RegenApplyAmount, 4)
                    )
                    .Sources
            )
            .Groups;

        Assert.All(groups, group => Assert.Equal(0, group.CriticalCount));
        Assert.Equal(
            4,
            Assert
                .Single(groups, group => group.Surface == CombatImpactEventSurface.CardAttribute)
                .ObservedValue
        );
    }

    [Fact]
    public void Max_health_increase_and_decrease_contest_the_same_transition()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.PlayerMaxHealthIncrease,
                    Player(ECombatantId.Player)
                )
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "trigger",
                    EActionCommandType.PlayerMaxHealthDecrease,
                    Player(ECombatantId.Player)
                )
            );
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.HealthMax] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.HealthMax,
                    PreviousValue = 100,
                    CurrentValue = 110,
                },
            },
        };

        var facts = CombatImpactProjector.ProjectFacts(simulation, Entities());

        Assert.Equal(2, facts.Count);
        Assert.All(
            facts,
            fact =>
            {
                Assert.Null(fact.Value);
                Assert.Null(fact.Unit);
                Assert.Equal(CombatImpactValueBasis.None, fact.ValueBasis);
            }
        );
    }

    [Fact]
    public void Generic_card_modifier_does_not_block_a_specific_attribute_transition()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardHaste, CardTarget("target")));
        simulation
            .Frames[0]
            .Events.Add(
                Executed("trigger", EActionCommandType.CardModifyAttribute, CardTarget("target"))
            );
        simulation.Frames[0].CardUpdates[target] = HasteUpdate(target, 950);

        var report = CombatImpactProjector.Project(simulation, Entities());

        Assert.Equal(
            950,
            Assert
                .Single(
                    CombatImpactProjector.ProjectFacts(simulation, Entities()),
                    fact => fact.Action == EActionCommandType.CardHaste
                )
                .Value
        );
        Assert.Null(
            Assert
                .Single(
                    CombatImpactProjector.ProjectFacts(simulation, Entities()),
                    fact => fact.Action == EActionCommandType.CardModifyAttribute
                )
                .Value
        );
        Assert.Equal(
            950,
            Assert
                .Single(
                    Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
                )
                .ObservedValue
        );
        Assert.DoesNotContain(report.Sources, source => source.Entity.Id == "trigger");
    }

    [Fact]
    public void Card_reload_uses_only_a_unique_ammo_transition()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardReload, CardTarget("target")));
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.Ammo] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.Ammo,
                    PreviousValue = 2,
                    CurrentValue = 5,
                },
            },
        };

        var fact = Assert.Single(CombatImpactProjector.ProjectFacts(simulation, Entities()));

        Assert.Equal(3, fact.Value);
        Assert.Equal(CombatImpactValueUnit.Amount, fact.Unit);
        Assert.Equal(CombatImpactValueBasis.NetFrameDelta, fact.ValueBasis);
    }

    [Fact]
    public void Apply_and_remove_actions_contest_the_same_player_attribute_transition()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "source",
                    EActionCommandType.PlayerBurnApply,
                    Player(ECombatantId.Opponent)
                )
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "trigger",
                    EActionCommandType.PlayerBurnRemove,
                    Player(ECombatantId.Opponent)
                )
            );
        simulation.Frames[0].OpponentUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.Burn] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.Burn,
                    PreviousValue = 10,
                    CurrentValue = 16,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());
        var apply = Assert.Single(
            CombatImpactProjector.ProjectFacts(simulation, Entities()),
            fact => fact.Action == EActionCommandType.PlayerBurnApply
        );

        Assert.Null(apply.Value);
        Assert.Equal(CombatImpactValueBasis.None, apply.ValueBasis);
        Assert.Null(Assert.Single(Assert.Single(report.Sources).Groups).ObservedValue);
    }

    [Fact]
    public void Generic_player_modifier_does_not_block_a_specific_attribute_transition()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.PlayerRageApply, Player(ECombatantId.Player))
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    "trigger",
                    EActionCommandType.PlayerModifyAttribute,
                    Player(ECombatantId.Player)
                )
            );
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.Rage] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.Rage,
                    PreviousValue = 0,
                    CurrentValue = 9,
                },
            },
        };

        var report = CombatImpactProjector.Project(simulation, Entities());

        Assert.Equal(
            9,
            Assert
                .Single(
                    CombatImpactProjector.ProjectFacts(simulation, Entities()),
                    fact => fact.Action == EActionCommandType.PlayerRageApply
                )
                .Value
        );
        Assert.Null(
            Assert
                .Single(
                    CombatImpactProjector.ProjectFacts(simulation, Entities()),
                    fact => fact.Action == EActionCommandType.PlayerModifyAttribute
                )
                .Value
        );
        Assert.Equal(
            9,
            Assert
                .Single(
                    Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
                )
                .ObservedValue
        );
        Assert.DoesNotContain(report.Sources, source => source.Entity.Id == "trigger");
    }

    [Fact]
    public void Max_health_increase_and_decrease_use_distinct_directional_group_keys()
    {
        var simulation = new CombatSim();
        simulation.Frames.Clear();
        var increaseFrame = new CombatSimFrame();
        increaseFrame.Events.Add(
            Executed(
                "source",
                EActionCommandType.PlayerMaxHealthIncrease,
                Player(ECombatantId.Player)
            )
        );
        increaseFrame.PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.HealthMax] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.HealthMax,
                    PreviousValue = 100,
                    CurrentValue = 130,
                },
            },
        };
        simulation.Frames.Add(increaseFrame);
        var decreaseFrame = new CombatSimFrame();
        decreaseFrame.Events.Add(
            Executed(
                "source",
                EActionCommandType.PlayerMaxHealthDecrease,
                Player(ECombatantId.Player)
            )
        );
        decreaseFrame.PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.HealthMax] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.HealthMax,
                    PreviousValue = 130,
                    CurrentValue = 120,
                },
            },
        };
        simulation.Frames.Add(decreaseFrame);

        var groups = Assert
            .Single(CombatImpactProjector.Project(simulation, Entities()).Sources)
            .Groups;

        Assert.Equal(2, groups.Count);
        Assert.Equal(
            30,
            Assert
                .Single(groups, group => group.NativeAttributeKey == "HealthMaxIncrease")
                .ObservedValue
        );
        Assert.Equal(
            -10,
            Assert
                .Single(groups, group => group.NativeAttributeKey == "HealthMaxDecrease")
                .ObservedValue
        );
    }

    [Fact]
    public void Quantified_and_unquantified_reload_occurrences_share_one_group_key()
    {
        var simulation = new CombatSim();
        simulation.Frames.Clear();
        var quantified = new CombatSimFrame();
        quantified.Events.Add(
            Executed("source", EActionCommandType.CardReload, CardTarget("target"))
        );
        var target = InstanceId.TryParse("target");
        quantified.CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.Ammo] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.Ammo,
                    PreviousValue = 2,
                    CurrentValue = 5,
                },
            },
        };
        simulation.Frames.Add(quantified);
        var unquantified = new CombatSimFrame();
        unquantified.Events.Add(
            Executed("source", EActionCommandType.CardReload, CardTarget("target"))
        );
        simulation.Frames.Add(unquantified);

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal("ReloadAmount", group.NativeAttributeKey);
        Assert.Equal(2, group.Count);
        Assert.Equal(3, group.ObservedValue);
        Assert.True(group.ObservedValueIsPartial);
    }

    [Fact]
    public void Aura_attribute_change_is_attributed_to_its_exact_activity_source()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("effect"),
                    TriggerSource = InstanceId.TryParse("source"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.DamageAmount] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    PreviousValue = 0,
                    CurrentValue = 187,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal(CombatImpactEventSurface.CardAttribute, group.Surface);
        Assert.Equal("DamageAmount", group.NativeAttributeKey);
        Assert.Equal(187, group.ObservedValue);
        Assert.Equal("target", Assert.Single(group.Targets).Entity.Id);
    }

    [Theory]
    [InlineData(ECardAttributeType.FlyingTargets)]
    [InlineData(ECardAttributeType.DestroyTargets)]
    [InlineData(ECardAttributeType.ForceUseTargets)]
    [InlineData(ECardAttributeType.EnchantTargets)]
    [InlineData(ECardAttributeType.UpgradeTargets)]
    [InlineData(ECardAttributeType.DisableTargets)]
    [InlineData(ECardAttributeType.RepairTargets)]
    [InlineData(ECardAttributeType.TransformTargets)]
    [InlineData(ECardAttributeType.EnchantRemoveTargets)]
    public void Pr132_modifier_aura_attributes_are_preserved(ECardAttributeType attributeType)
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("source"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [attributeType] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = attributeType,
                    PreviousValue = 0,
                    CurrentValue = 2,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal(attributeType.ToString(), group.NativeAttributeKey);
        Assert.Equal(2, group.ObservedValue);
        Assert.Equal("target", Assert.Single(group.Targets).Entity.Id);
    }

    [Theory]
    [InlineData(ECardAttributeType.ChargeAmount)]
    [InlineData(ECardAttributeType.HasteAmount)]
    [InlineData(ECardAttributeType.SlowAmount)]
    [InlineData(ECardAttributeType.FreezeAmount)]
    [InlineData(ECardAttributeType.FlatCooldownReduction)]
    public void Pr132_duration_modifier_units_remain_milliseconds(ECardAttributeType attributeType)
    {
        var group = ProjectSingleAuraAttribute(attributeType, 1500);

        Assert.Equal(CombatImpactValueUnit.Milliseconds, group.ObservedUnit);
        Assert.Equal(1500, group.ObservedValue);
    }

    [Fact]
    public void Pr132_lifesteal_modifier_unit_remains_percentage_points()
    {
        var group = ProjectSingleAuraAttribute(ECardAttributeType.Lifesteal, 12);

        Assert.Equal(CombatImpactValueUnit.PercentagePoints, group.ObservedUnit);
        Assert.Equal(12, group.ObservedValue);
    }

    [Fact]
    public void Aura_attribute_change_is_omitted_when_multiple_sources_claim_one_transition()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("source"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("trigger"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.DamageAmount] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    PreviousValue = 0,
                    CurrentValue = 187,
                },
            },
        };

        Assert.Empty(CombatImpactProjector.Project(simulation, Entities()).Sources);
    }

    [Fact]
    public void Aura_player_max_health_change_is_attributed_to_its_exact_activity_source()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("effect"),
                    TriggerSource = InstanceId.TryParse("source"),
                    AppliedTo = { Player(ECombatantId.Player) },
                }
            );
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.HealthMax] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.HealthMax,
                    PreviousValue = 5000,
                    CurrentValue = 6128,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal("HealthMax", group.NativeAttributeKey);
        Assert.Equal(1128, group.ObservedValue);
        Assert.Equal(
            CombatImpactProjector.PlayerId(ECombatantId.Player),
            Assert.Single(group.Targets).Entity.Id
        );
    }

    [Theory]
    [InlineData(EPlayerAttributeType.CritChance)]
    [InlineData(EPlayerAttributeType.DamageCrit)]
    [InlineData(EPlayerAttributeType.HealAmount)]
    [InlineData(EPlayerAttributeType.ShieldCrit)]
    [InlineData(EPlayerAttributeType.FlatDamageReduction)]
    [InlineData(EPlayerAttributeType.RageMax)]
    [InlineData(EPlayerAttributeType.Gold)]
    [InlineData(EPlayerAttributeType.RerollCostModifier)]
    public void Pr132_player_modifier_and_economy_aura_attributes_are_preserved(
        EPlayerAttributeType attributeType
    )
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("source"),
                    AppliedTo = { Player(ECombatantId.Player) },
                }
            );
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [attributeType] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = attributeType,
                    PreviousValue = 0,
                    CurrentValue = 3,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal(attributeType.ToString(), group.NativeAttributeKey);
        Assert.Equal(3, group.ObservedValue);
        Assert.Equal(
            CombatImpactProjector.PlayerId(ECombatantId.Player),
            Assert.Single(group.Targets).Entity.Id
        );
    }

    [Fact]
    public void Aura_does_not_duplicate_an_explicit_modifier_transition()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("source"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation
            .Frames[0]
            .Events.Add(
                Executed("source", EActionCommandType.CardModifyAttribute, CardTarget("target"))
            );
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.DamageAmount] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    PreviousValue = 0,
                    CurrentValue = 187,
                },
            },
        };

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);
        var group = Assert.Single(source.Groups);

        Assert.Equal(1, group.Count);
        Assert.Equal(187, group.ObservedValue);
    }

    private static CombatImpactGroup ProjectSingleAuraAttribute(
        ECardAttributeType attributeType,
        int currentValue
    )
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    Source = InstanceId.TryParse("source"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [attributeType] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = attributeType,
                    PreviousValue = 0,
                    CurrentValue = currentValue,
                },
            },
        };

        return Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );
    }

    private static CombatSimEventEffectExecuted Executed(
        string source,
        EActionCommandType action,
        IEffectTarget target,
        string? triggerSource = null
    ) =>
        new()
        {
            Source = InstanceId.TryParse(source),
            TriggerSource = triggerSource == null ? null : InstanceId.TryParse(triggerSource),
            ActionType = action,
            Target = target,
        };

    private static EffectTargetPlayer Player(ECombatantId target) => new() { Target = target };

    private static EffectTargetCard CardTarget(string target) =>
        new() { Target = InstanceId.TryParse(target) };

    private static CombatSimPlayerUpdate Damage(int amount) =>
        new()
        {
            HealthAdjustments =
            {
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Damage,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = amount,
                },
            },
        };

    private static CombatSimCardUpdate HasteUpdate(InstanceId target, int amount) =>
        new()
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.Haste] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.Haste,
                    PreviousValue = 0,
                    CurrentValue = amount,
                },
            },
        };

    private static CombatSimFrame PlayerEffectFrame(
        EActionCommandType action,
        EPlayerAttributeType attribute,
        int previous,
        int current
    )
    {
        var frame = new CombatSimFrame();
        frame.Events.Add(Executed("source", action, Player(ECombatantId.Player)));
        frame.PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [attribute] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = attribute,
                    PreviousValue = previous,
                    CurrentValue = current,
                },
            },
        };
        return frame;
    }

    private static CombatSimFrame SourceAttributeFrame(
        ECardAttributeType attribute,
        int previous,
        int current
    )
    {
        var frame = new CombatSimFrame();
        SetSourceAttributeUpdate(frame, attribute, previous, current);
        return frame;
    }

    private static void SetSourceAttributeUpdate(
        CombatSimFrame frame,
        ECardAttributeType attribute,
        int previous,
        int current
    )
    {
        var source = InstanceId.TryParse("source");
        frame.CardUpdates[source] = new CombatSimCardUpdate
        {
            CardInstanceId = source,
            Attributes =
            {
                [attribute] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = attribute,
                    PreviousValue = previous,
                    CurrentValue = current,
                },
            },
        };
    }

    private static void AddPlayerEffectFrames(
        CombatSim simulation,
        EPlayerAttributeType attribute,
        params int[] values
    )
    {
        for (var index = 1; index < values.Length; index++)
        {
            simulation.Frames.Add(
                PlayerEffectFrame(
                    EActionCommandType.PlayerBurnApply,
                    attribute,
                    values[index - 1],
                    values[index]
                )
            );
        }
    }

    private static IReadOnlyDictionary<string, CombatImpactEntity> EntitiesWithSourceAttribute(
        ECardAttributeType attribute,
        int value
    )
    {
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<ECardAttributeType, int> { [attribute] = value },
        };
        return entities;
    }

    private static IReadOnlyDictionary<string, CombatImpactEntity> Entities() =>
        new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal)
        {
            ["source"] = new("source", "Fairies", "Skill", null, null, ECombatantId.Player, 0),
            ["trigger"] = new(
                "trigger",
                "Trigger Item",
                "Item",
                null,
                null,
                ECombatantId.Player,
                1
            ),
            ["effect"] = new("effect", "Effect", "Effect", null, null, ECombatantId.Player, 2),
            ["target"] = new("target", "Bread Knife", "Item", null, null, ECombatantId.Opponent, 1),
            ["target-2"] = new(
                "target-2",
                "The Eclipse",
                "Item",
                null,
                null,
                ECombatantId.Opponent,
                2
            ),
            [CombatImpactProjector.PlayerId(ECombatantId.Player)] = new(
                CombatImpactProjector.PlayerId(ECombatantId.Player),
                "You",
                "Hero",
                null,
                null,
                ECombatantId.Player,
                3
            ),
            [CombatImpactProjector.PlayerId(ECombatantId.Opponent)] = new(
                CombatImpactProjector.PlayerId(ECombatantId.Opponent),
                "Opponent",
                "Hero",
                null,
                null,
                ECombatantId.Opponent,
                4
            ),
        };
}
