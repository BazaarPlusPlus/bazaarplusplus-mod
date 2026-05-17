using System.Collections.Generic;
using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarContextSnapshotTests
{
    private static AutoBazaarContext MakeChoice(int gold = 10, string serverTime = "t1")
        => new()
        {
            ServerTimeUtc = serverTime,
            StateName = AutoBazaarRunStateName.Choice,
            PlayerGold = gold,
            IsEnabled = true,
        };

    [Fact]
    public void Publish_FirstSnapshot_AssignsTickIdOne()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var snap = pub.Publish(MakeChoice());
        Assert.Equal(1UL, snap.TickId);
        Assert.Equal("\"1\"", snap.ETag);
    }

    [Fact]
    public void Publish_IdenticalContent_KeepsSameTickIdAndReference()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var s1 = pub.Publish(MakeChoice(serverTime: "t1"));
        var s2 = pub.Publish(MakeChoice(serverTime: "t2"));  // only ServerTimeUtc differs
        Assert.Equal(s1.TickId, s2.TickId);
        Assert.Same(s1, s2);
    }

    [Fact]
    public void Publish_DifferentPlayerGold_BumpsTickId()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var s1 = pub.Publish(MakeChoice(gold: 10));
        var s2 = pub.Publish(MakeChoice(gold: 11));
        Assert.Equal(1UL, s1.TickId);
        Assert.Equal(2UL, s2.TickId);
        Assert.NotSame(s1, s2);
    }

    [Fact]
    public void Publish_DifferentRunProgressFields_BumpsTickId()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var s1 = pub.Publish(new AutoBazaarContext
        {
            IsEnabled = true,
            StateName = AutoBazaarRunStateName.Choice,
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
        });
        var s2 = pub.Publish(new AutoBazaarContext
        {
            IsEnabled = true,
            StateName = AutoBazaarRunStateName.Choice,
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
        });

        Assert.Equal(1UL, s1.TickId);
        Assert.Equal(2UL, s2.TickId);
    }

    [Fact]
    public void Publish_DifferentStateName_BumpsTickId()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var s1 = pub.Publish(MakeChoice());
        // choice2 has same content as s1 — no bump expected
        var choice2 = new AutoBazaarContext { StateName = AutoBazaarRunStateName.Choice, IsEnabled = true, PlayerGold = 10 };
        var s2 = pub.Publish(choice2);
        Assert.Equal(1UL, s2.TickId);
        Assert.Same(s1, s2);
        // combat state differs — must bump
        var combat = new AutoBazaarContext { StateName = AutoBazaarRunStateName.Combat, IsEnabled = true, PlayerGold = 10 };
        var s3 = pub.Publish(combat);
        Assert.Equal(2UL, s3.TickId);
        Assert.NotSame(s1, s3);
    }

    [Fact]
    public void Publish_BoardItemsDiffer_BumpsTickId()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var withOne = new AutoBazaarContext
        {
            IsEnabled = true,
            StateName = AutoBazaarRunStateName.Choice,
            BoardItems = new[] { new AutoBazaarCardSnapshot { InstanceId = "i1", Kind = AutoBazaarCardKind.Item } },
        };
        var withTwo = new AutoBazaarContext
        {
            IsEnabled = true,
            StateName = AutoBazaarRunStateName.Choice,
            BoardItems = new[]
            {
                new AutoBazaarCardSnapshot { InstanceId = "i1", Kind = AutoBazaarCardKind.Item },
                new AutoBazaarCardSnapshot { InstanceId = "i2", Kind = AutoBazaarCardKind.Item },
            },
        };
        var s1 = pub.Publish(withOne);
        var s2 = pub.Publish(withTwo);
        Assert.NotEqual(s1.TickId, s2.TickId);
    }

    [Fact]
    public void Publish_CardMetadataDiffers_BumpsTickId()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var basic = new AutoBazaarContext
        {
            IsEnabled = true,
            BoardItems = new[]
            {
                new AutoBazaarCardSnapshot
                {
                    InstanceId = "i1",
                    Kind = AutoBazaarCardKind.Item,
                    Type = "Item",
                    Tags = new[] { "Weapon" },
                    HiddenTags = new[] { "Damage" },
                    Attributes = new Dictionary<string, int> { ["DamageAmount"] = 10 },
                    ActiveAbilities = new[]
                    {
                        new AutoBazaarCardAbilitySnapshot
                        {
                            Id = "a1",
                            Action = "TActionDamage",
                            Trigger = "TTriggerOnCardFired",
                        },
                    },
                },
            },
        };
        var changed = new AutoBazaarContext
        {
            IsEnabled = true,
            BoardItems = new[]
            {
                new AutoBazaarCardSnapshot
                {
                    InstanceId = "i1",
                    Kind = AutoBazaarCardKind.Item,
                    Type = "Item",
                    Tags = new[] { "Weapon" },
                    HiddenTags = new[] { "Damage" },
                    Attributes = new Dictionary<string, int> { ["DamageAmount"] = 12 },
                    ActiveAbilities = new[]
                    {
                        new AutoBazaarCardAbilitySnapshot
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
        var pub = new AutoBazaarContextSnapshotPublisher();
        var a = new AutoBazaarContext { IsEnabled = true, AvailableActions = new[] {
            new AutoBazaarDecisionOption { ActionKind = AutoBazaarActionKind.MoveItem, Group = AutoBazaarActionGroup.Move,
                DisplayKey = "MoveItem:i1", CardInstanceId = "i1", TargetSection = AutoBazaarTargetSection.Hand,
                TargetSockets = new[] { "Socket_0", "Socket_1" } } } };
        var b = new AutoBazaarContext { IsEnabled = true, AvailableActions = new[] {
            new AutoBazaarDecisionOption { ActionKind = AutoBazaarActionKind.MoveItem, Group = AutoBazaarActionGroup.Move,
                DisplayKey = "MoveItem:i1", CardInstanceId = "i1", TargetSection = AutoBazaarTargetSection.Hand,
                TargetSockets = new[] { "Socket_0", "Socket_2" } } } };  // sockets differ
        var s1 = pub.Publish(a);
        var s2 = pub.Publish(b);
        Assert.NotEqual(s1.TickId, s2.TickId);
    }

    [Fact]
    public void Current_IsNull_BeforeFirstPublish()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        Assert.Null(pub.Current);
    }

    [Fact]
    public void Current_TracksLatestPublish()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var s = pub.Publish(MakeChoice());
        Assert.Same(s, pub.Current);
    }

    [Fact]
    public void Reset_ClearsCurrentAndResetsTickIdCounter()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        pub.Publish(MakeChoice(gold: 10));
        pub.Publish(MakeChoice(gold: 11));
        pub.Reset();
        Assert.Null(pub.Current);
        var s = pub.Publish(MakeChoice(gold: 12));
        Assert.Equal(1UL, s.TickId);
    }
}
