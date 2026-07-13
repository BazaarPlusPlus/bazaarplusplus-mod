using System.Collections.Generic;
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public class BazaarAgentContextSnapshotTests
{
    private static BazaarAgentContext MakeChoice(int gold = 10, string serverTime = "t1") =>
        new()
        {
            ServerTimeUtc = serverTime,
            StateName = BazaarAgentRunStateName.Choice,
            PlayerGold = gold,
        };

    [Fact]
    public void Publish_FirstSnapshot_AssignsTickIdOne()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var snap = pub.Publish(MakeChoice());
        Assert.Equal(1UL, snap.TickId);
        Assert.Equal("\"1\"", snap.ETag);
    }

    [Fact]
    public void Publish_IdenticalContent_KeepsSameTickIdAndReference()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var s1 = pub.Publish(MakeChoice(serverTime: "t1"));
        var s2 = pub.Publish(MakeChoice(serverTime: "t2")); // only ServerTimeUtc differs
        Assert.Equal(s1.TickId, s2.TickId);
        Assert.Same(s1, s2);
    }

    [Fact]
    public void Publish_IdenticalContent_ReportsOnlyFirstPublication()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();

        pub.Publish(MakeChoice(serverTime: "t1"), out var firstPublication);
        pub.Publish(MakeChoice(serverTime: "t2"), out var repeatedPublication);

        Assert.True(firstPublication);
        Assert.False(repeatedPublication);
    }

    [Fact]
    public void Publish_DifferentPlayerGold_BumpsTickId()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var s1 = pub.Publish(MakeChoice(gold: 10));
        var s2 = pub.Publish(MakeChoice(gold: 11));
        Assert.Equal(1UL, s1.TickId);
        Assert.Equal(2UL, s2.TickId);
        Assert.NotSame(s1, s2);
    }

    [Fact]
    public void Publish_ReplayPhaseChange_BumpsTickIdAndClonesFields()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var s1 = pub.Publish(
            new BazaarAgentContext
            {
                StateName = BazaarAgentRunStateName.Replay,
                ReplayPhase = BazaarAgentReplayPhase.Playing,
                ReplayBattleId = "battle-7",
            }
        );
        var s2 = pub.Publish(
            new BazaarAgentContext
            {
                StateName = BazaarAgentRunStateName.Replay,
                ReplayPhase = BazaarAgentReplayPhase.FinishedAwaitingContinue,
                ReplayBattleId = "battle-7",
            }
        );
        Assert.Equal(2UL, s2.TickId);
        Assert.NotSame(s1, s2);
        Assert.NotEqual(s1.ETag, s2.ETag);
        // CloneWithTickId must carry the replay fields into the published snapshot.
        Assert.Equal(BazaarAgentReplayPhase.FinishedAwaitingContinue, s2.Context.ReplayPhase);
        Assert.Equal("battle-7", s2.Context.ReplayBattleId);
    }

    [Fact]
    public void Publish_SameReplayPhaseAndBattleId_KeepsTickId()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var s1 = pub.Publish(
            new BazaarAgentContext
            {
                ReplayPhase = BazaarAgentReplayPhase.FinishedAwaitingContinue,
                ReplayBattleId = "battle-7",
            }
        );
        var s2 = pub.Publish(
            new BazaarAgentContext
            {
                ReplayPhase = BazaarAgentReplayPhase.FinishedAwaitingContinue,
                ReplayBattleId = "battle-7",
            }
        );
        Assert.Same(s1, s2);
    }

    [Fact]
    public void Publish_ReplayBattleIdChange_BumpsTickId()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var s1 = pub.Publish(
            new BazaarAgentContext
            {
                ReplayPhase = BazaarAgentReplayPhase.Starting,
                ReplayBattleId = "a",
            }
        );
        var s2 = pub.Publish(
            new BazaarAgentContext
            {
                ReplayPhase = BazaarAgentReplayPhase.Starting,
                ReplayBattleId = "b",
            }
        );
        Assert.Equal(1UL, s1.TickId);
        Assert.Equal(2UL, s2.TickId);
    }

    [Fact]
    public void Publish_DifferentRunProgressFields_BumpsTickId()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var s1 = pub.Publish(
            new BazaarAgentContext
            {
                StateName = BazaarAgentRunStateName.Choice,
                PlayerHero = "Vanessa",
                Day = 1,
                Hour = 2,
                Wins = 0,
                Losses = 0,
                PlayerHealth = 100,
                PlayerMaxHealth = 100,
                PlayerPrestige = 20,
                PlayerLevel = 1,
                PlayerIncome = 1,
                CurrentEncounterType = "TCardEncounterEvent",
            }
        );
        var s2 = pub.Publish(
            new BazaarAgentContext
            {
                StateName = BazaarAgentRunStateName.Choice,
                PlayerHero = "Vanessa",
                Day = 1,
                Hour = 3,
                Wins = 0,
                Losses = 0,
                PlayerHealth = 100,
                PlayerMaxHealth = 100,
                PlayerPrestige = 20,
                PlayerLevel = 1,
                PlayerIncome = 1,
                CurrentEncounterType = "TCardEncounterEvent",
            }
        );

        Assert.Equal(1UL, s1.TickId);
        Assert.Equal(2UL, s2.TickId);
    }

    [Fact]
    public void Publish_DifferentStateName_BumpsTickId()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var s1 = pub.Publish(MakeChoice());
        // choice2 has same content as s1 — no bump expected
        var choice2 = new BazaarAgentContext
        {
            StateName = BazaarAgentRunStateName.Choice,
            PlayerGold = 10,
        };
        var s2 = pub.Publish(choice2);
        Assert.Equal(1UL, s2.TickId);
        Assert.Same(s1, s2);
        // combat state differs — must bump
        var combat = new BazaarAgentContext
        {
            StateName = BazaarAgentRunStateName.Combat,
            PlayerGold = 10,
        };
        var s3 = pub.Publish(combat);
        Assert.Equal(2UL, s3.TickId);
        Assert.NotSame(s1, s3);
    }

    [Fact]
    public void Publish_BoardItemsDiffer_BumpsTickId()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var withOne = new BazaarAgentContext
        {
            StateName = BazaarAgentRunStateName.Choice,
            BoardItems = new[]
            {
                new BazaarAgentCardSnapshot { InstanceId = "i1", Kind = BazaarAgentCardKind.Item },
            },
        };
        var withTwo = new BazaarAgentContext
        {
            StateName = BazaarAgentRunStateName.Choice,
            BoardItems = new[]
            {
                new BazaarAgentCardSnapshot { InstanceId = "i1", Kind = BazaarAgentCardKind.Item },
                new BazaarAgentCardSnapshot { InstanceId = "i2", Kind = BazaarAgentCardKind.Item },
            },
        };
        var s1 = pub.Publish(withOne);
        var s2 = pub.Publish(withTwo);
        Assert.NotEqual(s1.TickId, s2.TickId);
    }

    [Fact]
    public void Publish_CardMetadataDiffers_BumpsTickId()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var basic = new BazaarAgentContext
        {
            BoardItems = new[]
            {
                new BazaarAgentCardSnapshot
                {
                    InstanceId = "i1",
                    Kind = BazaarAgentCardKind.Item,
                    Type = "Item",
                    Tags = new[] { "Weapon" },
                    HiddenTags = new[] { "Damage" },
                    Attributes = new Dictionary<string, int> { ["DamageAmount"] = 10 },
                    ActiveAbilities = new[]
                    {
                        new BazaarAgentCardAbilitySnapshot
                        {
                            Id = "a1",
                            Action = "TActionDamage",
                            Trigger = "TTriggerOnCardFired",
                        },
                    },
                },
            },
        };
        var changed = new BazaarAgentContext
        {
            BoardItems = new[]
            {
                new BazaarAgentCardSnapshot
                {
                    InstanceId = "i1",
                    Kind = BazaarAgentCardKind.Item,
                    Type = "Item",
                    Tags = new[] { "Weapon" },
                    HiddenTags = new[] { "Damage" },
                    Attributes = new Dictionary<string, int> { ["DamageAmount"] = 12 },
                    ActiveAbilities = new[]
                    {
                        new BazaarAgentCardAbilitySnapshot
                        {
                            Id = "a1",
                            Action = "TActionDamage",
                            Trigger = "TTriggerOnCardFired",
                        },
                    },
                },
            },
        };

        var s1 = pub.Publish(basic);
        var s2 = pub.Publish(changed);

        Assert.NotEqual(s1.TickId, s2.TickId);
    }

    [Fact]
    public void Publish_AvailableActionsSocketsDiffer_BumpsTickId()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var a = new BazaarAgentContext
        {
            AvailableActions = new[]
            {
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.MoveItem,
                    Group = BazaarAgentActionGroup.Move,
                    DisplayKey = "MoveItem:i1",
                    CardInstanceId = "i1",
                    TargetSection = BazaarAgentTargetSection.Hand,
                    TargetSockets = new[] { "Socket_0", "Socket_1" },
                },
            },
        };
        var b = new BazaarAgentContext
        {
            AvailableActions = new[]
            {
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.MoveItem,
                    Group = BazaarAgentActionGroup.Move,
                    DisplayKey = "MoveItem:i1",
                    CardInstanceId = "i1",
                    TargetSection = BazaarAgentTargetSection.Hand,
                    TargetSockets = new[] { "Socket_0", "Socket_2" },
                },
            },
        }; // sockets differ
        var s1 = pub.Publish(a);
        var s2 = pub.Publish(b);
        Assert.NotEqual(s1.TickId, s2.TickId);
    }

    [Fact]
    public void Current_IsNull_BeforeFirstPublish()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        Assert.Null(pub.Current);
    }

    [Fact]
    public void Current_TracksLatestPublish()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        var s = pub.Publish(MakeChoice());
        Assert.Same(s, pub.Current);
    }

    [Fact]
    public void Reset_ClearsCurrentAndResetsTickIdCounter()
    {
        var pub = new BazaarAgentContextSnapshotPublisher();
        pub.Publish(MakeChoice(gold: 10));
        pub.Publish(MakeChoice(gold: 11));
        pub.Reset();
        Assert.Null(pub.Current);
        var s = pub.Publish(MakeChoice(gold: 12));
        Assert.Equal(1UL, s.TickId);
    }
}
