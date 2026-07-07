using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Game;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Spawning.SpawnFilters;
using BazaarGameShared.Domain.Spawning.SpawnGroups;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Localization;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class CollectionLevelUpTooltipTextTests
{
    public CollectionLevelUpTooltipTextTests()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
    }

    [Fact]
    public void Build_renders_health_pools_and_hero_filtered_groups()
    {
        var poolIds = Enumerable
            .Range(0, 25)
            .Select(i => Guid.Parse($"20000000-0000-0000-0000-{i:d12}"))
            .ToList();
        var levelUp = new TLevelUp
        {
            Level = 2,
            HealthIncrease = 150,
            Rewards = new TSpawnContextQuery
            {
                Groups =
                {
                    // Hero-excluded group (current hero Vanessa is not Dooley/Jules).
                    Group(
                        new[] { Guid.Parse("30000000-0000-0000-0000-000000000001") },
                        limit: 1,
                        new TPrerequisiteRun
                        {
                            Conditions = new TRunConditionalPlayerHero
                            {
                                Heroes = { EHero.Dooley, EHero.Jules },
                                Operator = EListComparisonOperator.None,
                            },
                        }
                    ),
                    // Hero-mismatched group (requires Dooley/Jules).
                    Group(
                        new[] { Guid.Parse("30000000-0000-0000-0000-000000000002") },
                        limit: 1,
                        new TPrerequisiteRun
                        {
                            Conditions = new TRunConditionalPlayerHero
                            {
                                Heroes = { EHero.Dooley, EHero.Jules },
                                Operator = EListComparisonOperator.Any,
                            },
                        }
                    ),
                    // Random pool.
                    Group(poolIds, limit: 3),
                    // Board-conditional bonus -> omitted.
                    Group(
                        new[] { Guid.Parse("30000000-0000-0000-0000-000000000003") },
                        limit: 1,
                        new TPrerequisiteCardCount()
                    ),
                },
            },
        };

        var text = CollectionLevelUpTooltipText.Build(
            levelUp,
            resolveTemplate: _ => null,
            currentHero: EHero.Vanessa,
            colorizeResult: result => $"«{result}»",
            currentLevel: 2
        );

        Assert.Contains("«+150 Max Health»", text);
        Assert.Contains("«+2 board slots»", text);
        Assert.Contains("3× random reward (25 options)", text);
        // Single-id groups with unresolvable templates and filtered groups add nothing.
        Assert.DoesNotContain("30000000", text);
    }

    [Fact]
    public void Build_returns_empty_for_null_level_up()
    {
        Assert.Equal(
            string.Empty,
            CollectionLevelUpTooltipText.Build(null, _ => null, EHero.Vanessa)
        );
    }

    [Fact]
    public void Build_keeps_hero_gated_groups_when_hero_is_unknown()
    {
        var poolIds = Enumerable
            .Range(0, 10)
            .Select(i => Guid.Parse($"40000000-0000-0000-0000-{i:d12}"))
            .ToList();
        var levelUp = new TLevelUp
        {
            Level = 3,
            Rewards = new TSpawnContextQuery
            {
                Groups =
                {
                    Group(
                        poolIds,
                        limit: 1,
                        new TPrerequisiteRun
                        {
                            Conditions = new TRunConditionalPlayerHero
                            {
                                Heroes = { EHero.Dooley },
                                Operator = EListComparisonOperator.Any,
                            },
                        }
                    ),
                },
            },
        };

        var text = CollectionLevelUpTooltipText.Build(levelUp, _ => null, currentHero: null);

        Assert.Contains("1× random reward (10 options)", text);
    }

    private static TSpawnGroup Group(
        IEnumerable<Guid> ids,
        int limit,
        params BazaarGameShared.Domain.Prerequisites.ITPrerequisite[] prerequisites
    ) =>
        new()
        {
            Filters = { new TSpawnFilterIdList { Ids = ids.ToList() } },
            Limit = new TFixedValue { Value = limit },
            Prerequisites = prerequisites.Length == 0 ? null : prerequisites.ToList(),
        };

    private sealed class TestLanguageProvider : ILanguageProvider
    {
        public string CurrentLanguageCode => "en";
    }

    private sealed class TestLocaleModeProvider : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}
