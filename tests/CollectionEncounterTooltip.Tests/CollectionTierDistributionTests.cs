using BazaarGameShared.Domain.Cards.Encounter.Event;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Domain.Spawning.SpawnBehaviors;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Localization;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class TierDistributionTests
{
    public TierDistributionTests()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
    }

    [Theory]
    [InlineData(0.9f, 0.1f, 0f, 0f, "Bronze 90%", null, "Silver 10%")]
    [InlineData(0f, 0.95f, 0.05f, 0f, "Silver 95%", null, "Gold 5%")]
    [InlineData(0f, 0.45f, 0.5f, 0.05f, "Silver 45%", "Gold 50%", "Diamond 5%")]
    public void Quality_line_formats_daily_game_data_weights_without_a_label(
        float bronze,
        float silver,
        float gold,
        float diamond,
        string expectedFirst,
        string? expectedMiddle,
        string expectedLast
    )
    {
        var distribution = TierDistribution.FromWeights(bronze, silver, gold, diamond);

        var text = EncounterPreviewTextFormatter.BuildQualityLine(
            distribution,
            fixedTier: null,
            dayTierCeiling: ETier.Diamond
        );

        Assert.DoesNotContain("Quality today", text);
        Assert.Contains(expectedFirst, text);
        if (expectedMiddle != null)
            Assert.Contains(expectedMiddle, text);
        Assert.Contains(expectedLast, text);
    }

    [Fact]
    public void Distribution_normalizes_positive_weights_instead_of_assuming_total_is_one()
    {
        var distribution = TierDistribution.FromWeights(0f, 0.2f, 0.55f, 0.05f);

        var text = EncounterPreviewTextFormatter.BuildQualityLine(
            distribution,
            fixedTier: null,
            dayTierCeiling: ETier.Diamond
        );

        Assert.Contains("Silver 25%", text);
        Assert.Contains("Gold 68.75%", text);
        Assert.Contains("Diamond 6.25%", text);
    }

    [Fact]
    public void Distribution_ignores_non_positive_weights_and_rejects_an_empty_table()
    {
        var distribution = TierDistribution.FromWeights(-1f, 0f, 2f, float.NaN);

        Assert.NotNull(distribution);
        Assert.Single(distribution!.Entries);
        Assert.Equal(ETier.Gold, distribution.Entries[0].Tier);
        Assert.Equal(100d, distribution.Entries[0].Percent);
        Assert.Null(TierDistribution.FromWeights(0f, -1f, float.NaN, 0f));
    }

    [Theory]
    [InlineData(ETier.Bronze)] // Curio
    [InlineData(ETier.Silver)] // Silvia
    [InlineData(ETier.Gold)] // Goldie
    [InlineData(ETier.Diamond)] // Luxe
    public void Fixed_tier_merchants_do_not_use_the_daily_distribution(ETier tier)
    {
        var template = CreateMerchantTemplate(
            new TSpawnBehaviorTier
            {
                Tiers = new HashSet<ETier> { tier },
                IsNot = false,
            },
            new TSpawnBehaviorIgnoreTierTable { IgnoreTierTable = true }
        );

        var policy = EncounterMerchantTierResolver.Resolve(template);
        var text = EncounterPreviewTextFormatter.BuildQualityLine(
            TierDistribution.FromWeights(0.9f, 0.1f, 0f, 0f),
            policy.FixedTier,
            policy.UsesDayDistribution ? ETier.Silver : null
        );

        Assert.Equal(tier, policy.FixedTier);
        Assert.False(policy.UsesDayDistribution);
        Assert.Contains($">{tier}</color>", text);
        Assert.DoesNotContain("Item quality", text);
        Assert.DoesNotContain("90%", text);
    }

    [Fact]
    public void Curio_style_fixed_tier_does_not_require_ignore_tier_table_behavior()
    {
        var template = CreateMerchantTemplate(
            new TSpawnBehaviorTier
            {
                Tiers = new HashSet<ETier> { ETier.Bronze },
                IsNot = false,
            }
        );

        var policy = EncounterMerchantTierResolver.Resolve(template);

        Assert.Equal(ETier.Bronze, policy.FixedTier);
        Assert.False(policy.UsesDayDistribution);
    }

    [Fact]
    public void Downshift_merchants_still_show_the_raw_daily_weights()
    {
        var policy = EncounterMerchantTierResolver.Resolve(
            CreateMerchantTemplate(new TSpawnBehaviorDownShiftTier())
        );

        Assert.Null(policy.FixedTier);
        Assert.True(policy.UsesDayDistribution);
    }

    [Fact]
    public void Ignore_tier_table_without_a_fixed_tier_suppresses_the_daily_line()
    {
        var policy = EncounterMerchantTierResolver.Resolve(
            CreateMerchantTemplate(new TSpawnBehaviorIgnoreTierTable { IgnoreTierTable = true })
        );

        Assert.Null(policy.FixedTier);
        Assert.False(policy.UsesDayDistribution);
    }

    [Theory]
    [InlineData("zh-CN", false, "青铜 90%", "白银 10%")]
    [InlineData("zh-TW", true, "青銅 90%", "白銀 10%")]
    public void Quality_line_localizes_both_chinese_scripts(
        string language,
        bool traditional,
        string expectedBronze,
        string expectedSilver
    )
    {
        var localeMode = traditional ? BppChineseLocaleMode.Taiwan : BppChineseLocaleMode.Mainland;
        L.Install(new TestLanguageProvider(language), new TestLocaleModeProvider(localeMode));

        var text = EncounterPreviewTextFormatter.BuildQualityLine(
            TierDistribution.FromWeights(0.9f, 0.1f, 0f, 0f),
            fixedTier: null,
            dayTierCeiling: ETier.Silver
        );

        Assert.Contains(expectedBronze, text);
        Assert.Contains(expectedSilver, text);
        Assert.DoesNotContain("品质：", text);
        Assert.DoesNotContain("品質：", text);
    }

    [Fact]
    public void Quality_line_falls_back_to_the_existing_day_ceiling()
    {
        var text = EncounterPreviewTextFormatter.BuildQualityLine(
            dayTierDistribution: null,
            fixedTier: null,
            dayTierCeiling: ETier.Gold
        );

        Assert.Contains("up to Gold", text);
        Assert.DoesNotContain("Quality today", text);
    }

    [Fact]
    public void Encounter_step_reward_line_shows_quality_for_grab_the_loot_style_rewards()
    {
        var text = EncounterPreviewTextFormatter.BuildRewardQualityLine(
            CreateRewardFilter(usesDayTierTable: true, usesDayTierDistribution: true),
            "Get 3 Loot items",
            TierDistribution.FromWeights(0.7f, 0.3f, 0f, 0f),
            ETier.Silver
        );

        Assert.Contains("Bronze 70%", text);
        Assert.Contains("Silver 30%", text);
    }

    [Fact]
    public void Encounter_step_reward_line_shows_source_weights_for_downshift_rewards()
    {
        var text = EncounterPreviewTextFormatter.BuildRewardQualityLine(
            CreateRewardFilter(usesDayTierTable: false, usesDayTierDistribution: true),
            "Gain 2 Gold and a Shield item from any Hero",
            TierDistribution.FromWeights(0.7f, 0.3f, 0f, 0f),
            ETier.Silver
        );

        Assert.Contains("Bronze 70%", text);
        Assert.Contains("Silver 30%", text);
    }

    [Fact]
    public void Encounter_step_reward_line_skips_fixed_or_inherited_quality()
    {
        var text = EncounterPreviewTextFormatter.BuildRewardQualityLine(
            CreateRewardFilter(usesDayTierTable: false, usesDayTierDistribution: false),
            "Get an item",
            TierDistribution.FromWeights(0.7f, 0.3f, 0f, 0f),
            ETier.Silver
        );

        Assert.Equal(string.Empty, text);
    }

    private static EncounterRewardFilter CreateRewardFilter(
        bool usesDayTierTable,
        bool usesDayTierDistribution
    ) =>
        new(
            ECardType.Item,
            quantity: 1,
            fromAnyHero: false,
            Array.Empty<ECardSize>(),
            new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond },
            Array.Empty<ECardTag>(),
            Array.Empty<EHiddenTag>(),
            "Item",
            usesDayTierTable: usesDayTierTable,
            usesDayTierDistribution: usesDayTierDistribution
        );

    private static TCardEncounterEvent CreateMerchantTemplate(params ITSpawnBehavior[] behaviors) =>
        new()
        {
            SelectionContext = new TSelectionContext
            {
                SpawnContext = new TSpawnContextQuery { Behaviors = behaviors.ToList() },
            },
        };

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
}
