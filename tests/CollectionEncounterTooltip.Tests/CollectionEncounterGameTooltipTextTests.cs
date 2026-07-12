using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Localization;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class CollectionEncounterGameTooltipTextTests
{
    public CollectionEncounterGameTooltipTextTests()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
    }

    [Fact]
    public void Build_renders_one_compact_line_per_choice_without_meta_brackets()
    {
        var option = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000002"),
                "Keep It for Luck",
                "Gain 1 XP",
                rewardFilter: null,
                isSourceMatch: false
            ),
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000003"),
                "Share It With a Friend",
                "Get a Small Silver-tier Friend",
                CreateRewardFilter(tiers: new[] { ETier.Silver }, summary: "Small Silver Friend"),
                isSourceMatch: false
            )
        );

        var text = CollectionEncounterGameTooltipText.Build(
            option,
            result => $"<line-height=1.6em>«{result}»</line-height>"
        );

        Assert.DoesNotContain("You find a strange mushroom in the Greenheart", text);
        Assert.Contains("<color=#FFD37E>Keep It for Luck:</color> «Gain 1 XP»", text);
        Assert.Contains(
            "<color=#FFD37E>Share It With a Friend:</color> «Get a Small Silver-tier Friend»",
            text
        );
        Assert.DoesNotContain("<line-height=1.6em>«", text);
        // No dim meta brackets anymore — the game text carries the information.
        Assert.DoesNotContain("[", text);
    }

    [Fact]
    public void Build_appends_day_tier_only_for_day_driven_card_rewards()
    {
        var option = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000005"),
                "Open It",
                "Get a Medium item",
                CreateRewardFilter(tiers: Array.Empty<ETier>(), summary: "Medium Item"),
                isSourceMatch: false
            ),
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000006"),
                "Hunt for Reagents",
                "Get a Silver-tier Reagent",
                CreateRewardFilter(tiers: new[] { ETier.Silver }, summary: "Silver Reagent"),
                isSourceMatch: false
            ),
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000007"),
                "Sell It",
                "Gain 4 Gold",
                rewardFilter: null,
                isSourceMatch: false
            )
        );

        var text = CollectionEncounterGameTooltipText.Build(
            option,
            colorizeResult: null,
            dayTierCeiling: ETier.Silver
        );

        Assert.Contains("Get a Medium item (up to Silver)", text);
        Assert.Contains("Get a Silver-tier Reagent", text);
        Assert.DoesNotContain("Get a Silver-tier Reagent (up to", text);
        Assert.DoesNotContain("Gain 4 Gold (up to", text);
    }

    [Fact]
    public void Build_does_not_append_day_tier_when_result_text_already_names_a_tier()
    {
        var option = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000005"),
                "Have a Late Night Treat",
                "Get 2 Diamond-tier Food",
                CreateRewardFilter(tiers: Array.Empty<ETier>(), summary: "Food"),
                isSourceMatch: false
            )
        );

        var text = CollectionEncounterGameTooltipText.Build(
            option,
            colorizeResult: null,
            dayTierCeiling: ETier.Gold
        );

        Assert.Contains("Get 2 Diamond-tier Food", text);
        Assert.DoesNotContain("Get 2 Diamond-tier Food (up to Gold)", text);
    }

    [Fact]
    public void Build_does_not_append_day_tier_when_localized_result_text_already_names_a_tier()
    {
        L.Install(new TestLanguageProvider("zh-CN"), new TestLocaleModeProvider());
        var option = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000005"),
                "深夜点心",
                "获得2个钻石级食物",
                CreateRewardFilter(tiers: Array.Empty<ETier>(), summary: "Food"),
                isSourceMatch: false
            )
        );

        var text = CollectionEncounterGameTooltipText.Build(
            option,
            colorizeResult: null,
            dayTierCeiling: ETier.Gold
        );

        Assert.Contains("获得2个钻石级食物", text);
        Assert.DoesNotContain("获得2个钻石级食物（最高黄金）", text);
    }

    // The result text follows the game language's script, not the BPP locale mode, so
    // the tier-descriptor guard must hold when the two scripts disagree.
    [Theory]
    [InlineData("获得2个钻石级食物", true)]
    [InlineData("獲得2個鑽石級食物", false)]
    public void Build_does_not_append_day_tier_when_game_script_differs_from_locale_mode(
        string resultText,
        bool traditionalOutputMode
    )
    {
        var mode = traditionalOutputMode
            ? BppChineseLocaleMode.Taiwan
            : BppChineseLocaleMode.Mainland;
        L.Install(new TestLanguageProvider("zh-CN"), new TestLocaleModeProvider(mode));
        var option = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000005"),
                "深夜点心",
                resultText,
                CreateRewardFilter(tiers: Array.Empty<ETier>(), summary: "Food"),
                isSourceMatch: false
            )
        );

        var text = CollectionEncounterGameTooltipText.Build(
            option,
            colorizeResult: null,
            dayTierCeiling: ETier.Gold
        );

        Assert.Contains(resultText, text);
        Assert.DoesNotContain("最高", text);
    }

    [Fact]
    public void Build_does_not_append_day_tier_when_reward_ignores_day_tier_table()
    {
        var option = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000006"),
                "Fight the Beast",
                "Get a Rage item",
                CreateRewardFilter(
                    tiers: Array.Empty<ETier>(),
                    summary: "Rage Item",
                    usesDayTierTable: false
                ),
                isSourceMatch: false
            )
        );

        var text = CollectionEncounterGameTooltipText.Build(
            option,
            colorizeResult: null,
            dayTierCeiling: ETier.Gold
        );

        Assert.Contains("Get a Rage item", text);
        Assert.DoesNotContain("Get a Rage item (up to Gold)", text);
    }

    [Fact]
    public void Build_shows_the_exact_tier_when_the_day_ceiling_leaves_one()
    {
        // A Bronze-Diamond span (e.g. from a not-Legendary complement) is day-driven:
        // on day 1 the effective tier is exactly Bronze.
        var option = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000008"),
                "Trade It for Something",
                "Get a Medium item",
                CreateRewardFilter(
                    tiers: new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond },
                    summary: "Medium Bronze-Diamond Item"
                ),
                isSourceMatch: false
            )
        );

        var text = CollectionEncounterGameTooltipText.Build(
            option,
            colorizeResult: null,
            dayTierCeiling: ETier.Bronze
        );

        Assert.Contains("Get a Medium item (Bronze)", text);
    }

    [Fact]
    public void Build_renders_ineligible_options_as_flat_dimmed_lines()
    {
        var option = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-000000000009"),
                "Sell It",
                "Gain 4 Gold",
                rewardFilter: null,
                isSourceMatch: false
            ),
            new CollectionEncounterChoiceDetail(
                Guid.Parse("10000000-0000-0000-0000-00000000000a"),
                "Add It to Your Bushel",
                "(if you have a Bushel) Your Bushel gains 20 Heal",
                rewardFilter: null,
                isSourceMatch: false,
                prerequisiteSummary: "",
                isEligible: false
            )
        );

        var text = CollectionEncounterGameTooltipText.Build(option, result => $"«{result}»");

        Assert.Contains(
            "<size=85%><line-height=1.3em><color=#8F8268>Add It to Your Bushel: (if you have a Bushel) Your Bushel gains 20 Heal</color></size>",
            text
        );
        // No accent color and no keyword colorizer on the dimmed line.
        Assert.DoesNotContain("<color=#FFD37E>Add It to Your Bushel", text);
        Assert.DoesNotContain("«(if you have a Bushel)", text);
    }

    [Fact]
    public void Build_renders_outcome_groups_with_percentages()
    {
        var option = new CollectionEncounterOption(
            Guid.Parse("10000000-0000-0000-0000-00000000000b"),
            "Mountain Pass",
            sourceKey: null,
            sourceKind: null,
            Guid.Parse("10000000-0000-0000-0000-00000000000b"),
            "Aid a caravan descending the Great Plateau",
            rewardFilter: null,
            choiceDetails: null,
            outcomeGroups: new[]
            {
                new CollectionEncounterOutcomeView(
                    percent: 40,
                    isEligible: true,
                    isCombatPool: false,
                    optionCount: 2,
                    new[]
                    {
                        new CollectionEncounterChoiceDetail(
                            Guid.Parse("10000000-0000-0000-0000-00000000000c"),
                            "A Routine Job",
                            "Gain 2 XP",
                            rewardFilter: null,
                            isSourceMatch: false
                        ),
                        new CollectionEncounterChoiceDetail(
                            Guid.Parse("10000000-0000-0000-0000-00000000000d"),
                            "Generous Tip",
                            "Gain 10 Gold\nand 1 XP",
                            rewardFilter: null,
                            isSourceMatch: false
                        ),
                    }
                ),
                new CollectionEncounterOutcomeView(
                    percent: 50,
                    isEligible: true,
                    isCombatPool: true,
                    optionCount: 10,
                    Array.Empty<CollectionEncounterChoiceDetail>()
                ),
                new CollectionEncounterOutcomeView(
                    percent: null,
                    isEligible: false,
                    isCombatPool: false,
                    optionCount: 1,
                    new[]
                    {
                        new CollectionEncounterChoiceDetail(
                            Guid.Parse("10000000-0000-0000-0000-00000000000e"),
                            "Clear the Way",
                            "(if you have Powder Keg) Gain 5 Gold",
                            rewardFilter: null,
                            isSourceMatch: false
                        ),
                    }
                ),
            }
        );

        var text = CollectionEncounterGameTooltipText.Build(option);

        Assert.Contains("Possible outcomes:", text);
        Assert.Contains("<color=#FFD37E>40%</color>", text);
        // Sub-entries use a hanging indent so soft-wrapped lines stay aligned.
        Assert.Contains("<indent=2.2em>- A Routine Job: Gain 2 XP</indent>", text);
        // Embedded newlines flatten so sub-entries stay one line each.
        Assert.Contains("<indent=2.2em>- Generous Tip: Gain 10 Gold and 1 XP</indent>", text);
        Assert.Contains("Fight a monster (10 possible)", text);
        // Prerequisite-unmet groups render dimmed without a percentage; their own
        // text already carries the condition.
        Assert.Contains(
            "<size=85%><line-height=1.3em><color=#8F8268>· <indent=1em>Clear the Way: (if you have Powder Keg) Gain 5 Gold</indent></color></size>",
            text
        );
    }

    [Fact]
    public void Build_returns_empty_without_choice_details()
    {
        var option = new CollectionEncounterOption(
            Guid.Parse("10000000-0000-0000-0000-000000000004"),
            "Jungle Ruins",
            sourceKey: null,
            sourceKind: null,
            Guid.Parse("10000000-0000-0000-0000-000000000004"),
            "You find abandoned ruins in the jungle",
            rewardFilter: null
        );

        Assert.Equal(string.Empty, CollectionEncounterGameTooltipText.Build(option));
    }

    private static CollectionEncounterRewardFilter CreateRewardFilter(
        ETier[] tiers,
        string summary,
        bool usesDayTierTable = true
    ) =>
        new(
            ECardType.Item,
            quantity: 1,
            fromAnyHero: false,
            Array.Empty<ECardSize>(),
            tiers,
            Array.Empty<ECardTag>(),
            Array.Empty<EHiddenTag>(),
            summary,
            Array.Empty<ECardTag>(),
            Array.Empty<EHiddenTag>(),
            usesDayTierTable
        );

    private static CollectionEncounterOption CreateOption(
        params CollectionEncounterChoiceDetail[] choices
    ) =>
        new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "A Strange Mushroom",
            sourceKey: null,
            sourceKind: null,
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "You find a strange mushroom in the Greenheart",
            rewardFilter: null,
            choices
        );

    private sealed class TestLanguageProvider : ILanguageProvider
    {
        public TestLanguageProvider(string languageCode = "en")
        {
            CurrentLanguageCode = languageCode;
        }

        public string CurrentLanguageCode { get; }
    }

    private sealed class TestLocaleModeProvider : ILocaleModeProvider
    {
        public TestLocaleModeProvider(BppChineseLocaleMode mode = BppChineseLocaleMode.Mainland)
        {
            CurrentMode = mode;
        }

        public BppChineseLocaleMode CurrentMode { get; }
    }

    [Fact]
    public void Choice_pool_renders_combat_summary()
    {
        var option = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Empty,
                displayName: string.Empty,
                resultText: string.Empty,
                rewardFilter: null,
                isSourceMatch: false,
                pool: new CollectionEncounterChoicePool(
                    isCombat: true,
                    optionCount: 14,
                    Array.Empty<CollectionEncounterChoiceDetail>()
                )
            )
        );

        var text = CollectionEncounterGameTooltipText.Build(option);

        Assert.Contains("Fight a monster (14 possible)", text);
    }

    [Fact]
    public void Choice_pool_expands_small_entry_lists_and_counts_large_ones()
    {
        var small = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Empty,
                displayName: string.Empty,
                resultText: string.Empty,
                rewardFilter: null,
                isSourceMatch: false,
                pool: new CollectionEncounterChoicePool(
                    isCombat: false,
                    optionCount: 2,
                    new[]
                    {
                        new CollectionEncounterChoiceDetail(
                            Guid.NewGuid(),
                            "Aquatic Training",
                            "Your leftmost item gains the Aquatic type",
                            rewardFilter: null,
                            isSourceMatch: false
                        ),
                        new CollectionEncounterChoiceDetail(
                            Guid.NewGuid(),
                            "Apparel Training",
                            "Your leftmost item gains the Apparel type",
                            rewardFilter: null,
                            isSourceMatch: false
                        ),
                    }
                )
            )
        );
        var smallText = CollectionEncounterGameTooltipText.Build(small);
        Assert.Contains("one of 2:", smallText);
        Assert.Contains("Aquatic Training", smallText);

        var large = CreateOption(
            new CollectionEncounterChoiceDetail(
                Guid.Empty,
                displayName: string.Empty,
                resultText: string.Empty,
                rewardFilter: null,
                isSourceMatch: false,
                pool: new CollectionEncounterChoicePool(
                    isCombat: false,
                    optionCount: 16,
                    Array.Empty<CollectionEncounterChoiceDetail>()
                )
            )
        );
        var largeText = CollectionEncounterGameTooltipText.Build(large);
        Assert.Contains("Random reward (16 options)", largeText);
        Assert.DoesNotContain("one of", largeText);
    }
}
