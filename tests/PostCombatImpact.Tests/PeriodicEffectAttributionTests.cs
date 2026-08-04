using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class PeriodicEffectAttributionTests
{
    private const string DamageIcon = "<sprite name=Damage>";
    private const string ShieldIcon = "<sprite name=Shield>";

    [Fact]
    public void BurnBooksHealthAndShieldWithTwoForOneMitigation()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    EActionCommandType.PlayerBurnApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 100, 100),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.Shield, 3, 3),
                        (EPlayerAttributeType.Burn, 0, 10)
                    )
                ),
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 100, 96),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.Shield, 3, 0),
                        (EPlayerAttributeType.Burn, 10, 9)
                    ),
                    Adjustment(EDamageType.Burn, EPlayerHealthChangeType.Shield, -3),
                    Adjustment(EDamageType.Burn, EPlayerHealthChangeType.Health, -4)
                ),
            },
            ("burner", ECardStats.BurnAdded, 10)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("burner"));
        var impact = impacts[new PeriodicImpactKey("burner", CombatImpactPeriodicKind.Burn)];

        Assert.Equal(4, impact.HealthAmount);
        Assert.Equal(3, impact.ShieldAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);

        var report = CombatImpactProjector.Project(simulation, Entities("burner"));
        var burnGroup = Assert.Single(Assert.Single(report.Sources).Groups);
        Assert.Equal(impact, burnGroup.PeriodicImpact);
    }

    [Fact]
    public void BurnBooksTypedDamageAfterReceivedDamageReduction()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    EActionCommandType.PlayerBurnApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 100, 100),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.Shield, 0, 0),
                        (EPlayerAttributeType.Burn, 0, 10)
                    )
                ),
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 100, 92),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.Shield, 0, 0),
                        (EPlayerAttributeType.Burn, 10, 9),
                        (EPlayerAttributeType.PercentDamageReduction, 20, 20)
                    ),
                    Adjustment(EDamageType.Burn, EPlayerHealthChangeType.Health, -8)
                ),
            },
            ("burner", ECardStats.BurnAdded, 10)
        );

        var impact = PeriodicEffectAttribution.Project(simulation, Entities("burner"))[
            new PeriodicImpactKey("burner", CombatImpactPeriodicKind.Burn)
        ];

        Assert.Equal(8, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void PoisonAppliesBeforeTheFirstTickAndLaterUsesOpeningOwnership()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "first",
                    EActionCommandType.PlayerPoisonApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 1000, 997),
                        (EPlayerAttributeType.HealthMax, 1000, 1000),
                        (EPlayerAttributeType.Poison, 0, 3)
                    ),
                    Adjustment(EDamageType.Poison, EPlayerHealthChangeType.Health, -3)
                ),
                Frame(
                    "second",
                    EActionCommandType.PlayerPoisonApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 997, 994),
                        (EPlayerAttributeType.HealthMax, 1000, 1000),
                        (EPlayerAttributeType.Poison, 3, 6)
                    ),
                    Adjustment(EDamageType.Poison, EPlayerHealthChangeType.Health, -3)
                ),
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 994, 988),
                        (EPlayerAttributeType.HealthMax, 1000, 1000),
                        (EPlayerAttributeType.Poison, 6, 6)
                    ),
                    Adjustment(EDamageType.Poison, EPlayerHealthChangeType.Health, -6)
                ),
            },
            ("first", ECardStats.PoisonAdded, 3),
            ("second", ECardStats.PoisonAdded, 3)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("first", "second"));
        var first = impacts[new PeriodicImpactKey("first", CombatImpactPeriodicKind.Poison)];
        var second = impacts[new PeriodicImpactKey("second", CombatImpactPeriodicKind.Poison)];

        Assert.Equal(9, first.HealthAmount);
        Assert.Equal(3, second.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Proportional, first.Proof);
        Assert.Equal(CombatImpactPeriodicProof.Proportional, second.Proof);
    }

    [Fact]
    public void PoisonBooksTypedDamageAfterReceivedDamageReduction()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "poisoner",
                    EActionCommandType.PlayerPoisonApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 100, 100),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.Poison, 0, 10)
                    )
                ),
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 100, 92),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.Poison, 10, 10),
                        (EPlayerAttributeType.PercentDamageReduction, 20, 20)
                    ),
                    Adjustment(EDamageType.Poison, EPlayerHealthChangeType.Health, -8)
                ),
            },
            ("poisoner", ECardStats.PoisonAdded, 10)
        );

        var impact = PeriodicEffectAttribution.Project(simulation, Entities("poisoner"))[
            new PeriodicImpactKey("poisoner", CombatImpactPeriodicKind.Poison)
        ];

        Assert.Equal(8, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void RegenBooksOnlyHealingAcceptedByTheHealthCap()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "regenerator",
                    EActionCommandType.PlayerRegenApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 95, 95),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 0, 10)
                    )
                ),
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 95, 100),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 10, 10)
                    ),
                    Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 10)
                ),
            },
            ("regenerator", ECardStats.RegenAdded, 10)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("regenerator"));
        var impact = impacts[new PeriodicImpactKey("regenerator", CombatImpactPeriodicKind.Regen)];

        Assert.Equal(5, impact.HealthAmount);
        Assert.Equal(0, impact.ShieldAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void RegenBooksTypedHealingAfterSameFrameStatusDecay()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "regenerator",
                    EActionCommandType.PlayerRegenApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 50, 50),
                        (EPlayerAttributeType.HealthMax, 200, 200),
                        (EPlayerAttributeType.HealthRegen, 0, 100)
                    )
                ),
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 50, 147),
                        (EPlayerAttributeType.HealthMax, 200, 200),
                        (EPlayerAttributeType.HealthRegen, 100, 97)
                    ),
                    Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 97)
                ),
            },
            ("regenerator", ECardStats.RegenAdded, 100)
        );

        var impact = PeriodicEffectAttribution.Project(simulation, Entities("regenerator"))[
            new PeriodicImpactKey("regenerator", CombatImpactPeriodicKind.Regen)
        ];

        Assert.Equal(97, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void HealthLedgerLetsDamageCreateRoomForSameFrameRegen()
    {
        var setup = Frame(
            "poisoner",
            EActionCommandType.PlayerPoisonApply,
            Attributes(
                (EPlayerAttributeType.Health, 100, 100),
                (EPlayerAttributeType.HealthMax, 100, 100),
                (EPlayerAttributeType.Poison, 0, 30),
                (EPlayerAttributeType.HealthRegen, 0, 50)
            )
        );
        setup.Events.Add(Execution("regenerator", EActionCommandType.PlayerRegenApply));
        var tick = Frame(
            null,
            EActionCommandType.None,
            Attributes(
                (EPlayerAttributeType.Health, 100, 100),
                (EPlayerAttributeType.HealthMax, 100, 100),
                (EPlayerAttributeType.Poison, 30, 30),
                (EPlayerAttributeType.HealthRegen, 50, 50)
            ),
            Adjustment(EDamageType.Poison, EPlayerHealthChangeType.Health, -30),
            Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 50)
        );
        var simulation = Simulation(
            new[] { setup, tick },
            ("poisoner", ECardStats.PoisonAdded, 30),
            ("regenerator", ECardStats.RegenAdded, 50)
        );

        var impacts = PeriodicEffectAttribution.Project(
            simulation,
            Entities("poisoner", "regenerator")
        );

        Assert.Equal(
            30,
            impacts[new PeriodicImpactKey("poisoner", CombatImpactPeriodicKind.Poison)].HealthAmount
        );
        Assert.Equal(
            30,
            impacts[
                new PeriodicImpactKey("regenerator", CombatImpactPeriodicKind.Regen)
            ].HealthAmount
        );
    }

    [Fact]
    public void RegenAfterLethalDamageIsNotRealizedWhenCombatantDies()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "regenerator",
                    EActionCommandType.PlayerRegenApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 100, 100),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 0, 10)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (EPlayerAttributeType.Health, 100, -40),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 10, 10)
                    ),
                    Adjustment(EDamageType.Damage, EPlayerHealthChangeType.Health, -150),
                    Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 10)
                ),
            },
            ("regenerator", ECardStats.RegenAdded, 10)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("regenerator"));

        Assert.Empty(impacts);
    }

    [Fact]
    public void LethalBurnIsCappedAtRemainingHealth()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    EActionCommandType.PlayerBurnApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 5, 5),
                        (EPlayerAttributeType.HealthMax, 5, 5),
                        (EPlayerAttributeType.Burn, 0, 20)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (EPlayerAttributeType.Health, 5, -15),
                        (EPlayerAttributeType.HealthMax, 5, 5)
                    ),
                    Adjustment(EDamageType.Burn, EPlayerHealthChangeType.Health, -20)
                ),
            },
            ("burner", ECardStats.BurnAdded, 20)
        );

        var impact = PeriodicEffectAttribution.Project(simulation, Entities("burner"))[
            new PeriodicImpactKey("burner", CombatImpactPeriodicKind.Burn)
        ];

        Assert.Equal(5, impact.HealthAmount);
    }

    [Fact]
    public void PeriodicDamageAfterAnEarlierLethalAdjustmentIsNotRealized()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "poisoner",
                    EActionCommandType.PlayerPoisonApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 10, 10),
                        (EPlayerAttributeType.HealthMax, 10, 10),
                        (EPlayerAttributeType.Poison, 0, 7)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (EPlayerAttributeType.Health, 10, -7),
                        (EPlayerAttributeType.HealthMax, 10, 10)
                    ),
                    Adjustment(EDamageType.Damage, EPlayerHealthChangeType.Health, -10),
                    Adjustment(EDamageType.Poison, EPlayerHealthChangeType.Health, -7)
                ),
            },
            ("poisoner", ECardStats.PoisonAdded, 7)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("poisoner"));

        Assert.Empty(impacts);
    }

    [Fact]
    public void DeathEventRemainsTerminalAfterLaterNumericHealing()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "poisoner",
                    EActionCommandType.PlayerPoisonApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 10, 10),
                        (EPlayerAttributeType.HealthMax, 10, 10),
                        (EPlayerAttributeType.Poison, 0, 5)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (EPlayerAttributeType.Health, 10, 5),
                        (EPlayerAttributeType.HealthMax, 10, 10)
                    ),
                    Adjustment(EDamageType.Damage, EPlayerHealthChangeType.Health, -20),
                    Adjustment(EDamageType.Heal, EPlayerHealthChangeType.Health, 20),
                    Adjustment(EDamageType.Poison, EPlayerHealthChangeType.Health, -5)
                ),
            },
            ("poisoner", ECardStats.PoisonAdded, 5)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("poisoner"));

        Assert.Empty(impacts);
    }

    [Fact]
    public void PeriodicDamageBeforeALaterLethalAdjustmentStillCounts()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    EActionCommandType.PlayerBurnApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 15, 15),
                        (EPlayerAttributeType.HealthMax, 15, 15),
                        (EPlayerAttributeType.Burn, 0, 5)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (EPlayerAttributeType.Health, 15, -10),
                        (EPlayerAttributeType.HealthMax, 15, 15)
                    ),
                    Adjustment(EDamageType.Burn, EPlayerHealthChangeType.Health, -5),
                    Adjustment(EDamageType.Damage, EPlayerHealthChangeType.Health, -20)
                ),
            },
            ("burner", ECardStats.BurnAdded, 5)
        );

        var impact = PeriodicEffectAttribution.Project(simulation, Entities("burner"))[
            new PeriodicImpactKey("burner", CombatImpactPeriodicKind.Burn)
        ];

        Assert.Equal(5, impact.HealthAmount);
    }

    [Fact]
    public void BurnShieldConsumptionAfterLethalHealthDamageIsNotRealized()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    EActionCommandType.PlayerBurnApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 10, 10),
                        (EPlayerAttributeType.HealthMax, 10, 10),
                        (EPlayerAttributeType.Shield, 5, 5),
                        (EPlayerAttributeType.Burn, 0, 20)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (EPlayerAttributeType.Health, 10, 0),
                        (EPlayerAttributeType.HealthMax, 10, 10),
                        (EPlayerAttributeType.Shield, 5, 0)
                    ),
                    Adjustment(EDamageType.Damage, EPlayerHealthChangeType.Health, -10),
                    Adjustment(EDamageType.Burn, EPlayerHealthChangeType.Shield, -5)
                ),
            },
            ("burner", ECardStats.BurnAdded, 20)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("burner"));

        Assert.Empty(impacts);
    }

    [Fact]
    public void BurnShieldConsumptionBeforeLethalHealthDamageStillCounts()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    EActionCommandType.PlayerBurnApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 10, 10),
                        (EPlayerAttributeType.HealthMax, 10, 10),
                        (EPlayerAttributeType.Shield, 5, 5),
                        (EPlayerAttributeType.Burn, 0, 20)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (EPlayerAttributeType.Health, 10, 0),
                        (EPlayerAttributeType.HealthMax, 10, 10),
                        (EPlayerAttributeType.Shield, 5, 0)
                    ),
                    Adjustment(EDamageType.Burn, EPlayerHealthChangeType.Shield, -5),
                    Adjustment(EDamageType.Damage, EPlayerHealthChangeType.Health, -10)
                ),
            },
            ("burner", ECardStats.BurnAdded, 20)
        );

        var impact = PeriodicEffectAttribution.Project(simulation, Entities("burner"))[
            new PeriodicImpactKey("burner", CombatImpactPeriodicKind.Burn)
        ];

        Assert.Equal(5, impact.ShieldAmount);
    }

    [Fact]
    public void BurnHealthAndShieldAdjustmentsShareOneAtomicTick()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "burner",
                    EActionCommandType.PlayerBurnApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 5, 5),
                        (EPlayerAttributeType.HealthMax, 5, 5),
                        (EPlayerAttributeType.Shield, 5, 5),
                        (EPlayerAttributeType.Burn, 0, 20)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (EPlayerAttributeType.Health, 5, -15),
                        (EPlayerAttributeType.HealthMax, 5, 5),
                        (EPlayerAttributeType.Shield, 5, 0)
                    ),
                    Adjustment(EDamageType.Burn, EPlayerHealthChangeType.Health, -20),
                    Adjustment(EDamageType.Burn, EPlayerHealthChangeType.Shield, -5)
                ),
            },
            ("burner", ECardStats.BurnAdded, 20)
        );

        var impact = PeriodicEffectAttribution.Project(simulation, Entities("burner"))[
            new PeriodicImpactKey("burner", CombatImpactPeriodicKind.Burn)
        ];

        Assert.Equal(5, impact.HealthAmount);
        Assert.Equal(5, impact.ShieldAmount);
    }

    [Fact]
    public void RegenBeforeLethalDamageStillCountsOnDeathFrame()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "regenerator",
                    EActionCommandType.PlayerRegenApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 90, 90),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 0, 10)
                    )
                ),
                DeathFrame(
                    Attributes(
                        (EPlayerAttributeType.Health, 90, -50),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 10, 10)
                    ),
                    Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 10),
                    Adjustment(EDamageType.Damage, EPlayerHealthChangeType.Health, -150)
                ),
            },
            ("regenerator", ECardStats.RegenAdded, 10)
        );

        var impact = PeriodicEffectAttribution.Project(simulation, Entities("regenerator"))[
            new PeriodicImpactKey("regenerator", CombatImpactPeriodicKind.Regen)
        ];

        Assert.Equal(10, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void RegenCanRescueFromNonPositiveHealthWithoutADeathEvent()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "regenerator",
                    EActionCommandType.PlayerRegenApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 10, 10),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 0, 20)
                    )
                ),
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 10, 10),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 20, 20)
                    ),
                    Adjustment(EDamageType.Damage, EPlayerHealthChangeType.Health, -20),
                    Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 20)
                ),
            },
            ("regenerator", ECardStats.RegenAdded, 20)
        );

        var impact = PeriodicEffectAttribution.Project(simulation, Entities("regenerator"))[
            new PeriodicImpactKey("regenerator", CombatImpactPeriodicKind.Regen)
        ];

        Assert.Equal(20, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void InitialStatusWithoutAnyObservedCandidateIsNotInventedAsCardImpact()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 90, 100),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 10, 10)
                    ),
                    Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 10)
                ),
            }
        );

        var impacts = PeriodicEffectAttribution.Project(
            simulation,
            EntitiesOwnedBy(ECombatantId.Opponent, "possible-source")
        );

        Assert.Empty(impacts);
    }

    [Fact]
    public void StatusAbsentRegenDoesNotUseUnobservedStaticAttributeCandidate()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 90, 100),
                        (EPlayerAttributeType.HealthMax, 100, 100)
                    ),
                    Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 10)
                ),
            }
        );
        var entities = EntitiesOwnedBy(ECombatantId.Opponent, "regenerator")
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        entities["regenerator"] = entities["regenerator"] with
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.RegenApplyAmount] = 10,
            },
        };

        var diagnostics = new PeriodicEffectAttributionDiagnostics();
        var impacts = PeriodicEffectAttribution.Project(simulation, entities, diagnostics);

        Assert.Empty(impacts);
        var gap = Assert.Single(diagnostics.Gaps);
        Assert.Equal(10, gap.HealthAmount);
        Assert.Equal(PeriodicUnknownOrigin.StatusStateAbsent, gap.Origin);
    }

    [Fact]
    public void InitialRegenUsesWholeCombatCandidateOnlyAsApproximate()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 90, 100),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 10, 10)
                    ),
                    Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 10)
                ),
            },
            ("possible-source", ECardStats.RegenAdded, 10)
        );

        var impacts = PeriodicEffectAttribution.Project(
            simulation,
            EntitiesOwnedBy(ECombatantId.Opponent, "possible-source")
        );
        var impact = impacts[
            new PeriodicImpactKey("possible-source", CombatImpactPeriodicKind.Regen)
        ];

        Assert.Equal(10, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Proportional, impact.Proof);
    }

    [Fact]
    public void InitialRegenFallsBackToLaterObservedRegenSourcesAsApproximate()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 50, 60),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 10, 10)
                    ),
                    Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 10)
                ),
                Frame(
                    "regenerator",
                    EActionCommandType.PlayerRegenApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 60, 40),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 10, 20)
                    ),
                    Adjustment(EDamageType.Damage, EPlayerHealthChangeType.Health, -20)
                ),
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes(
                        (EPlayerAttributeType.Health, 40, 60),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.HealthRegen, 20, 20)
                    ),
                    Adjustment(EDamageType.Regen, EPlayerHealthChangeType.Health, 20)
                ),
            },
            ("regenerator", ECardStats.RegenAdded, 10)
        );

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities("regenerator"));
        var impact = impacts[new PeriodicImpactKey("regenerator", CombatImpactPeriodicKind.Regen)];

        Assert.Equal(30, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Proportional, impact.Proof);
    }

    [Fact]
    public void StatusResetClosesTheOldOwnershipEpoch()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "old-source",
                    EActionCommandType.PlayerBurnApply,
                    Attributes((EPlayerAttributeType.Burn, 0, 10))
                ),
                Frame(
                    null,
                    EActionCommandType.None,
                    Attributes((EPlayerAttributeType.Burn, 10, 0))
                ),
                Frame(
                    "new-source",
                    EActionCommandType.PlayerBurnApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 100, 94),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.Shield, 0, 0),
                        (EPlayerAttributeType.Burn, 0, 6)
                    ),
                    Adjustment(EDamageType.Burn, EPlayerHealthChangeType.Health, -6)
                ),
            },
            ("old-source", ECardStats.BurnAdded, 10),
            ("new-source", ECardStats.BurnAdded, 6)
        );

        var impacts = PeriodicEffectAttribution.Project(
            simulation,
            Entities("old-source", "new-source")
        );

        Assert.DoesNotContain(
            new PeriodicImpactKey("old-source", CombatImpactPeriodicKind.Burn),
            impacts
        );
        var impact = impacts[new PeriodicImpactKey("new-source", CombatImpactPeriodicKind.Burn)];
        Assert.Equal(6, impact.HealthAmount);
        Assert.Equal(CombatImpactPeriodicProof.Exact, impact.Proof);
    }

    [Fact]
    public void UnresolvedApplySourceRemainsUnknown()
    {
        var simulation = Simulation(
            new[]
            {
                Frame(
                    "missing-source",
                    EActionCommandType.PlayerPoisonApply,
                    Attributes(
                        (EPlayerAttributeType.Health, 100, 90),
                        (EPlayerAttributeType.HealthMax, 100, 100),
                        (EPlayerAttributeType.Poison, 0, 10)
                    ),
                    Adjustment(EDamageType.Poison, EPlayerHealthChangeType.Health, -10)
                ),
            }
        );
        var diagnostics = new PeriodicEffectAttributionDiagnostics();

        var impacts = PeriodicEffectAttribution.Project(simulation, Entities(), diagnostics);

        Assert.Empty(impacts);
        var gap = Assert.Single(diagnostics.Gaps);
        Assert.Equal(10, gap.HealthAmount);
        Assert.Equal(PeriodicUnknownOrigin.MissingApplySource, gap.Origin);
    }

    [Fact]
    public void FormatterKeepsProofInternalAndUsesNativeImpactIcons()
    {
        var group = new CombatImpactGroup(
            CombatImpactKind.Burn,
            CombatImpactAggregator.NativeKey(CombatImpactKind.Burn),
            1,
            10,
            CombatImpactValueUnit.Amount,
            CombatImpactCoverage.Exact,
            new CombatImpactAuthoritativeMetric(
                CombatImpactKind.Burn,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Burn),
                10,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            ),
            0,
            []
        )
        {
            PeriodicImpact = new CombatImpactPeriodicImpact(
                7,
                2,
                CombatImpactPeriodicProof.Proportional,
                PeriodicEffectAttribution.ModelVersion
            ),
        };

        Assert.Equal("×1 · 10 total", CombatImpactMetricFormatter.Group(group, chinese: false));
        Assert.Equal("×1 · 总计 10", CombatImpactMetricFormatter.Group(group, chinese: true));
        Assert.Equal(
            $"7 {DamageIcon} · 2 {ShieldIcon}",
            CombatImpactMetricFormatter.PeriodicImpact(
                group,
                chinese: false,
                damageMarker: DamageIcon,
                shieldMarker: ShieldIcon
            )
        );
        Assert.Equal(
            $"7 {DamageIcon} · 2 {ShieldIcon}",
            CombatImpactMetricFormatter.PeriodicImpact(
                group,
                chinese: true,
                damageMarker: DamageIcon,
                shieldMarker: ShieldIcon
            )
        );
    }

    private static CombatSim Simulation(
        IReadOnlyList<CombatSimFrame> frames,
        params (string SourceId, ECardStats Stat, int Value)[] stats
    )
    {
        var simulation = new CombatSim { Frames = frames.ToList() };
        foreach (var (sourceId, stat, value) in stats)
        {
            if (!simulation.CardStats.TryGetValue(sourceId, out var sourceStats))
            {
                sourceStats = new Dictionary<ECardStats, int>();
                simulation.CardStats[sourceId] = sourceStats;
            }
            sourceStats[stat] = value;
        }
        return simulation;
    }

    private static CombatSimFrame Frame(
        string? sourceId,
        EActionCommandType action,
        IReadOnlyDictionary<EPlayerAttributeType, CombatSimPlayerAttributeUpdate> attributes,
        params CombatSimPlayerHealthAdjustment[] adjustments
    )
    {
        var frame = new CombatSimFrame
        {
            OpponentUpdates = new CombatSimPlayerUpdate
            {
                Attributes = new Dictionary<EPlayerAttributeType, CombatSimPlayerAttributeUpdate>(
                    attributes
                ),
                HealthAdjustments = adjustments.ToList(),
            },
        };
        if (sourceId != null)
            frame.Events.Add(Execution(sourceId, action));
        return frame;
    }

    private static CombatSimFrame DeathFrame(
        IReadOnlyDictionary<EPlayerAttributeType, CombatSimPlayerAttributeUpdate> attributes,
        params CombatSimPlayerHealthAdjustment[] adjustments
    )
    {
        var frame = Frame(null, EActionCommandType.None, attributes, adjustments);
        frame.Events.Add(new CombatSimEventCombatantDied { CombatantId = ECombatantId.Opponent });
        return frame;
    }

    private static CombatSimEventEffectExecuted Execution(
        string sourceId,
        EActionCommandType action
    ) =>
        new()
        {
            ActionType = action,
            Source = InstanceId.TryParse(sourceId),
            Target = new EffectTargetPlayer { Target = ECombatantId.Opponent },
        };

    private static Dictionary<EPlayerAttributeType, CombatSimPlayerAttributeUpdate> Attributes(
        params (EPlayerAttributeType Type, int Previous, int Current)[] values
    ) =>
        values.ToDictionary(
            value => value.Type,
            value => new CombatSimPlayerAttributeUpdate
            {
                AttributeType = value.Type,
                PreviousValue = value.Previous,
                CurrentValue = value.Current,
            }
        );

    private static CombatSimPlayerHealthAdjustment Adjustment(
        EDamageType damageType,
        EPlayerHealthChangeType pool,
        int amount
    ) =>
        new()
        {
            DamageType = damageType,
            AttributeChanged = pool,
            Amount = amount,
        };

    private static IReadOnlyDictionary<string, CombatImpactEntity> Entities(
        params string[] sourceIds
    ) => EntitiesOwnedBy(ECombatantId.Player, sourceIds);

    private static IReadOnlyDictionary<string, CombatImpactEntity> EntitiesOwnedBy(
        ECombatantId sourceCombatant,
        params string[] sourceIds
    )
    {
        var entities = sourceIds.ToDictionary(
            sourceId => sourceId,
            sourceId => new CombatImpactEntity(
                sourceId,
                sourceId,
                "Item",
                null,
                0,
                CombatantId: sourceCombatant
            ),
            StringComparer.Ordinal
        );
        var opponentId = CombatImpactProjector.PlayerId(ECombatantId.Opponent);
        entities[opponentId] = new CombatImpactEntity(
            opponentId,
            "Opponent",
            "Hero",
            null,
            entities.Count,
            CombatantId: ECombatantId.Opponent
        );
        return entities;
    }
}
