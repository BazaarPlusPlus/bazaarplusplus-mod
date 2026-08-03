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
        Assert.Equal(60, damage.CriticalObservedValue);
        Assert.Equal("Opponent", Assert.Single(damage.Targets).Entity.Name);
        Assert.Equal(1, source.EffectCount);
    }

    [Fact]
    public void Recovers_damage_crits_when_multiple_same_frame_attacks_hide_native_adjustments()
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
                Executed("trigger", EActionCommandType.PlayerDamage, Player(ECombatantId.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatSimPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Damage,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = -37_454,
                    IsCrit = true,
                },
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Damage,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = -86,
                    IsCrit = true,
                },
            },
        };
        var sourceInstance = InstanceId.TryParse("source");
        simulation.Frames[0].CardUpdates[sourceInstance] = new CombatSimCardUpdate
        {
            CardInstanceId = sourceInstance,
            Attributes =
            {
                [ECardAttributeType.DamageAmount] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    PreviousValue = 359,
                    CurrentValue = 18_727,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.DamageDone] = 37_454,
        };
        simulation.CardStats["trigger"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.DamageDone] = 86,
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.DamageAmount] = 359,
            },
        };
        entities["trigger"] = entities["trigger"] with
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.DamageAmount] = 43,
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        var sourceDamage = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
        );
        Assert.Null(sourceDamage.ObservedValue);
        Assert.Equal(37_454, sourceDamage.AuthoritativeMetric?.Value);
        Assert.Equal(1, sourceDamage.CriticalCount);
        Assert.Equal(37_454, sourceDamage.CriticalObservedValue);

        var triggerDamage = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "trigger").Groups
        );
        Assert.Null(triggerDamage.ObservedValue);
        Assert.Equal(86, triggerDamage.AuthoritativeMetric?.Value);
        Assert.Equal(1, triggerDamage.CriticalCount);
        Assert.Equal(86, triggerDamage.CriticalObservedValue);
    }

    [Fact]
    public void Does_not_infer_damage_crits_without_a_native_critical_adjustment()
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
                Executed("trigger", EActionCommandType.PlayerDamage, Player(ECombatantId.Opponent))
            );
        simulation.Frames[0].OpponentUpdates = new CombatSimPlayerUpdate
        {
            HealthAdjustments =
            {
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Damage,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = -20,
                    IsCrit = false,
                },
                new CombatSimPlayerHealthAdjustment
                {
                    DamageType = EDamageType.Damage,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = -10,
                    IsCrit = false,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.DamageDone] = 20,
        };
        simulation.CardStats["trigger"] = new Dictionary<ECardStats, int>
        {
            [ECardStats.DamageDone] = 10,
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.DamageAmount] = 10,
            },
        };
        entities["trigger"] = entities["trigger"] with
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.DamageAmount] = 5,
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.All(
            report.Sources,
            source => Assert.Equal(0, Assert.Single(source.Groups).CriticalCount)
        );
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
        var cases = new List<(
            EPlayerAttributeType Attribute,
            ECardStats Statistic,
            string CanonicalKey
        )>
        {
            (EPlayerAttributeType.HealthRegen, ECardStats.RegenAdded, "RegenApplyAmount"),
            (EPlayerAttributeType.Rage, ECardStats.RageAdded, "RageApplyAmount"),
        };
        if (OptionalCombatTempoTestValues.TryResolve(out var tempo))
        {
            cases.Add((EPlayerAttributeType.Tempo, tempo.AddedStatistic, "TempoApplyAmount"));
        }
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
            Assert.Equal(CombatImpactKind.AttributeChange, concrete.Kind);
            Assert.Equal(CombatImpactEventSurface.PlayerAttribute, concrete.Surface);
            Assert.Equal(attribute.ToString(), concrete.NativeAttributeKey);
            Assert.Equal(12, concrete.ObservedValue);
            Assert.Equal(CombatImpactValueUnit.Amount, concrete.Unit);
            Assert.Equal(CombatImpactCoverage.LowerBound, concrete.ObservedCoverage);
            Assert.Equal(canonicalKey, authoritative.NativeAttributeKey);
            Assert.Equal(CombatImpactEventSurface.AppliedEffect, authoritative.Surface);
            Assert.Equal(20, authoritative.AuthoritativeMetric?.Value);
        }
    }

    [Fact]
    public void Card_stats_map_to_explicit_authoritative_metric_domains()
    {
        var simulation = new CombatSim();
        var statistics = new Dictionary<ECardStats, int>
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
            [ECardStats.UseCount] = 13,
        };
        var hasTempo = OptionalCombatTempoTestValues.TryResolve(out var tempo);
        if (hasTempo)
        {
            statistics[tempo.AddedStatistic] = 11;
            statistics[tempo.SpentStatistic] = 12;
        }
        simulation.CardStats["source"] = statistics;

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);
        var metrics = source
            .Groups.Select(group => group.AuthoritativeMetric)
            .Where(metric => metric != null)
            .Cast<CombatImpactAuthoritativeMetric>()
            .ToArray();

        Assert.Equal(13, source.UseCount);
        Assert.Equal(hasTempo ? 12 : 10, metrics.Length);
        Assert.Equal(
            hasTempo ? 9 : 7,
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
        if (hasTempo)
        {
            Assert.Contains(
                metrics,
                metric =>
                    metric.NativeAttributeKey == "TempoApplyAmount"
                    && metric.Value == 11
                    && metric.Basis == CombatImpactAuthoritativeBasis.TotalAmount
            );
            Assert.Contains(
                metrics,
                metric =>
                    metric.NativeAttributeKey == "TempoRemoveAmount"
                    && metric.Value == 12
                    && metric.Basis == CombatImpactAuthoritativeBasis.TotalAmount
            );
        }
    }

    [Fact]
    public void Card_action_cost_event_is_projected_without_a_compile_time_runtime_type_dependency()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                OptionalCombatSimEventFactory.CardActionCostSpent(
                    "source",
                    EPlayerAttributeType.Tempo,
                    ECardAttributeType.TempoCost
                )
            );

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal("TempoRemoveAmount", group.NativeAttributeKey);
        Assert.Equal(1, group.Count);
        Assert.Null(group.ObservedValue);
        Assert.Null(group.AuthoritativeMetric);
        Assert.Equal(
            CombatImpactProjector.PlayerId(ECombatantId.Player),
            Assert.Single(group.Targets).Entity.Id
        );
    }

    [Fact]
    public void Projects_tempo_gain_and_card_action_cost_spend_with_authoritative_totals()
    {
        if (!OptionalCombatTempoTestValues.TryResolve(out var tempo))
            return;

        var simulation = new CombatSim();
        simulation.Frames.Clear();
        var gainFrame = new CombatSimFrame();
        gainFrame.Events.Add(Executed("source", tempo.ApplyAction, Player(ECombatantId.Player)));
        gainFrame.PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.Tempo] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.Tempo,
                    PreviousValue = 0,
                    CurrentValue = 6,
                },
            },
        };
        simulation.Frames.Add(gainFrame);
        var spendFrame = new CombatSimFrame();
        spendFrame.Events.Add(
            OptionalCombatSimEventFactory.CardActionCostSpent(
                "source",
                EPlayerAttributeType.Tempo,
                ECardAttributeType.TempoCost
            )
        );
        simulation.Frames.Add(spendFrame);
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [tempo.AddedStatistic] = 6,
            [tempo.SpentStatistic] = 4,
        };

        var source = Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources);

        Assert.Equal(2, source.EffectCount);
        var gained = Assert.Single(
            source.Groups,
            group => group.NativeAttributeKey == "TempoApplyAmount"
        );
        Assert.Equal(1, gained.Count);
        Assert.Equal(6, gained.ObservedValue);
        Assert.Equal(6, gained.AuthoritativeMetric?.Value);
        Assert.Equal(
            CombatImpactProjector.PlayerId(ECombatantId.Player),
            Assert.Single(gained.Targets).Entity.Id
        );

        var spent = Assert.Single(
            source.Groups,
            group => group.NativeAttributeKey == "TempoRemoveAmount"
        );
        Assert.Equal(1, spent.Count);
        Assert.Null(spent.ObservedValue);
        Assert.Equal(4, spent.AuthoritativeMetric?.Value);
        Assert.Equal(
            CombatImpactProjector.PlayerId(ECombatantId.Player),
            Assert.Single(spent.Targets).Entity.Id
        );
    }

    [Fact]
    public void Tempo_gain_does_not_claim_a_same_frame_delta_that_also_includes_cost_spend()
    {
        if (!OptionalCombatTempoTestValues.TryResolve(out var tempo))
            return;

        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(Executed("source", tempo.ApplyAction, Player(ECombatantId.Player)));
        simulation
            .Frames[0]
            .Events.Add(
                OptionalCombatSimEventFactory.CardActionCostSpent(
                    "source",
                    EPlayerAttributeType.Tempo,
                    ECardAttributeType.TempoCost
                )
            );
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.Tempo] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.Tempo,
                    PreviousValue = 5,
                    CurrentValue = 7,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [tempo.AddedStatistic] = 6,
            [tempo.SpentStatistic] = 4,
        };

        var groups = Assert
            .Single(CombatImpactProjector.Project(simulation, Entities()).Sources)
            .Groups;

        var gained = Assert.Single(groups, group => group.NativeAttributeKey == "TempoApplyAmount");
        Assert.Null(gained.ObservedValue);
        Assert.Equal(6, gained.AuthoritativeMetric?.Value);
        var spent = Assert.Single(groups, group => group.NativeAttributeKey == "TempoRemoveAmount");
        Assert.Null(spent.ObservedValue);
        Assert.Equal(4, spent.AuthoritativeMetric?.Value);
    }

    [Fact]
    public void Explicit_tempo_remove_reports_the_positive_amount_spent()
    {
        if (!OptionalCombatTempoTestValues.TryResolve(out var tempo))
            return;

        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(Executed("source", tempo.RemoveAction, Player(ECombatantId.Player)));
        simulation.Frames[0].PlayerUpdates = new CombatSimPlayerUpdate
        {
            Attributes =
            {
                [EPlayerAttributeType.Tempo] = new CombatSimPlayerAttributeUpdate
                {
                    AttributeType = EPlayerAttributeType.Tempo,
                    PreviousValue = 7,
                    CurrentValue = 3,
                },
            },
        };
        simulation.CardStats["source"] = new Dictionary<ECardStats, int>
        {
            [tempo.SpentStatistic] = 4,
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal("TempoRemoveAmount", group.NativeAttributeKey);
        Assert.Equal(4, group.ObservedValue);
        Assert.Equal(4, group.AuthoritativeMetric?.Value);
        Assert.Equal(CombatImpactValueUnit.Amount, group.Unit);
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
    }

    [Fact]
    public void Known_effect_attribute_does_not_fall_back_to_an_unrelated_transition()
    {
        var simulation = new CombatSim();
        var target = InstanceId.TryParse("target");
        var executed = Executed(
            "source",
            EActionCommandType.CardModifyAttribute,
            CardTarget("target")
        );
        executed.EffectId = "0";
        simulation.Frames[0].Events.Add(executed);
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.DamageAmount] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    PreviousValue = 40,
                    CurrentValue = 50,
                },
            },
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            AbilityAttributeTypesByEffectId = new Dictionary<string, ECardAttributeType>
            {
                ["0"] = ECardAttributeType.SellPrice,
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.Empty(report.Sources);
    }

    [Fact]
    public void Effect_ids_preserve_concurrent_value_and_crit_gains_from_the_same_replay_frames()
    {
        var simulation = new CombatSim();
        simulation.Frames.Clear();
        for (var frameIndex = 0; frameIndex < 3; frameIndex++)
            simulation.Frames.Add(ValueAndCritGainFrame(frameIndex));

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["pen"] = new CombatImpactEntity(
            "pen",
            "PenFT",
            "Item",
            null,
            5,
            AbilityAttributeTypesByEffectId: new Dictionary<string, ECardAttributeType>
            {
                ["0"] = ECardAttributeType.SellPrice,
            }
        );
        entities["caviar"] = new CombatImpactEntity(
            "caviar",
            "Caviar",
            "Item",
            null,
            6,
            AuraAttributeTypesByEffectId: new Dictionary<string, ECardAttributeType>
            {
                ["1"] = ECardAttributeType.CritChance,
            }
        );
        entities["soul"] = new CombatImpactEntity("soul", "Soul of the District", "Item", null, 7);
        entities["crit-target"] = new CombatImpactEntity(
            "crit-target",
            "Crit Target",
            "Item",
            null,
            8
        );
        entities["value-target"] = new CombatImpactEntity(
            "value-target",
            "Value Target",
            "Item",
            null,
            9
        );

        var report = CombatImpactProjector.Project(simulation, entities);

        var value = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "pen").Groups
        );
        Assert.Equal("SellPrice", value.NativeAttributeKey);
        Assert.Equal(9, value.Count);
        Assert.Equal(27, value.ObservedValue);

        var crit = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "caviar").Groups
        );
        Assert.Equal("CritChance", crit.NativeAttributeKey);
        Assert.Equal(6, crit.Count);
        Assert.Equal(36, crit.ObservedValue);

        var soul = Assert.Single(report.Received, received => received.Entity.Id == "soul");
        Assert.Equal(
            9,
            Assert
                .Single(soul.Groups, group => group.NativeAttributeKey == "SellPrice")
                .ObservedValue
        );
        Assert.Equal(
            18,
            Assert
                .Single(soul.Groups, group => group.NativeAttributeKey == "CritChance")
                .ObservedValue
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
    public void Keeps_configured_control_duration_separate_from_native_target_count()
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
            Assert
                .Single(
                    CombatImpactProjector
                        .Project(
                            simulation,
                            EntitiesWithSourceAttribute(ECardAttributeType.SlowAmount, 2_000)
                        )
                        .Sources
                )
                .Groups
        );

        Assert.Equal(CombatImpactKind.Slow, group.Kind);
        Assert.Equal(CombatImpactValueUnit.Milliseconds, group.Unit);
        Assert.Equal(2_000, group.ObservedValue);
        Assert.Equal(1, group.AuthoritativeMetric?.Value);
        Assert.Equal(CombatImpactValueUnit.Applications, group.AuthoritativeMetric?.Unit);
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
        Assert.Equal(CombatImpactCoverage.None, caused.ObservedCoverage);
        Assert.Null(caused.AuthoritativeMetric);
        Assert.Equal("×2", CombatImpactMetricFormatter.Group(caused, chinese: false));
        Assert.Equal(
            "×2",
            CombatImpactMetricFormatter.Target(caused, causedTarget, chinese: false)
        );

        var received = Assert.Single(report.Received);
        var incoming = Assert.Single(received.Groups);
        Assert.Equal(CombatImpactKind.Destroy, incoming.Kind);
        Assert.Equal(2, incoming.Count);
        Assert.Equal(2, Assert.Single(incoming.Sources).Count);
        Assert.Null(incoming.ObservedValue);
        Assert.Equal(CombatImpactCoverage.None, incoming.ObservedCoverage);
        Assert.Equal("×2", CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: false));
        Assert.Equal(
            "×2",
            CombatImpactMetricFormatter.IncomingSource(
                incoming,
                Assert.Single(incoming.Sources),
                chinese: false
            )
        );
    }

    [Theory]
    [InlineData("effect", "source", "source")]
    [InlineData("source", "trigger", "source")]
    [InlineData("effect", "missing", null)]
    public void Source_attribution_prefers_an_activity_direct_source_then_trigger_source(
        string directSource,
        string triggerSource,
        string? expectedSource
    )
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(
                Executed(
                    directSource,
                    EActionCommandType.PlayerDamage,
                    Player(ECombatantId.Opponent),
                    triggerSource
                )
            );
        simulation.Frames[0].OpponentUpdates = Damage(-40);

        var sources = CombatImpactProjector.Project(simulation, Entities()).Sources;
        if (expectedSource == null)
        {
            Assert.Empty(sources);
            return;
        }

        var source = Assert.Single(sources);
        Assert.Equal(expectedSource, source.Entity.Id);
        Assert.Equal(40, Assert.Single(source.Groups).ObservedValue);
    }

    [Fact]
    public void Filters_unclassified_actions_and_discloses_unresolved_targets()
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

        var displayed = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(CombatImpactKind.Destroy, displayed.Kind);
        Assert.Equal(1, displayed.UnresolvedTargetCount);
    }

    [Theory]
    [InlineData(EDamageType.Regen, null)]
    [InlineData(EDamageType.Heal, 50)]
    public void Player_heal_matches_only_heal_adjustments(
        EDamageType damageType,
        int? expectedValue
    )
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
                    DamageType = damageType,
                    AttributeChanged = EPlayerHealthChangeType.Health,
                    Amount = 50,
                },
            },
        };

        var group = Assert.Single(
            Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
        );

        Assert.Equal(CombatImpactKind.Healing, group.Kind);
        Assert.Equal(expectedValue, group.ObservedValue);
        Assert.Equal(
            expectedValue.HasValue ? CombatImpactCoverage.Exact : CombatImpactCoverage.None,
            group.ObservedCoverage
        );
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
            [zarlic] = new(
                zarlic,
                "Zarlic",
                "Item",
                null,
                0,
                Attributes: new Dictionary<ECardAttributeType, int>
                {
                    [ECardAttributeType.HasteAmount] = 1_000,
                }
            ),
            [decoy] = new(decoy, "Other Food", "Item", null, 1),
        };

        var report = CombatImpactProjector.Project(simulation, entities);
        var source = Assert.Single(report.Sources);
        var haste = Assert.Single(source.Groups);

        Assert.Equal(10, source.UseCount);
        Assert.Equal(10, source.EffectCount);
        Assert.Equal(10, haste.Count);
        Assert.Equal(10_000, haste.ObservedValue);
        Assert.Equal(CombatImpactValueUnit.Milliseconds, haste.Unit);
        Assert.Equal(10, haste.AuthoritativeMetric?.Value);
        Assert.Equal(
            CombatImpactAuthoritativeBasis.ApplicationCount,
            haste.AuthoritativeMetric?.Basis
        );
        var targetRow = Assert.Single(haste.Targets);
        Assert.Equal("Zarlic", targetRow.Entity.Name);
        Assert.Equal(10, targetRow.Count);
        Assert.Equal(10_000, targetRow.ObservedValue);

        var received = Assert.Single(report.Received);
        var incoming = Assert.Single(received.Groups);
        Assert.Equal(CombatImpactKind.Haste, incoming.Kind);
        Assert.Equal(10, incoming.Count);
        Assert.Equal(10_000, incoming.ObservedValue);
        Assert.Equal(10, Assert.Single(incoming.Sources).Count);
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

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.HasteAmount] = 1_000,
            },
        };
        entities["trigger"] = entities["trigger"] with
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.HasteAmount] = 2_000,
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);
        var firstGroup = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
        );
        var secondGroup = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "trigger").Groups
        );

        var firstTarget = Assert.Single(firstGroup.Targets);
        Assert.Equal("target", firstTarget.Entity.Id);
        Assert.Equal(1_000, firstTarget.ObservedValue);
        var secondTarget = Assert.Single(secondGroup.Targets);
        Assert.Equal("target-2", secondTarget.Entity.Id);
        Assert.Equal(2_000, secondTarget.ObservedValue);
    }

    [Fact]
    public void Configured_duration_is_not_lost_when_actions_share_a_frame_and_target()
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

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.HasteAmount] = 1_000,
            },
        };
        entities["trigger"] = entities["trigger"] with
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.HasteAmount] = 2_000,
            },
        };

        var report = CombatImpactProjector.Project(simulation, entities);
        var fairies = Assert.Single(report.Sources, source => source.Entity.Id == "source");
        var trigger = Assert.Single(report.Sources, source => source.Entity.Id == "trigger");
        var fairiesGroup = Assert.Single(fairies.Groups);
        var triggerGroup = Assert.Single(trigger.Groups);

        Assert.Equal(2, fairiesGroup.Count);
        Assert.Equal(2_000, fairiesGroup.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Exact, fairiesGroup.ObservedCoverage);
        Assert.All(fairiesGroup.Targets, target => Assert.Equal(1_000, target.ObservedValue));
        Assert.Equal(2_000, triggerGroup.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Exact, triggerGroup.ObservedCoverage);
    }

    [Fact]
    public void Configured_duration_ignores_status_net_delta_when_decay_shares_the_frame()
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

        var report = CombatImpactProjector.Project(
            simulation,
            EntitiesWithSourceAttribute(ECardAttributeType.HasteAmount, 1_000)
        );
        var group = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(1_000, group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Exact, group.ObservedCoverage);
    }

    [Fact]
    public void Configured_duration_actions_do_not_fall_back_to_target_frame_deltas()
    {
        var cases = new[]
        {
            (EActionCommandType.CardCharge, ECardAttributeType.Cooldown, CombatImpactKind.Charge),
            (EActionCommandType.CardHaste, ECardAttributeType.Haste, CombatImpactKind.Haste),
            (EActionCommandType.CardSlow, ECardAttributeType.Slow, CombatImpactKind.Slow),
            (EActionCommandType.CardFreeze, ECardAttributeType.Freeze, CombatImpactKind.Freeze),
        };
        foreach (var (action, targetAttribute, expectedKind) in cases)
        {
            var simulation = new CombatSim();
            var target = InstanceId.TryParse("target");
            simulation.Frames[0].Events.Add(Executed("source", action, CardTarget("target")));
            simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
            {
                CardInstanceId = target,
                Attributes =
                {
                    [targetAttribute] = new CombatSimCardAttributeUpdate
                    {
                        AttributeType = targetAttribute,
                        PreviousValue = action == EActionCommandType.CardCharge ? 5_000 : 1_000,
                        CurrentValue = action == EActionCommandType.CardCharge ? 4_000 : 1_950,
                    },
                },
            };

            var report = CombatImpactProjector.Project(simulation, Entities());
            var group = Assert.Single(Assert.Single(report.Sources).Groups);
            var targetRow = Assert.Single(group.Targets);
            var incoming = Assert.Single(Assert.Single(report.Received).Groups);
            var incomingSource = Assert.Single(incoming.Sources);

            Assert.Equal(expectedKind, group.Kind);
            Assert.Equal(1, group.Count);
            Assert.Null(group.ObservedValue);
            Assert.Equal(CombatImpactCoverage.None, group.ObservedCoverage);
            Assert.Null(targetRow.ObservedValue);
            Assert.Null(incoming.ObservedValue);
            Assert.Null(incomingSource.ObservedValue);
        }
    }

    [Fact]
    public void Configured_duration_actions_use_one_source_value_in_both_perspectives()
    {
        var cases = new[]
        {
            (
                EActionCommandType.CardCharge,
                ECardAttributeType.ChargeAmount,
                ECardAttributeType.Cooldown,
                CombatImpactKind.Charge
            ),
            (
                EActionCommandType.CardHaste,
                ECardAttributeType.HasteAmount,
                ECardAttributeType.Haste,
                CombatImpactKind.Haste
            ),
            (
                EActionCommandType.CardSlow,
                ECardAttributeType.SlowAmount,
                ECardAttributeType.Slow,
                CombatImpactKind.Slow
            ),
            (
                EActionCommandType.CardFreeze,
                ECardAttributeType.FreezeAmount,
                ECardAttributeType.Freeze,
                CombatImpactKind.Freeze
            ),
        };
        foreach (var (action, sourceAttribute, targetAttribute, expectedKind) in cases)
        {
            var simulation = new CombatSim();
            var target = InstanceId.TryParse("target");
            simulation.Frames[0].Events.Add(Executed("source", action, CardTarget("target")));
            simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
            {
                CardInstanceId = target,
                Attributes =
                {
                    [targetAttribute] = new CombatSimCardAttributeUpdate
                    {
                        AttributeType = targetAttribute,
                        PreviousValue = action == EActionCommandType.CardCharge ? 5_000 : 1_000,
                        CurrentValue = action == EActionCommandType.CardCharge ? 4_000 : 1_950,
                    },
                },
            };

            var report = CombatImpactProjector.Project(
                simulation,
                EntitiesWithSourceAttribute(sourceAttribute, 2_000)
            );
            var caused = Assert.Single(Assert.Single(report.Sources).Groups);
            var causedTarget = Assert.Single(caused.Targets);
            var received = Assert.Single(Assert.Single(report.Received).Groups);
            var receivedSource = Assert.Single(received.Sources);

            Assert.Equal(expectedKind, caused.Kind);
            Assert.Equal(CombatImpactAggregator.NativeKey(expectedKind), caused.NativeAttributeKey);
            Assert.Equal(2_000, caused.ObservedValue);
            Assert.Equal(CombatImpactCoverage.Exact, caused.ObservedCoverage);
            Assert.Equal(2_000, causedTarget.ObservedValue);
            Assert.Equal(CombatImpactCoverage.Exact, causedTarget.ObservedCoverage);
            Assert.Equal(2_000, received.ObservedValue);
            Assert.Equal(CombatImpactCoverage.Exact, received.ObservedCoverage);
            Assert.Equal(2_000, receivedSource.ObservedValue);
            Assert.Equal(CombatImpactCoverage.Exact, receivedSource.ObservedCoverage);
        }
    }

    [Fact]
    public void Configured_duration_uses_the_source_value_at_each_event_frame()
    {
        var simulation = new CombatSim();
        simulation
            .Frames[0]
            .Events.Add(Executed("source", EActionCommandType.CardHaste, CardTarget("target")));
        simulation.Frames.Add(SourceAttributeFrame(ECardAttributeType.HasteAmount, 1_000, 2_000));
        var laterFrame = new CombatSimFrame();
        laterFrame.Events.Add(
            Executed("source", EActionCommandType.CardHaste, CardTarget("target"))
        );
        simulation.Frames.Add(laterFrame);

        var report = CombatImpactProjector.Project(
            simulation,
            EntitiesWithSourceAttribute(ECardAttributeType.HasteAmount, 1_000)
        );
        var caused = Assert.Single(Assert.Single(report.Sources).Groups);
        var received = Assert.Single(Assert.Single(report.Received).Groups);

        Assert.Equal(2, caused.Count);
        Assert.Equal(3_000, caused.ObservedValue);
        Assert.Equal(3_000, Assert.Single(caused.Targets).ObservedValue);
        Assert.Equal(3_000, received.ObservedValue);
        Assert.Equal(3_000, Assert.Single(received.Sources).ObservedValue);
    }

    [Fact]
    public void Charge_uses_each_source_cards_configured_amount()
    {
        var simulation = new CombatSim();
        for (var index = 0; index < 4; index++)
            simulation
                .Frames[0]
                .Events.Add(
                    Executed("finesse", EActionCommandType.CardCharge, CardTarget("target"))
                );
        for (var index = 0; index < 3; index++)
            simulation
                .Frames[0]
                .Events.Add(Executed("wok", EActionCommandType.CardCharge, CardTarget("target")));

        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["finesse"] = new CombatImpactEntity(
            "finesse",
            "Finesse Shield",
            "Skill",
            null,
            5,
            Attributes: new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.ChargeAmount] = 1_000,
            }
        );
        entities["wok"] = new CombatImpactEntity(
            "wok",
            "Jumbo Wok",
            "Item",
            null,
            6,
            Attributes: new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.ChargeAmount] = 2_000,
            }
        );

        var report = CombatImpactProjector.Project(simulation, entities);

        var finesse = Assert.Single(report.Sources, source => source.Entity.Id == "finesse");
        var finesseCharge = Assert.Single(finesse.Groups);
        Assert.Equal(4, finesseCharge.Count);
        Assert.Equal(4_000, finesseCharge.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Exact, finesseCharge.ObservedCoverage);

        var wok = Assert.Single(report.Sources, source => source.Entity.Id == "wok");
        var wokCharge = Assert.Single(wok.Groups);
        Assert.Equal(3, wokCharge.Count);
        Assert.Equal(6_000, wokCharge.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Exact, wokCharge.ObservedCoverage);

        var incoming = Assert.Single(Assert.Single(report.Received).Groups);
        Assert.Equal(7, incoming.Count);
        Assert.Equal(10_000, incoming.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Exact, incoming.ObservedCoverage);
        Assert.Equal(
            "×7 · 10s",
            CombatImpactMetricFormatter.IncomingGroup(incoming, chinese: false)
        );
        Assert.Equal(
            4_000,
            Assert.Single(incoming.Sources, source => source.Entity.Id == "finesse").ObservedValue
        );
        Assert.Equal(
            6_000,
            Assert.Single(incoming.Sources, source => source.Entity.Id == "wok").ObservedValue
        );
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

            var group = Assert.Single(
                Assert.Single(CombatImpactProjector.Project(simulation, Entities()).Sources).Groups
            );

            Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
            Assert.Equal(expected, group.ObservedValue);
            Assert.Equal(CombatImpactValueUnit.Amount, group.Unit);
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
            .Single(
                CombatImpactProjector
                    .Project(
                        simulation,
                        EntitiesWithSourceAttribute(ECardAttributeType.RegenApplyAmount, 4)
                    )
                    .Sources
            )
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
        Assert.All(groups, group => Assert.Equal(0, group.CriticalCount));
    }

    [Theory]
    [InlineData(
        EActionCommandType.PlayerMaxHealthIncrease,
        EActionCommandType.PlayerMaxHealthDecrease,
        ECombatantId.Player,
        EPlayerAttributeType.HealthMax,
        100,
        110
    )]
    [InlineData(
        EActionCommandType.PlayerBurnApply,
        EActionCommandType.PlayerBurnRemove,
        ECombatantId.Opponent,
        EPlayerAttributeType.Burn,
        10,
        16
    )]
    public void Competing_player_actions_do_not_split_one_transition(
        EActionCommandType firstAction,
        EActionCommandType secondAction,
        ECombatantId target,
        EPlayerAttributeType attribute,
        int previous,
        int current
    )
    {
        var simulation = new CombatSim();
        simulation.Frames[0].Events.Add(Executed("source", firstAction, Player(target)));
        simulation.Frames[0].Events.Add(Executed("trigger", secondAction, Player(target)));
        var update = new CombatSimPlayerUpdate
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
        if (target == ECombatantId.Player)
            simulation.Frames[0].PlayerUpdates = update;
        else
            simulation.Frames[0].OpponentUpdates = update;

        var report = CombatImpactProjector.Project(simulation, Entities());

        Assert.NotEmpty(report.Sources);
        Assert.All(
            report.Sources.SelectMany(source => source.Groups),
            group => Assert.Null(group.ObservedValue)
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

        var report = CombatImpactProjector.Project(
            simulation,
            EntitiesWithSourceAttribute(ECardAttributeType.HasteAmount, 1_000)
        );

        Assert.Equal(
            1_000,
            Assert
                .Single(
                    Assert.Single(report.Sources, source => source.Entity.Id == "source").Groups
                )
                .ObservedValue
        );
        Assert.DoesNotContain(report.Sources, source => source.Entity.Id == "trigger");
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
        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal(CombatImpactValueUnit.Amount, group.Unit);
        Assert.Equal(2, group.Count);
        Assert.Equal(3, group.ObservedValue);
        Assert.Equal(CombatImpactCoverage.Partial, group.ObservedCoverage);
    }

    [Theory]
    [InlineData(ECardAttributeType.DamageAmount, 187, "Amount")]
    [InlineData(ECardAttributeType.FlyingTargets, 2, "Amount")]
    [InlineData(ECardAttributeType.DestroyTargets, 2, "Amount")]
    [InlineData(ECardAttributeType.ForceUseTargets, 2, "Amount")]
    [InlineData(ECardAttributeType.EnchantTargets, 2, "Amount")]
    [InlineData(ECardAttributeType.UpgradeTargets, 2, "Amount")]
    [InlineData(ECardAttributeType.DisableTargets, 2, "Amount")]
    [InlineData(ECardAttributeType.RepairTargets, 2, "Amount")]
    [InlineData(ECardAttributeType.TransformTargets, 2, "Amount")]
    [InlineData(ECardAttributeType.EnchantRemoveTargets, 2, "Amount")]
    [InlineData(ECardAttributeType.ChargeAmount, 1500, "Milliseconds")]
    [InlineData(ECardAttributeType.HasteAmount, 1500, "Milliseconds")]
    [InlineData(ECardAttributeType.SlowAmount, 1500, "Milliseconds")]
    [InlineData(ECardAttributeType.FreezeAmount, 1500, "Milliseconds")]
    [InlineData(ECardAttributeType.FlatCooldownReduction, 1500, "Milliseconds")]
    [InlineData(ECardAttributeType.Lifesteal, 12, "PercentagePoints")]
    [InlineData(ECardAttributeType.TempoCost, 2, "Amount")]
    [InlineData(ECardAttributeType.FlatTempoCostReduction, 1, "Amount")]
    [InlineData(ECardAttributeType.PercentTempoCostReduction, 20, "PercentagePoints")]
    public void Pr132_card_modifier_attributes_and_units_are_preserved(
        ECardAttributeType attributeType,
        int value,
        string expectedUnit
    )
    {
        var group = ProjectSingleAuraAttribute(attributeType, value);

        Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
        Assert.Equal(CombatImpactEventSurface.CardAttribute, group.Surface);
        Assert.Equal(attributeType.ToString(), group.NativeAttributeKey);
        Assert.Equal(expectedUnit, group.Unit.ToString());
        Assert.Equal(value, group.ObservedValue);
        Assert.Equal("target", Assert.Single(group.Targets).Entity.Id);
    }

    [Fact]
    public void Newer_tempo_amount_attributes_and_units_are_preserved_when_available()
    {
        if (!OptionalCombatTempoTestValues.TryResolve(out var tempo))
            return;

        var cases = new[]
        {
            (Attribute: tempo.ApplyAmountAttribute, Value: 3),
            (Attribute: tempo.RemoveAmountAttribute, Value: 2),
        };
        foreach (var (attribute, value) in cases)
        {
            var group = ProjectSingleAuraAttribute(attribute, value);

            Assert.Equal(CombatImpactKind.AttributeChange, group.Kind);
            Assert.Equal(CombatImpactEventSurface.CardAttribute, group.Surface);
            Assert.Equal(attribute.ToString(), group.NativeAttributeKey);
            Assert.Equal(CombatImpactValueUnit.Amount, group.Unit);
            Assert.Equal(value, group.ObservedValue);
            Assert.Equal("target", Assert.Single(group.Targets).Entity.Id);
        }
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
    public void Self_reference_aura_recalculations_are_not_counted_as_combat_impact()
    {
        var simulation = new CombatSim();
        var self = InstanceId.TryParse("source");
        var target = InstanceId.TryParse("target");
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    EffectId = "health-formula",
                    Source = self,
                    AppliedTo = { CardTarget("source") },
                }
            );
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventEffectAuraExecuted
                {
                    EffectId = "value-formula",
                    Source = InstanceId.TryParse("trigger"),
                    AppliedTo = { CardTarget("target") },
                }
            );
        simulation.Frames[0].CardUpdates[self] = new CombatSimCardUpdate
        {
            CardInstanceId = self,
            Attributes =
            {
                [ECardAttributeType.DamageAmount] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.DamageAmount,
                    PreviousValue = 6,
                    CurrentValue = 7_018,
                },
            },
        };
        simulation.Frames[0].CardUpdates[target] = new CombatSimCardUpdate
        {
            CardInstanceId = target,
            Attributes =
            {
                [ECardAttributeType.CritChance] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.CritChance,
                    PreviousValue = 64,
                    CurrentValue = 82,
                },
            },
        };
        var entities = Entities().ToDictionary(item => item.Key, item => item.Value);
        entities["source"] = entities["source"] with
        {
            ReferenceValuedAuraEffectIds = ["health-formula"],
        };
        entities["trigger"] = entities["trigger"] with
        {
            AuraAttributeTypesByEffectId = new Dictionary<string, ECardAttributeType>
            {
                ["value-formula"] = ECardAttributeType.CritChance,
            },
            ReferenceValuedAuraEffectIds = ["value-formula"],
        };

        var report = CombatImpactProjector.Project(simulation, entities);

        Assert.DoesNotContain(report.Sources, source => source.Entity.Id == "source");
        Assert.DoesNotContain(report.Received, received => received.Entity.Id == "source");
        var crit = Assert.Single(
            Assert.Single(report.Sources, source => source.Entity.Id == "trigger").Groups
        );
        Assert.Equal("CritChance", crit.NativeAttributeKey);
        Assert.Equal(18, crit.ObservedValue);
        Assert.Equal("target", Assert.Single(crit.Targets).Entity.Id);
    }

    [Theory]
    [InlineData(EPlayerAttributeType.HealthMax, "Amount")]
    [InlineData(EPlayerAttributeType.CritChance, "PercentagePoints")]
    [InlineData(EPlayerAttributeType.DamageCrit, "Amount")]
    [InlineData(EPlayerAttributeType.HealAmount, "Amount")]
    [InlineData(EPlayerAttributeType.ShieldCrit, "Amount")]
    [InlineData(EPlayerAttributeType.FlatDamageReduction, "Amount")]
    [InlineData(EPlayerAttributeType.RageMax, "Amount")]
    [InlineData(EPlayerAttributeType.Tempo, "Amount")]
    [InlineData(EPlayerAttributeType.TempoGainCooldownMax, "Milliseconds")]
    [InlineData(EPlayerAttributeType.FlatTempoGainCooldownReduction, "Milliseconds")]
    [InlineData(EPlayerAttributeType.PercentTempoGainCooldownReduction, "PercentagePoints")]
    [InlineData(EPlayerAttributeType.Gold, "Amount")]
    [InlineData(EPlayerAttributeType.RerollCostModifier, "Amount")]
    public void Pr132_player_modifier_and_economy_aura_attributes_are_preserved(
        EPlayerAttributeType attributeType,
        string expectedUnit
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
        Assert.Equal(expectedUnit, group.Unit.ToString());
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

    private static CombatSimFrame ValueAndCritGainFrame(int frameIndex)
    {
        var frame = new CombatSimFrame();
        foreach (var targetId in new[] { "soul", "crit-target", "value-target" })
        {
            var executed = Executed(
                "pen",
                EActionCommandType.CardModifyAttribute,
                CardTarget(targetId)
            );
            executed.ExecutionContextId = $"pen-context-{frameIndex}";
            executed.EffectId = "0";
            frame.Events.Add(executed);
        }

        frame.Events.Add(
            new CombatSimEventEffectAuraExecuted
            {
                ExecutionContextId = $"caviar-context-{frameIndex}",
                EffectId = "1",
                Source = InstanceId.TryParse("caviar"),
                AppliedTo = { CardTarget("soul"), CardTarget("crit-target") },
            }
        );

        foreach (var targetId in new[] { "soul", "crit-target", "value-target" })
        {
            var target = InstanceId.TryParse(targetId);
            var update = new CombatSimCardUpdate
            {
                CardInstanceId = target,
                Attributes =
                {
                    [ECardAttributeType.SellPrice] = new CombatSimCardAttributeUpdate
                    {
                        AttributeType = ECardAttributeType.SellPrice,
                        PreviousValue = frameIndex * 3,
                        CurrentValue = (frameIndex + 1) * 3,
                    },
                },
            };
            if (targetId != "value-target")
            {
                update.Attributes[ECardAttributeType.CritChance] = new CombatSimCardAttributeUpdate
                {
                    AttributeType = ECardAttributeType.CritChance,
                    PreviousValue = frameIndex * 6,
                    CurrentValue = (frameIndex + 1) * 6,
                };
            }
            frame.CardUpdates[target] = update;
        }

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
            ["source"] = new(
                "source",
                "Fairies",
                "Skill",
                null,
                0,
                CombatantId: ECombatantId.Player
            ),
            ["trigger"] = new("trigger", "Trigger Item", "Item", null, 1),
            ["effect"] = new("effect", "Effect", "Effect", null, 2),
            ["target"] = new("target", "Bread Knife", "Item", null, 1),
            ["target-2"] = new("target-2", "The Eclipse", "Item", null, 2),
            [CombatImpactProjector.PlayerId(ECombatantId.Player)] = new(
                CombatImpactProjector.PlayerId(ECombatantId.Player),
                "You",
                "Hero",
                null,
                3
            ),
            [CombatImpactProjector.PlayerId(ECombatantId.Opponent)] = new(
                CombatImpactProjector.PlayerId(ECombatantId.Opponent),
                "Opponent",
                "Hero",
                null,
                4
            ),
        };
}
