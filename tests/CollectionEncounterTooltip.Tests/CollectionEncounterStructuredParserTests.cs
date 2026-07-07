using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class CollectionEncounterStructuredParserTests
{
    [Fact]
    public void TryParseEventStepReferences_reads_step_ids_from_spawn_group_filters()
    {
        var brewId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var tradeId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var tinyFurryMonsterId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var json =
            """
            {
              "$type": "TCardEncounterEvent",
              "SelectionContext": {
                "SpawnContext": {
                  "$type": "TSpawnContextQuery",
                  "Groups": [
                    {
                      "Filters": [
                        {
                          "Ids": [
                            "10000000-0000-0000-0000-000000000001",
                            "10000000-0000-0000-0000-000000000002"
                          ],
                          "Prerequisites": {
                            "$type": "THasCardPrerequisite",
                            "Ids": ["20000000-0000-0000-0000-000000000001"]
                          }
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """;

        var references = CollectionEncounterStructuredParser.TryParseEventStepReferences(json);

        Assert.Equal(new[] { brewId, tradeId }, references.Select(reference => reference.TemplateId));
        Assert.Equal(new[] { tinyFurryMonsterId }, references[0].PrerequisiteTemplateIds);
        Assert.Equal(new[] { tinyFurryMonsterId }, references[1].PrerequisiteTemplateIds);
    }

    [Fact]
    public void TryParseEventStepReferences_combines_group_and_filter_prerequisite_ids()
    {
        var stepId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var groupPrerequisiteId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var filterPrerequisiteId = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var json =
            """
            {
              "$type": "TCardEncounterEvent",
              "SelectionContext": {
                "SpawnContext": {
                  "Groups": [
                    {
                      "Prerequisites": {
                        "$type": "THasCardPrerequisite",
                        "Ids": ["20000000-0000-0000-0000-000000000002"]
                      },
                      "Filters": [
                        {
                          "Ids": ["10000000-0000-0000-0000-000000000003"],
                          "Prerequisites": {
                            "$type": "THasCardPrerequisite",
                            "TemplateId": "20000000-0000-0000-0000-000000000003"
                          }
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """;

        var reference = Assert.Single(
            CollectionEncounterStructuredParser.TryParseEventStepReferences(json)
        );

        Assert.Equal(stepId, reference.TemplateId);
        Assert.Equal(
            new[] { groupPrerequisiteId, filterPrerequisiteId },
            reference.PrerequisiteTemplateIds
        );
    }

    [Fact]
    public void TryParseRewardFilter_reads_deal_card_constraints_from_action_spawn_context()
    {
        var json =
            """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                {
                                  "$type": "ConstraintCardType",
                                  "Types": ["Item"],
                                  "IsNot": false
                                },
                                {
                                  "$type": "ConstraintSize",
                                  "Sizes": ["Medium"],
                                  "IsNot": false
                                },
                                {
                                  "$type": "ConstraintTier",
                                  "Tiers": ["Bronze", "Silver", "Gold", "Diamond"],
                                  "IsNot": false
                                },
                                {
                                  "$type": "ConstraintTag",
                                  "Tags": ["Loot"],
                                  "IsNot": true
                                }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = CollectionEncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.Equal(new[] { ECardSize.Medium }, reward.Sizes);
        Assert.Equal(new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond }, reward.Tiers);
        Assert.Empty(reward.Tags);
        Assert.Equal(new[] { ECardTag.Loot }, reward.ExcludedTags);
        Assert.Equal("Medium Bronze-Diamond Item, not Loot", reward.FilterSummary);
    }

    [Fact]
    public void TryParseRewardFilter_reads_runtime_deal_card_action_without_json_type_metadata()
    {
        var step = new RuntimeEncounterStep
        {
            Abilities = new Dictionary<string, RuntimeAbility>
            {
                ["0"] = new()
                {
                    Action = new TActionGameDealCards
                    {
                        SpawnContext = new RuntimeSpawnContext
                        {
                            Limit = new RuntimeFixedValue { Value = 1.0 },
                            Groups = new[]
                            {
                                new RuntimeSpawnGroup
                                {
                                    Filters = new[]
                                    {
                                        new RuntimeSpawnFilter
                                        {
                                            Constraints = new object[]
                                            {
                                                new ConstraintCardType
                                                {
                                                    Types = new[] { "Skill" },
                                                },
                                                new ConstraintHiddenTag
                                                {
                                                    Tags = new[] { "Freeze" },
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var reward = CollectionEncounterStructuredParser.TryParseRewardFilter(step);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Skill, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.Equal(new[] { EHiddenTag.Freeze }, reward.Keywords);
    }

    [Fact]
    public void TryParseRewardFilter_reads_runtime_constraint_and_children()
    {
        var step = new RuntimeEncounterStep
        {
            Abilities = new Dictionary<string, RuntimeAbility>
            {
                ["0"] = new()
                {
                    Action = new TActionGameDealCards
                    {
                        SpawnContext = new RuntimeSpawnContext
                        {
                            Limit = new RuntimeFixedValue { Value = 1.0 },
                            Groups = new[]
                            {
                                new RuntimeSpawnGroup
                                {
                                    Filters = new[]
                                    {
                                        new RuntimeSpawnFilter
                                        {
                                            Constraints = new object[]
                                            {
                                                new ConstraintAnd
                                                {
                                                    Constraints = new object[]
                                                    {
                                                        new ConstraintCardType
                                                        {
                                                            Types = new[] { "Item" },
                                                        },
                                                        new ConstraintSize
                                                        {
                                                            Sizes = new[] { "Medium" },
                                                        },
                                                        new ConstraintTier
                                                        {
                                                            Tiers = new[]
                                                            {
                                                                "Bronze",
                                                                "Silver",
                                                                "Gold",
                                                                "Diamond",
                                                            },
                                                        },
                                                        new ConstraintTag
                                                        {
                                                            Tags = new[] { "Loot" },
                                                            IsNot = true,
                                                        },
                                                    },
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var reward = CollectionEncounterStructuredParser.TryParseRewardFilter(step);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.Equal(new[] { ECardSize.Medium }, reward.Sizes);
        Assert.Equal(new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond }, reward.Tiers);
        Assert.Equal(new[] { ECardTag.Loot }, reward.ExcludedTags);
        Assert.Equal("Medium Bronze-Diamond Item, not Loot", reward.FilterSummary);
    }

    [Fact]
    public void TryParseRewardFilter_maps_excluded_tier_and_size_to_included_complements()
    {
        var json =
            """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "Limit": { "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Item"] },
                                { "$type": "ConstraintTag", "Tags": ["Tool"] },
                                { "$type": "ConstraintTier", "Tiers": ["Legendary"], "IsNot": true },
                                { "$type": "ConstraintSize", "Sizes": ["Large"], "IsNot": true }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = CollectionEncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Equal(new[] { ECardSize.Small, ECardSize.Medium }, reward.Sizes);
        Assert.Equal(new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond }, reward.Tiers);
        Assert.Equal(new[] { ECardTag.Tool }, reward.Tags);
        Assert.Equal("Small Medium Bronze-Diamond Tool", reward.FilterSummary);
    }

    [Fact]
    public void TryParseRewardFilter_reads_real_constraint_is_not_as_exclusion()
    {
        var json =
            """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "Limit": { "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                {
                                  "$type": "ConstraintCardType",
                                  "Types": ["Item"],
                                  "IsNot": false
                                },
                                {
                                  "$type": "ConstraintSize",
                                  "Sizes": ["Medium"],
                                  "IsNot": false
                                },
                                {
                                  "$type": "ConstraintTier",
                                  "Tiers": ["Bronze", "Silver", "Gold", "Diamond"],
                                  "IsNot": false
                                },
                                {
                                  "$type": "ConstraintTag",
                                  "Tags": ["Loot"],
                                  "IsNot": true
                                }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = CollectionEncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Empty(reward.Tags);
        Assert.Equal(new[] { ECardTag.Loot }, reward.ExcludedTags);
        Assert.Equal("Medium Bronze-Diamond Item, not Loot", reward.FilterSummary);
    }

    [Fact]
    public void TryParseRewardFilter_merges_alternative_filters_of_the_same_card_type()
    {
        // Weighted alternatives (e.g. a 95%/5% split) of the same card type describe one
        // pool for display purposes and are unioned.
        var json =
            """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "Limit": { "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Item"] },
                                { "$type": "ConstraintTag", "Tags": ["Tool"] },
                                { "$type": "ConstraintSize", "Sizes": ["Small"] }
                              ]
                            },
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Item"] },
                                { "$type": "ConstraintTag", "Tags": ["Weapon"] },
                                { "$type": "ConstraintSize", "Sizes": ["Large"] }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = CollectionEncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward!.CardType);
        Assert.Equal(new[] { ECardSize.Small, ECardSize.Large }, reward.Sizes);
        Assert.Equal(new[] { ECardTag.Tool, ECardTag.Weapon }, reward.Tags);
    }

    [Fact]
    public void TryParseRewardFilter_returns_null_for_alternative_filters_of_different_card_types()
    {
        var json =
            """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "Limit": { "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Item"] },
                                { "$type": "ConstraintSize", "Sizes": ["Small"] }
                              ]
                            },
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Skill"] }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = CollectionEncounterStructuredParser.TryParseRewardFilter(json);

        Assert.Null(reward);
    }

    [Fact]
    public void TryParseRewardFilter_skips_unconstrained_deal_card_actions()
    {
        var json =
            """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 }
                    }
                  }
                },
                "1": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Types": ["Skill"],
                              "HiddenTags": ["Freeze"]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = CollectionEncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Skill, reward.CardType);
        Assert.Equal(new[] { EHiddenTag.Freeze }, reward.Keywords);
    }

    private sealed class RuntimeEncounterStep
    {
        public Dictionary<string, RuntimeAbility> Abilities { get; init; } = new();
    }

    private sealed class RuntimeAbility
    {
        public object? Action { get; init; }
    }

    private sealed class TActionGameDealCards
    {
        public RuntimeSpawnContext? SpawnContext { get; init; }
    }

    private sealed class RuntimeSpawnContext
    {
        public RuntimeFixedValue? Limit { get; init; }

        public RuntimeSpawnGroup[] Groups { get; init; } = Array.Empty<RuntimeSpawnGroup>();
    }

    private sealed class RuntimeFixedValue
    {
        public double Value { get; init; }
    }

    private sealed class RuntimeSpawnGroup
    {
        public RuntimeSpawnFilter[] Filters { get; init; } = Array.Empty<RuntimeSpawnFilter>();
    }

    private sealed class RuntimeSpawnFilter
    {
        public object[] Constraints { get; init; } = Array.Empty<object>();
    }

    private sealed class ConstraintCardType
    {
        public string[] Types { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    private sealed class ConstraintHiddenTag
    {
        public string[] Tags { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    private sealed class ConstraintAnd
    {
        public object[] Constraints { get; init; } = Array.Empty<object>();
    }

    private sealed class ConstraintSize
    {
        public string[] Sizes { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    private sealed class ConstraintTier
    {
        public string[] Tiers { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    private sealed class ConstraintTag
    {
        public string[] Tags { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    [Fact]
    public void TryParseEventStepReferences_reads_tag_prerequisite_groups()
    {
        var json =
            """
            {
              "$type": "TCardEncounterEvent",
              "SelectionContext": {
                "SpawnContext": {
                  "Limit": { "$type": "TFixedValue", "Value": 3.0 },
                  "Groups": [
                    {
                      "Filters": [{ "Ids": ["10000000-0000-0000-0000-000000000001"] }],
                      "Prerequisites": [
                        {
                          "$type": "TPrerequisiteCardCount",
                          "Subject": {
                            "$type": "TTargetCardSection",
                            "TargetSection": "AbsolutePlayerHandAndStash",
                            "Conditions": {
                              "$type": "TCardConditionalTag",
                              "Tags": ["Toy", "Drone"],
                              "Operator": "Any"
                            }
                          },
                          "Comparison": "GreaterThanOrEqual",
                          "Amount": 1
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """;

        var references = CollectionEncounterStructuredParser.TryParseEventStepReferences(json);

        var reference = Assert.Single(references);
        var tagGroup = Assert.Single(reference.PrerequisiteTagGroups);
        Assert.Equal(new[] { "Toy", "Drone" }, tagGroup);
        Assert.Equal(3, CollectionEncounterStructuredParser.TryParseEventChoiceLimit(json));
    }
}
