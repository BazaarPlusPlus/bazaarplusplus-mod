#nullable enable
using BazaarPlusPlus.BazaarAgent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class BazaarAgentProtocolV3ProjectorTests
{
    [Fact]
    public void Bootstrap_is_full_and_a_later_move_emits_only_its_reference_and_slots()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var board = Card("real-item", BazaarAgentCardLocation.Board, "Socket_2");
        var first = projector.Bootstrap(
            Snapshot(1, new[] { board }, Array.Empty<BazaarAgentCardSnapshot>())
        );

        var firstCard = Assert.Single(first.View.Board!.Upsert!);
        Assert.True(first.View.IsFull);
        Assert.Equal("Test Item#00001", firstCard.Item);
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
        Assert.Equal(new[] { "Test Item#00001" }, second.View.Board!.Remove);
        var movedCard = Assert.Single(second.View.Chest!.Upsert!);
        Assert.Equal("Test Item#00001", movedCard.Item);
        Assert.Equal(new[] { 0, 1 }, movedCard.Slots);
        Assert.Null(movedCard.Size);
        Assert.Null(movedCard.Tier);
        Assert.Null(movedCard.Description);
        Assert.Null(movedCard.CooldownSeconds);
        Assert.Null(movedCard.Name);
        var json = JObject.Parse(JsonConvert.SerializeObject(second.View));
        Assert.Equal(
            new[] { "item", "slots" },
            ((JObject)json["chest"]!["upsert"]![0]!).Properties().Select(property => property.Name)
        );
    }

    [Fact]
    public void Move_with_other_card_changes_emits_only_the_changed_fields()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                new[] { Card("real-item", BazaarAgentCardLocation.Board, "Socket_2") },
                Array.Empty<BazaarAgentCardSnapshot>()
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    new[]
                    {
                        Card(
                            "real-item",
                            BazaarAgentCardLocation.Chest,
                            "Socket_0",
                            description: "Deal 14 damage."
                        ),
                    }
                ),
                first.SessionId,
                1,
                out var second
            )
        );

        var movedCard = Assert.Single(second.View.Chest!.Upsert!);
        Assert.Equal("Test Item#00001", movedCard.Item);
        Assert.Equal(new[] { 0, 1 }, movedCard.Slots);
        Assert.Null(movedCard.Size);
        Assert.Null(movedCard.Tier);
        Assert.Equal("Deal 14 damage.", movedCard.Description);
        Assert.Null(movedCard.CooldownSeconds);
        var json = JObject.Parse(JsonConvert.SerializeObject(second.View));
        Assert.Equal(
            new[] { "item", "slots", "description" },
            ((JObject)json["chest"]!["upsert"]![0]!).Properties().Select(property => property.Name)
        );
    }

    [Fact]
    public void Selection_item_becoming_owned_emits_only_its_reference_and_slots()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var offered = Card("real-item", BazaarAgentCardLocation.Selection, "Socket_2");
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                selection: [offered]
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    [Card("real-item", BazaarAgentCardLocation.Board, "Socket_2")],
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    selection: Array.Empty<BazaarAgentCardSnapshot>()
                ),
                first.SessionId,
                1,
                out var second
            )
        );

        var owned = Assert.Single(second.View.Board!.Upsert!);
        Assert.Equal("Test Item#00001", owned.Item);
        Assert.Equal(new[] { 2, 3 }, owned.Slots);
        var json = JObject.Parse(JsonConvert.SerializeObject(second.View));
        Assert.Equal(
            new[] { "item", "slots" },
            ((JObject)json["board"]!["upsert"]![0]!).Properties().Select(property => property.Name)
        );
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
    public void Changed_item_delta_keeps_its_human_readable_identity()
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
        Assert.Equal("Test Item#00001", card.Item);
        Assert.Null(card.Name);
        Assert.Equal("Deal 14 damage.", card.Description);
        var json = JObject.Parse(JsonConvert.SerializeObject(changed.View));
        Assert.Null(json["board"]!["upsert"]![0]!["attributes"]);
        Assert.Null(json["board"]!["upsert"]![0]!["abilities"]);
        Assert.Null(json["board"]!["upsert"]![0]!["canSelect"]);
        Assert.Null(json["board"]!["upsert"]![0]!["canFit"]);
        Assert.Null(json["board"]!["upsert"]![0]!["free"]);
        Assert.Null(json["board"]!["upsert"]![0]!["canSell"]);
        Assert.Null(json["board"]!["upsert"]![0]!["template"]);
        Assert.Null(json["board"]!["upsert"]![0]!["id"]);
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

    [Fact]
    public void Selection_is_omitted_when_unchanged_and_replaced_when_changed()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                selection: new[] { Card("offer-one", BazaarAgentCardLocation.Selection, "") }
            )
        );

        var firstOffer = Assert.Single(first.View.Selection!);
        Assert.Equal("Test Item#00001", firstOffer.Item);

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    selection: new[] { Card("offer-one", BazaarAgentCardLocation.Selection, "") }
                ),
                first.SessionId,
                1,
                out var unchanged
            )
        );

        Assert.Null(unchanged.View.Selection);

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    3,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    selection: new[] { Card("offer-two", BazaarAgentCardLocation.Selection, "") }
                ),
                unchanged.SessionId,
                2,
                out var next
            )
        );

        var nextOffer = Assert.Single(next.View.Selection!);
        Assert.Equal("Test Item#00002", nextOffer.Item);
        Assert.Equal("Deal 10 damage.", nextOffer.Description);
    }

    [Fact]
    public void Selection_is_replaced_when_an_existing_option_changes()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                selection:
                [
                    Card(
                        "offer",
                        BazaarAgentCardLocation.Selection,
                        "",
                        description: "Deal 10 damage."
                    ),
                ]
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    selection:
                    [
                        Card(
                            "offer",
                            BazaarAgentCardLocation.Selection,
                            "",
                            description: "Deal 14 damage."
                        ),
                    ]
                ),
                first.SessionId,
                1,
                out var changed
            )
        );

        Assert.Equal("Deal 14 damage.", Assert.Single(changed.View.Selection!).Description);
    }

    [Fact]
    public void Selection_is_replaced_at_a_new_hour_even_when_its_options_are_unchanged()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                selection: [Card("offer", BazaarAgentCardLocation.Selection, "")],
                day: 2,
                hour: 1
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    selection: [Card("offer", BazaarAgentCardLocation.Selection, "")],
                    day: 2,
                    hour: 2
                ),
                first.SessionId,
                1,
                out var next
            )
        );

        Assert.Equal("Test Item#00001", Assert.Single(next.View.Selection!).Item);
    }

    [Fact]
    public void Selection_empty_array_explicitly_clears_a_previous_row()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                selection: [Card("offer", BazaarAgentCardLocation.Selection, "")]
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    selection: Array.Empty<BazaarAgentCardSnapshot>()
                ),
                first.SessionId,
                1,
                out var cleared
            )
        );

        Assert.Empty(cleared.View.Selection!);
    }

    [Fact]
    public void Owned_skill_uses_its_name_as_the_delta_key()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var projection = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                playerSkills:
                [
                    new BazaarAgentCardSnapshot
                    {
                        InstanceId = "unknown-opponent-skill",
                        Kind = BazaarAgentCardKind.Skill,
                        TemplateId = "ignored-template",
                        DisplayName = "Stable Skill",
                        Size = "Medium",
                        Location = BazaarAgentCardLocation.Skill,
                    },
                ]
            )
        );

        var skill = Assert.Single(projection.View.Skills!.Upsert!);
        Assert.Equal("Stable Skill", skill.Name);
        Assert.Null(skill.Skill);
        Assert.Null(skill.Size);
        Assert.Null(skill.Tags);

        var json = JObject.Parse(JsonConvert.SerializeObject(projection.View));
        var serializedSkill = json["skills"]!["upsert"]![0]!;
        Assert.Null(serializedSkill["template"]);
        Assert.Equal("Stable Skill", serializedSkill["name"]!.Value<string>());
        Assert.Null(serializedSkill["size"]);
        Assert.Null(serializedSkill["tags"]);
    }

    [Fact]
    public void Changed_owned_skill_delta_contains_only_its_key_and_changed_field()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                playerSkills: [Skill("skill", "Stable Skill")]
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    playerSkills:
                    [
                        new BazaarAgentCardSnapshot
                        {
                            InstanceId = "skill",
                            Kind = BazaarAgentCardKind.Skill,
                            DisplayName = "Stable Skill",
                            Tier = "Bronze",
                            Location = BazaarAgentCardLocation.Skill,
                            Description = "An improved skill.",
                        },
                    ]
                ),
                first.SessionId,
                1,
                out var second
            )
        );

        var changed = Assert.Single(second.View.Skills!.Upsert!);
        Assert.Equal("Stable Skill", changed.Name);
        Assert.Equal("An improved skill.", changed.Description);
        Assert.Null(changed.Tier);
        Assert.Null(changed.HiddenTags);
        var json = JObject.Parse(JsonConvert.SerializeObject(second.View));
        Assert.Equal(
            new[] { "name", "description" },
            ((JObject)json["skills"]!["upsert"]![0]!).Properties().Select(property => property.Name)
        );
    }

    [Fact]
    public void Skill_replacement_removes_the_old_name_and_upserts_the_new_name()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                playerSkills: [Skill("old-skill", "Old Skill")]
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    playerSkills: [Skill("new-skill", "New Skill")]
                ),
                first.SessionId,
                1,
                out var next
            )
        );

        Assert.Equal(["Old Skill"], next.View.Skills!.Remove);
        Assert.Equal("New Skill", Assert.Single(next.View.Skills.Upsert!).Name);
    }

    [Fact]
    public void Selecting_a_skill_emits_only_its_owned_skill_key()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var offeredSkill = Skill("happy-camper", "Happy Camper", BazaarAgentCardLocation.Selection);
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                selection: [offeredSkill]
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    selection: Array.Empty<BazaarAgentCardSnapshot>(),
                    playerSkills: [Skill("happy-camper", "Happy Camper")]
                ),
                first.SessionId,
                1,
                out var next
            )
        );

        var owned = Assert.Single(next.View.Skills!.Upsert!);
        Assert.Equal("Happy Camper", owned.Name);
        Assert.Null(owned.Tier);
        Assert.Null(owned.Description);
        var json = JObject.Parse(JsonConvert.SerializeObject(next.View));
        Assert.Equal(
            new[] { "name" },
            ((JObject)json["skills"]!["upsert"]![0]!).Properties().Select(property => property.Name)
        );
    }

    [Fact]
    public void Encounter_selection_keeps_only_reference_description_and_price()
    {
        var projector = new BazaarAgentProtocolV3Projector();
        var encounter = new BazaarAgentCardSnapshot
        {
            InstanceId = "watch-match",
            Kind = BazaarAgentCardKind.Encounter,
            TemplateId = "ignored-template",
            DisplayName = "Watch Match",
            Size = "Medium",
            Tier = "Bronze",
            Tags = ["Ignored"],
            HiddenTags = ["Ignored"],
            Description = "Watch the match.",
            BuyPrice = 5,
            Location = BazaarAgentCardLocation.Selection,
        };

        var projection = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                selection: [encounter]
            )
        );

        var option = Assert.Single(projection.View.Selection!);
        Assert.Equal("Watch Match#00001", option.Encounter);
        Assert.Equal("Watch the match.", option.Description);
        Assert.Equal(5, option.BuyPrice);
        var json = JObject.Parse(JsonConvert.SerializeObject(projection.View));
        var serialized = json["selection"]![0]!;
        Assert.Null(serialized["item"]);
        Assert.Null(serialized["name"]);
        Assert.Null(serialized["template"]);
        Assert.Null(serialized["size"]);
        Assert.Null(serialized["tier"]);
        Assert.Null(serialized["tags"]);
        Assert.Null(serialized["hiddenTags"]);
    }

    [Fact]
    public void Combat_encounter_selection_includes_the_native_opponent_lineup()
    {
        var templateId = Guid.NewGuid().ToString("D");
        var encounter = new BazaarAgentCardSnapshot
        {
            InstanceId = "monster-choice",
            Kind = BazaarAgentCardKind.Encounter,
            TemplateId = templateId,
            DisplayName = "Monster",
            Location = BazaarAgentCardLocation.Selection,
            OpponentPreview = new BazaarAgentCombatOpponentPreview
            {
                Board = [Card("monster-item", BazaarAgentCardLocation.Board, "Socket_4")],
                Skills = [Skill("monster-skill", "Monster Skill")],
                Health = 300,
                MaxHealth = 300,
            },
        };

        var projection = new BazaarAgentProtocolV3Projector().Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                selection: [encounter]
            )
        );

        var opponent = Assert.Single(projection.View.Selection!).Opponent;
        Assert.NotNull(opponent);
        Assert.Equal(300, opponent.Health);
        Assert.Equal(new[] { 4, 5 }, Assert.Single(opponent.Board).Slots);
        Assert.Equal("Monster Skill", Assert.Single(opponent.Skills).Name);
        var json = JObject.Parse(JsonConvert.SerializeObject(projection.View));
        var skill = json["selection"]![0]!["opponent"]!["skills"]![0]!;
        Assert.Null(skill["tags"]);
        Assert.Null(skill["skill"]);
    }

    [Fact]
    public void Pve_opening_is_omitted_after_its_opponent_preview_was_sent()
    {
        var templateId = Guid.NewGuid().ToString("D");
        var encounter = new BazaarAgentCardSnapshot
        {
            InstanceId = "monster-choice",
            Kind = BazaarAgentCardKind.Encounter,
            TemplateId = templateId,
            DisplayName = "Monster",
            Location = BazaarAgentCardLocation.Selection,
            OpponentPreview = new BazaarAgentCombatOpponentPreview(),
        };
        var projector = new BazaarAgentProtocolV3Projector();
        var first = projector.Bootstrap(
            Snapshot(
                1,
                Array.Empty<BazaarAgentCardSnapshot>(),
                Array.Empty<BazaarAgentCardSnapshot>(),
                selection: [encounter]
            )
        );

        Assert.True(
            projector.TryProjectDelta(
                Snapshot(
                    2,
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    Array.Empty<BazaarAgentCardSnapshot>(),
                    battle: new BazaarAgentBattleSummary { Phase = "starting", BattleType = "pve" },
                    stateName: BazaarAgentRunStateName.Combat,
                    currentEncounterId: templateId
                ),
                first.SessionId,
                1,
                out var opening
            )
        );

        Assert.Null(opening.View.LastBattle);
        Assert.Null(opening.View.BattleCleared);
    }

    [Fact]
    public void State_omits_internal_ids_and_free_selection_flag()
    {
        var projection = new BazaarAgentProtocolV3Projector().Bootstrap(
            new BazaarAgentContextSnapshot(
                new BazaarAgentContext
                {
                    TickId = 1,
                    StateName = BazaarAgentRunStateName.Encounter,
                    RunId = "run-id",
                    GameModeId = "mode-id",
                    CurrentEncounterId = "encounter-id",
                    CurrentEncounterType = null,
                    SelectionIsFree = true,
                    ReplayPhase = BazaarAgentReplayPhase.None,
                }
            )
        );

        var state = projection.View.State!;
        Assert.DoesNotContain("run", state.Keys);
        Assert.DoesNotContain("mode", state.Keys);
        Assert.DoesNotContain("encounter", state.Keys);
        Assert.DoesNotContain("encounterType", state.Keys);
        Assert.DoesNotContain("freeSelection", state.Keys);
        Assert.DoesNotContain("replay", state.Keys);
    }

    private static BazaarAgentContextSnapshot Snapshot(
        ulong revision,
        IReadOnlyList<BazaarAgentCardSnapshot> board,
        IReadOnlyList<BazaarAgentCardSnapshot> chest,
        BazaarAgentBattleSummary? battle = null,
        IReadOnlyList<BazaarAgentCardSnapshot>? selection = null,
        IReadOnlyList<BazaarAgentCardSnapshot>? playerSkills = null,
        BazaarAgentRunStateName stateName = BazaarAgentRunStateName.Choice,
        string? currentEncounterId = null,
        int? day = null,
        int? hour = null
    ) =>
        new(
            new BazaarAgentContext
            {
                TickId = revision,
                StateName = stateName,
                CurrentEncounterId = currentEncounterId,
                Day = day,
                Hour = hour,
                PlayerGold = 10,
                BoardItems = board,
                ChestItems = chest,
                PlayerSkills = playerSkills ?? Array.Empty<BazaarAgentCardSnapshot>(),
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

    private static BazaarAgentCardSnapshot Skill(
        string instanceId,
        string name,
        BazaarAgentCardLocation location = BazaarAgentCardLocation.Skill
    ) =>
        new()
        {
            InstanceId = instanceId,
            Kind = BazaarAgentCardKind.Skill,
            DisplayName = name,
            Tier = "Bronze",
            Location = location,
            Description = "A skill.",
        };
}
