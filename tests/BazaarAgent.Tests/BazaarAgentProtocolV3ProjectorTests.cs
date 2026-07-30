#nullable enable
using BazaarPlusPlus.BazaarAgent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class BazaarAgentProtocolV3ProjectorTests
{
    [Fact]
    public void Bootstrap_is_full_and_a_later_move_is_a_group_delta_with_stable_short_id()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var board = Card("real-item", BazaarAgentCardLocation.Board, "Socket_2");
        var first = projector.Bootstrap(
            Snapshot(1, new[] { board }, Array.Empty<BazaarAgentCardSnapshot>())
        );

        var firstCard = Assert.Single(first.View.Board!.Upsert!);
        Assert.True(first.View.IsFull);
        Assert.Equal("i00001", firstCard.Id);
        Assert.Equal("t00001", firstCard.Template);
        Assert.Equal("Test Item", firstCard.Name);
        Assert.Equal("Deal 10 damage.", firstCard.Description);
        Assert.Equal(6, firstCard.CooldownSeconds);
        Assert.Equal(0, firstCard.Ammo);
        Assert.Equal(3, firstCard.AmmoMax);
        Assert.Equal(new[] { 2, 3 }, firstCard.Slots);

        var moved = Card("real-item", BazaarAgentCardLocation.Chest, "Socket_0");
        Assert.True(
            projector.TryProjectDelta(
                Snapshot(2, Array.Empty<BazaarAgentCardSnapshot>(), new[] { moved }),
                first.SessionId,
                1,
                out var second
            )
        );

        Assert.Null(second.View.IsFull);
        Assert.Equal(new[] { "i00001" }, second.View.Board!.Remove);
        var movedCard = Assert.Single(second.View.Chest!.Upsert!);
        Assert.Equal("i00001", movedCard.Id);
        Assert.Equal(new[] { 0, 1 }, movedCard.Slots);
        Assert.Null(movedCard.Template);
        Assert.Null(movedCard.Name);
    }

    [Fact]
    public void Delta_requires_the_last_revision_for_its_session()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                new[] { Card("one", BazaarAgentCardLocation.Board, "Socket_0") },
                Array.Empty<BazaarAgentCardSnapshot>()
            )
        );

        Assert.False(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    new[] { Card("one", BazaarAgentCardLocation.Board, "Socket_0") },
                    Array.Empty<BazaarAgentCardSnapshot>()
                ),
                first.SessionId,
                0,
                out _
            )
        );
        Assert.False(projector.IsCurrentSession(first.SessionId, 0, 1));
        Assert.True(projector.IsCurrentSession(first.SessionId, 1, 1));
    }

    [Fact]
    public void Changed_card_delta_omits_unchanged_identity_and_uses_description_instead_of_attributes()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                new[] { Card("one", BazaarAgentCardLocation.Board, "Socket_0") },
                Array.Empty<BazaarAgentCardSnapshot>()
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    new[]
                    {
                        Card(
                            "one",
                            BazaarAgentCardLocation.Board,
                            "Socket_0",
                            description: "Deal 14 damage."
                        ),
                    },
                    Array.Empty<BazaarAgentCardSnapshot>()
                ),
                first.SessionId,
                1,
                out var changed
            )
        );

        var card = Assert.Single(changed.View.Board!.Upsert!);
        Assert.Equal("i00001", card.Id);
        Assert.Null(card.Template);
        Assert.Null(card.Name);
        Assert.Equal("Deal 14 damage.", card.Description);
        var json = JObject.Parse(JsonConvert.SerializeObject(changed.View));
        Assert.Null(json["board"]!["upsert"]![0]!["attributes"]);
        Assert.Null(json["board"]!["upsert"]![0]!["abilities"]);
        Assert.Null(json["board"]!["upsert"]![0]!["canSelect"]);
        Assert.Null(json["board"]!["upsert"]![0]!["canFit"]);
        Assert.Null(json["board"]!["upsert"]![0]!["free"]);
        Assert.Null(json["board"]!["upsert"]![0]!["canSell"]);
    }

    [Fact]
    public void Zero_ammo_capacity_omits_ammo_fields()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var projection = projector.Bootstrap(
            Snapshot(
                1,
                new[]
                {
                    Card(
                        "one",
                        BazaarAgentCardLocation.Board,
                        "Socket_0",
                        ammo: null,
                        ammoMax: null
                    ),
                },
                Array.Empty<BazaarAgentCardSnapshot>()
            )
        );

        var card = Assert.Single(projection.View.Board!.Upsert!);
        Assert.Null(card.Ammo);
        Assert.Null(card.AmmoMax);
        var json = JObject.Parse(JsonConvert.SerializeObject(projection.View));
        Assert.Null(json["board"]!["upsert"]![0]!["ammo"]);
        Assert.Null(json["board"]!["upsert"]![0]!["ammoMax"]);
    }

    [Fact]
    public void Clearing_a_card_field_replaces_the_cached_card()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                new[]
                {
                    Card(
                        "one",
                        BazaarAgentCardLocation.Board,
                        "Socket_0",
                        description: "Deal damage."
                    ),
                },
                Array.Empty<BazaarAgentCardSnapshot>()
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    new[]
                    {
                        Card("one", BazaarAgentCardLocation.Board, "Socket_0", description: null),
                    },
                    Array.Empty<BazaarAgentCardSnapshot>()
                ),
                first.SessionId,
                1,
                out var changed
            )
        );

        var card = Assert.Single(changed.View.Board!.Upsert!);
        Assert.True(card.IsFull);
        Assert.Null(card.Description);
    }

    [Fact]
    public void Clearing_the_battle_summary_is_explicit_in_a_delta()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                new BazaarAgentBattleSummary { SummaryId = "battle" }
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    Array.Empty<BazaarAgentCardSnapshot>()
                ),
                first.SessionId,
                1,
                out var changed
            )
        );

        Assert.True(changed.View.BattleCleared);
    }

    [Fact]
    public void Delta_tolerates_a_card_in_both_an_owned_section_and_outgoing_selection()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var owned = Card("moving-item", BazaarAgentCardLocation.Chest, "Socket_0");
        var outgoingOffer = Card("moving-item", BazaarAgentCardLocation.Selection, "");
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                new[] { owned },
                selection: new[] { outgoingOffer }
            )
        );

        var exception = Record.Exception(() =>
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    new[] { owned },
                    selection: new[] { outgoingOffer }
                ),
                first.SessionId,
                1,
                out _
            )
        );

        Assert.Null(exception);
    }

    private static BazaarAgentContextSnapshot Snapshot(
        ulong revision,
        IReadOnlyList<BazaarAgentCardSnapshot> board,
        IReadOnlyList<BazaarAgentCardSnapshot> chest,
        BazaarAgentBattleSummary? battle = null,
        IReadOnlyList<BazaarAgentCardSnapshot>? selection = null
    ) =>
        new(
            new BazaarAgentContext
            {
                TickId = revision,
                StateName = BazaarAgentRunStateName.Choice,
                PlayerGold = 10,
                BoardItems = board,
                ChestItems = chest,
                SelectionOptions = selection ?? Array.Empty<BazaarAgentCardSnapshot>(),
                LastBattle = battle,
            }
        );

    private static BazaarAgentCardSnapshot Card(
        string instanceId,
        BazaarAgentCardLocation location,
        string socket,
        string? description = "Deal 10 damage.",
        int? ammo = 0,
        int? ammoMax = 3
    ) =>
        new()
        {
            InstanceId = instanceId,
            Kind = BazaarAgentCardKind.Item,
            TemplateId = "real-template",
            DisplayName = "Test Item",
            Size = "Medium",
            Tier = "Bronze",
            Location = location,
            SocketId = socket,
            Description = description,
            CooldownSeconds = 6,
            Ammo = ammo,
            AmmoMax = ammoMax,
        };
}
