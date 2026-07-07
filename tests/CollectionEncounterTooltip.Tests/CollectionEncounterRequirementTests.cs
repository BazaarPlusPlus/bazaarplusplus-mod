using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.CollectionPanel;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public class CollectionEncounterRequirementTests
{
    private static readonly Guid PackageId = Guid.Parse("b0000000-0000-0000-0000-000000000001");

    // Farai's delivery outcomes: the group only rolls while you do NOT own the
    // package ("Equal 0") — the exact inverse of a plain has-check.
    [Fact]
    public void Equal_zero_requirement_is_met_only_without_the_card()
    {
        var requirement = new CollectionEncounterCardRequirement(
            new[] { PackageId },
            Array.Empty<IReadOnlyList<string>>(),
            tagOperator: "Any",
            comparison: "Equal",
            amount: 0
        );

        Assert.True(requirement.Matches(Inventory()));
        Assert.False(requirement.Matches(Inventory(Card(PackageId))));
    }

    [Fact]
    public void Count_threshold_requirements_count_duplicates()
    {
        var requirement = new CollectionEncounterCardRequirement(
            new[] { PackageId },
            Array.Empty<IReadOnlyList<string>>(),
            tagOperator: "Any",
            comparison: "GreaterThanOrEqual",
            amount: 2
        );

        Assert.False(requirement.Matches(Inventory(Card(PackageId))));
        Assert.True(requirement.Matches(Inventory(Card(PackageId), Card(PackageId))));
    }

    [Fact]
    public void Tag_requirement_with_equal_zero_inverts_on_hidden_tag_ownership()
    {
        var requirement = new CollectionEncounterCardRequirement(
            Array.Empty<Guid>(),
            new IReadOnlyList<string>[] { new[] { "Package" } },
            tagOperator: "Any",
            comparison: "Equal",
            amount: 0
        );

        Assert.True(requirement.Matches(Inventory(Card(Guid.NewGuid(), "Food"))));
        Assert.False(requirement.Matches(Inventory(Card(Guid.NewGuid(), "Package"))));
    }

    // Treasure Chest: weight-1 fixed id + weight-9 TSpawnFilterQuery pool. Dropping
    // the query group used to show the fixed chest as 100%.
    [Fact]
    public void Outcome_groups_keep_query_pools_in_the_roll()
    {
        var json =
            """
            {
              "$type": "TCardEncounterEvent",
              "SelectionContext": {
                "SpawnContext": {
                  "SelectionMethod": "Random",
                  "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                  "Groups": [
                    {
                      "RandomWeight": 1,
                      "Filters": [
                        { "$type": "TSpawnFilterIdList", "Ids": ["10000000-0000-0000-0000-000000000001"] }
                      ]
                    },
                    {
                      "RandomWeight": 9,
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                      "Filters": [
                        {
                          "$type": "TSpawnFilterQuery",
                          "Constraints": {
                            "$type": "ConstraintAnd",
                            "Constraints": [
                              { "$type": "ConstraintSize", "Sizes": ["Small", "Medium"], "IsNot": false },
                              { "$type": "ConstraintCardType", "Types": ["Item"], "IsNot": false },
                              { "$type": "ConstraintTag", "Tags": ["Loot"], "IsNot": true }
                            ]
                          }
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """;

        Assert.True(
            CollectionEncounterStructuredParser.TryParseEventOutcomeGroups(json, out var groups)
        );

        Assert.Equal(2, groups.Count);
        Assert.Equal(1u, groups[0].Weight);
        Assert.Empty(groups[0].QueryPools);
        Assert.Equal(9u, groups[1].Weight);
        Assert.Empty(groups[1].Ids);
        var pool = Assert.Single(groups[1].QueryPools);
        Assert.NotNull(pool.Filter);
    }

    // Runtime templates lose $type and serialize enum tags numerically; a numeric
    // Comparison must still parse ("2" = GreaterThanOrEqual is positional — verify
    // via names to stay robust against enum reordering).
    [Fact]
    public void Requirements_parse_from_prerequisites_without_type_markers()
    {
        var json =
            """
            [
              {
                "Subject": {
                  "Conditions": { "Id": "b0000000-0000-0000-0000-000000000001", "IsNot": false }
                },
                "Comparison": "Equal",
                "Amount": 0
              }
            ]
            """;

        var requirements = CollectionEncounterStructuredParser.ReadCardRequirements(
            Newtonsoft.Json.Linq.JToken.Parse(json)
        );

        var requirement = Assert.Single(requirements);
        Assert.Equal(new[] { PackageId }, requirement.Ids);
        Assert.Equal("Equal", requirement.Comparison);
        Assert.Equal(0, requirement.Amount);
    }

    [Fact]
    public void Negated_and_tier_conditionals_yield_no_requirement()
    {
        var json =
            """
            [
              {
                "Subject": {
                  "Conditions": { "Id": "b0000000-0000-0000-0000-000000000001", "IsNot": true }
                },
                "Comparison": "Equal",
                "Amount": 0
              },
              {
                "Subject": {
                  "Conditions": { "$type": "TCardConditionalTier", "Tiers": ["Bronze"], "IsNot": false }
                },
                "Comparison": "Equal",
                "Amount": 0
              }
            ]
            """;

        Assert.Empty(
            CollectionEncounterStructuredParser.ReadCardRequirements(
                Newtonsoft.Json.Linq.JToken.Parse(json)
            )
        );
    }

    private static CollectionEncounterInventory Inventory(
        params CollectionEncounterInventoryCard[] cards
    ) => new(cards.ToList());

    private static CollectionEncounterInventoryCard Card(Guid templateId, params string[] tags) =>
        new(templateId, new HashSet<string>(tags, StringComparer.Ordinal));
}
