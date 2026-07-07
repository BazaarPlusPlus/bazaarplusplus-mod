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

        var text = CollectionEncounterGameTooltipText.Build(option, result => $"«{result}»");

        Assert.DoesNotContain("You find a strange mushroom in the Greenheart", text);
        Assert.Contains("<color=#FFD37E>Keep It for Luck:</color> «Gain 1 XP»", text);
        Assert.Contains(
            "<color=#FFD37E>Share It With a Friend:</color> «Get a Small Silver-tier Friend»",
            text
        );
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
            "<size=85%><color=#8F8268>Add It to Your Bushel: (if you have a Bushel) Your Bushel gains 20 Heal</color></size>",
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
        Assert.Contains("- A Routine Job: Gain 2 XP", text);
        // Embedded newlines flatten so sub-entries stay one line each.
        Assert.Contains("- Generous Tip: Gain 10 Gold and 1 XP", text);
        Assert.Contains("Fight a monster (10 possible)", text);
        // Prerequisite-unmet groups render dimmed without a percentage.
        Assert.Contains(
            "<size=85%><color=#8F8268>· Clear the Way: (if you have Powder Keg) Gain 5 Gold (requires cards you don't own)</color></size>",
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
        string summary
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
            Array.Empty<EHiddenTag>()
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
        public string CurrentLanguageCode => "en";
    }

    private sealed class TestLocaleModeProvider : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}
