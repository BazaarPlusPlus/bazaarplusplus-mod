#nullable enable
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public sealed class BazaarAgentAgentViewProjectorTests
{
    [Fact]
    public void Layout_validator_rejects_a_full_cross_inventory_drag_when_displaced_size_cannot_return()
    {
        BazaarAgentCardSnapshot CardAt(
            string id,
            int size,
            BazaarAgentCardLocation location,
            int socket
        ) =>
            new()
            {
                InstanceId = id,
                Kind = BazaarAgentCardKind.Item,
                Size = size.ToString(),
                Location = location,
                SocketId = "Socket_" + socket,
                TemplateId = id,
            };
        var snapshot = new BazaarAgentContextSnapshot(
            new BazaarAgentContext
            {
                BoardItems = new[]
                {
                    CardAt("b3a", 3, BazaarAgentCardLocation.Board, 0),
                    CardAt("b3b", 3, BazaarAgentCardLocation.Board, 3),
                    CardAt("b3c", 3, BazaarAgentCardLocation.Board, 6),
                    CardAt("b1", 1, BazaarAgentCardLocation.Board, 9),
                },
                ChestItems = new[]
                {
                    CardAt("c2a", 2, BazaarAgentCardLocation.Chest, 0),
                    CardAt("c2b", 2, BazaarAgentCardLocation.Chest, 2),
                    CardAt("c2c", 2, BazaarAgentCardLocation.Chest, 4),
                    CardAt("c2d", 2, BazaarAgentCardLocation.Chest, 6),
                    CardAt("c2e", 2, BazaarAgentCardLocation.Chest, 8),
                },
            }
        );

        var result = BazaarAgentLayoutMoveValidator.Validate(
            snapshot,
            new BazaarAgentAction
            {
                ActionKind = BazaarAgentActionKind.MoveItem,
                CardInstanceId = "b3a",
                TargetSection = BazaarAgentTargetSection.Stash,
                TargetSockets = new[] { "Socket_0", "Socket_1", "Socket_2" },
            }
        );

        Assert.NotEqual(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Layout_validator_rejects_a_locked_target_socket()
    {
        var snapshot = new BazaarAgentContextSnapshot(
            new BazaarAgentContext
            {
                BoardItems = new[]
                {
                    new BazaarAgentCardSnapshot
                    {
                        InstanceId = "item-1",
                        Size = "Small",
                        Location = BazaarAgentCardLocation.Board,
                        SocketId = "Socket_0",
                    },
                },
                LockedChestSockets = new[] { "Socket_3" },
            }
        );

        var result = BazaarAgentLayoutMoveValidator.Validate(
            snapshot,
            new BazaarAgentAction
            {
                ActionKind = BazaarAgentActionKind.MoveItem,
                CardInstanceId = "item-1",
                TargetSection = BazaarAgentTargetSection.Stash,
                TargetSockets = new[] { "Socket_3" },
            }
        );

        Assert.NotEqual(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void First_occurrence_in_a_session_includes_knowledge_but_a_move_does_not_repeat_it()
    {
        var projector = new BazaarAgentAgentViewProjector();
        var original = Card(attributes: new Dictionary<string, int> { ["Damage"] = 10 });
        var first = projector.Project(Snapshot(original), "agent-a");

        var moved = Card(
            location: BazaarAgentCardLocation.Chest,
            socketId: "Socket_4",
            order: 4,
            attributes: new Dictionary<string, int> { ["Damage"] = 10 }
        );
        var second = projector.Project(Snapshot(moved), "agent-a");

        var firstKnowledge = Assert.Single(first.View.CardKnowledge);
        Assert.Equal(firstKnowledge.KnowledgeId, Assert.Single(first.View.BoardItems).KnowledgeId);
        Assert.Empty(second.View.CardKnowledge);
        var movedRef = Assert.Single(second.View.BoardItems);
        Assert.Equal(firstKnowledge.KnowledgeId, movedRef.KnowledgeId);
        Assert.Equal(BazaarAgentCardLocation.Chest, movedRef.Location);
        Assert.Equal("Socket_4", movedRef.SocketId);
    }

    [Fact]
    public void Agent_view_carries_the_completed_battle_summary_without_card_knowledge_churn()
    {
        var projector = new BazaarAgentAgentViewProjector();
        var view = projector
            .Project(
                new BazaarAgentContextSnapshot(
                    new BazaarAgentContext
                    {
                        StateName = BazaarAgentRunStateName.Choice,
                        LockedBoardSockets = new[] { "Socket_8" },
                        LastBattle = new BazaarAgentBattleSummary
                        {
                            BattleType = "pve",
                            Result = "loss",
                            Opponent = new BazaarAgentBattleCombatant
                            {
                                Attributes = new BazaarAgentBattleAttributes
                                {
                                    Poison = new BazaarAgentBattleValueChange
                                    {
                                        Start = 2,
                                        End = 7,
                                    },
                                },
                            },
                        },
                    }
                ),
                "agent-a"
            )
            .View;

        Assert.Empty(view.CardKnowledge);
        Assert.Equal("pve", view.LastBattle?.BattleType);
        Assert.Equal("loss", view.LastBattle?.Result);
        Assert.Equal(5, view.LastBattle?.Opponent.Attributes.Poison?.Delta);
        Assert.Equal(new[] { "Socket_8" }, view.LockedBoardSockets);
    }

    [Fact]
    public void Semantic_change_emits_a_complete_replacement_with_a_new_knowledge_id()
    {
        var projector = new BazaarAgentAgentViewProjector();
        var first = projector.Project(
            Snapshot(Card(attributes: new Dictionary<string, int> { ["Damage"] = 10 })),
            "agent-a"
        );
        var second = projector.Project(
            Snapshot(Card(attributes: new Dictionary<string, int> { ["Damage"] = 14 })),
            "agent-a"
        );

        var originalKnowledge = Assert.Single(first.View.CardKnowledge);
        var changedKnowledge = Assert.Single(second.View.CardKnowledge);
        Assert.NotEqual(originalKnowledge.KnowledgeId, changedKnowledge.KnowledgeId);
        Assert.Equal(14, changedKnowledge.Attributes["Damage"]);
        Assert.Equal(
            changedKnowledge.KnowledgeId,
            Assert.Single(second.View.BoardItems).KnowledgeId
        );
    }

    [Fact]
    public void Knowledge_cache_is_scoped_to_agent_session_and_can_be_reset()
    {
        var projector = new BazaarAgentAgentViewProjector();
        var snapshot = Snapshot(Card());

        var first = projector.Project(snapshot, "agent-a");
        var repeated = projector.Project(snapshot, "agent-a");
        var otherAgent = projector.Project(snapshot, "agent-b");
        var reset = projector.Project(snapshot, "agent-a", resetKnowledge: true);

        Assert.Single(first.View.CardKnowledge);
        Assert.Empty(repeated.View.CardKnowledge);
        Assert.Single(otherAgent.View.CardKnowledge);
        Assert.Single(reset.View.CardKnowledge);
        Assert.NotEqual(first.CacheEpoch, reset.CacheEpoch);
    }

    [Fact]
    public void Zero_attributes_do_not_create_a_semantic_revision()
    {
        var projector = new BazaarAgentAgentViewProjector();
        var withoutZero = projector.Project(
            Snapshot(Card(attributes: new Dictionary<string, int> { ["Damage"] = 10 })),
            "agent-a"
        );
        var withZero = projector.Project(
            Snapshot(
                Card(attributes: new Dictionary<string, int> { ["Damage"] = 10, ["Burn"] = 0 })
            ),
            "agent-a"
        );

        Assert.Empty(withZero.View.CardKnowledge);
        Assert.Equal(
            Assert.Single(withoutZero.View.BoardItems).KnowledgeId,
            Assert.Single(withZero.View.BoardItems).KnowledgeId
        );
    }

    [Fact]
    public void Current_cooldown_does_not_create_a_semantic_revision()
    {
        var projector = new BazaarAgentAgentViewProjector();
        var ready = projector.Project(
            Snapshot(
                Card(attributes: new Dictionary<string, int> { ["Damage"] = 10, ["Cooldown"] = 0 })
            ),
            "agent-a"
        );
        var coolingDown = projector.Project(
            Snapshot(
                Card(attributes: new Dictionary<string, int> { ["Damage"] = 10, ["Cooldown"] = 7 })
            ),
            "agent-a"
        );

        Assert.Empty(coolingDown.View.CardKnowledge);
        Assert.Equal(
            Assert.Single(ready.View.BoardItems).KnowledgeId,
            Assert.Single(coolingDown.View.BoardItems).KnowledgeId
        );
    }

    [Fact]
    public void Explicit_batch_query_returns_full_knowledge_and_marks_it_seen_for_context()
    {
        var projector = new BazaarAgentAgentViewProjector();
        var snapshot = Snapshot(Card());

        var query = projector.Query(snapshot, "agent-a", new[] { "item-1", "missing-item" });
        var found = query.Response.Results[0];
        var missing = query.Response.Results[1];

        Assert.True(found.Found);
        Assert.NotNull(found.Card);
        Assert.False(missing.Found);
        Assert.Equal("not_found", missing.Error);
        var knowledge = Assert.Single(query.Response.CardKnowledge);
        Assert.Equal(knowledge.KnowledgeId, found.Card!.KnowledgeId);
        Assert.Empty(projector.Project(snapshot, "agent-a").View.CardKnowledge);
    }

    private static BazaarAgentContextSnapshot Snapshot(BazaarAgentCardSnapshot card) =>
        new(
            new BazaarAgentContext
            {
                TickId = 1,
                StateName = BazaarAgentRunStateName.Choice,
                BoardItems = new[] { card },
                AvailableActions = new[]
                {
                    new BazaarAgentDecisionOption
                    {
                        ActionKind = BazaarAgentActionKind.SellItem,
                        Group = BazaarAgentActionGroup.Sell,
                        DisplayKey = "SellItem:item-1",
                        CardInstanceId = card.InstanceId,
                        Card = card,
                    },
                },
            }
        );

    private static BazaarAgentCardSnapshot Card(
        BazaarAgentCardLocation location = BazaarAgentCardLocation.Board,
        string socketId = "Socket_0",
        int order = 0,
        IReadOnlyDictionary<string, int>? attributes = null
    ) =>
        new()
        {
            InstanceId = "item-1",
            Kind = BazaarAgentCardKind.Item,
            Type = "Item",
            TemplateId = "template-1",
            DisplayName = "Test Item",
            Tier = "Bronze",
            Size = "2",
            Enchantment = "None",
            Location = location,
            SocketId = socketId,
            Order = order,
            Tags = new[] { "Weapon" },
            Attributes = attributes ?? new Dictionary<string, int> { ["Damage"] = 10 },
            ActiveAbilities = new[]
            {
                new BazaarAgentCardAbilitySnapshot { Id = "ability-1", Action = "DealDamage" },
            },
            CanSell = true,
        };
}
