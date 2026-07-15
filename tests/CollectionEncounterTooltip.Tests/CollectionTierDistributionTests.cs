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

namespace CollectionEncounterTooltip.Tests;

public sealed class CollectionTierDistributionTests
{
    public CollectionTierDistributionTests()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
    }

    [Theory]
    [InlineData(0.9f, 0.1f, 0f, 0f, "Bronze 90%", null, "Silver 10%")]
    [InlineData(0f, 0.95f, 0.05f, 0f, "Silver 95%", null, "Gold 5%")]
    [InlineData(0f, 0.45f, 0.5f, 0.05f, "Silver 45%", "Gold 50%", "Diamond 5%")]
    public void Merchant_line_formats_daily_game_data_weights(
        float bronze,
        float silver,
        float gold,
        float diamond,
        string expectedFirst,
        string? expectedMiddle,
        string expectedLast
    )
    {
        var distribution = CollectionTierDistribution.FromWeights(bronze, silver, gold, diamond);

        var text = CollectionEncounterGameTooltipText.BuildMerchantQuality(
            distribution,
            fixedTier: null,
            dayTierCeiling: ETier.Diamond
        );

        Assert.Contains("Quality today:", text);
        Assert.Contains(expectedFirst, text);
        if (expectedMiddle != null)
            Assert.Contains(expectedMiddle, text);
        Assert.Contains(expectedLast, text);
    }

    [Fact]
    public void Distribution_normalizes_positive_weights_instead_of_assuming_total_is_one()
    {
        var distribution = CollectionTierDistribution.FromWeights(0f, 0.2f, 0.55f, 0.05f);

        var text = CollectionEncounterGameTooltipText.BuildMerchantQuality(
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
        var distribution = CollectionTierDistribution.FromWeights(-1f, 0f, 2f, float.NaN);

        Assert.NotNull(distribution);
        Assert.Single(distribution!.Entries);
        Assert.Equal(ETier.Gold, distribution.Entries[0].Tier);
        Assert.Equal(100d, distribution.Entries[0].Percent);
        Assert.Null(CollectionTierDistribution.FromWeights(0f, -1f, float.NaN, 0f));
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

        var policy = CollectionMerchantTierResolver.Resolve(template);
        var text = CollectionEncounterGameTooltipText.BuildMerchantQuality(
            CollectionTierDistribution.FromWeights(0.9f, 0.1f, 0f, 0f),
            policy.FixedTier,
            policy.UsesDayDistribution ? ETier.Silver : null
        );

        Assert.Equal(tier, policy.FixedTier);
        Assert.False(policy.UsesDayDistribution);
        Assert.Contains("Item quality: <color=", text);
        Assert.Contains($">{tier}</color>", text);
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

        var policy = CollectionMerchantTierResolver.Resolve(template);

        Assert.Equal(ETier.Bronze, policy.FixedTier);
        Assert.False(policy.UsesDayDistribution);
    }

    [Fact]
    public void Downshift_merchants_still_show_the_raw_daily_weights()
    {
        var policy = CollectionMerchantTierResolver.Resolve(
            CreateMerchantTemplate(new TSpawnBehaviorDownShiftTier())
        );

        Assert.Null(policy.FixedTier);
        Assert.True(policy.UsesDayDistribution);
    }

    [Fact]
    public void Ignore_tier_table_without_a_fixed_tier_suppresses_the_daily_line()
    {
        var policy = CollectionMerchantTierResolver.Resolve(
            CreateMerchantTemplate(new TSpawnBehaviorIgnoreTierTable { IgnoreTierTable = true })
        );

        Assert.Null(policy.FixedTier);
        Assert.False(policy.UsesDayDistribution);
    }

    [Theory]
    [InlineData("zh-CN", false, "今日品质：", "青铜 90%", "白银 10%")]
    [InlineData("zh-TW", true, "今日品質：", "青銅 90%", "白銀 10%")]
    public void Merchant_line_localizes_both_chinese_scripts(
        string language,
        bool traditional,
        string expectedLabel,
        string expectedBronze,
        string expectedSilver
    )
    {
        var localeMode = traditional ? BppChineseLocaleMode.Taiwan : BppChineseLocaleMode.Mainland;
        L.Install(new TestLanguageProvider(language), new TestLocaleModeProvider(localeMode));

        var text = CollectionEncounterGameTooltipText.BuildMerchantQuality(
            CollectionTierDistribution.FromWeights(0.9f, 0.1f, 0f, 0f),
            fixedTier: null,
            dayTierCeiling: ETier.Silver
        );

        Assert.Contains(expectedLabel, text);
        Assert.Contains(expectedBronze, text);
        Assert.Contains(expectedSilver, text);
    }

    [Fact]
    public void Merchant_line_falls_back_to_the_existing_day_ceiling()
    {
        var text = CollectionEncounterGameTooltipText.BuildMerchantQuality(
            dayTierDistribution: null,
            fixedTier: null,
            dayTierCeiling: ETier.Gold
        );

        Assert.Contains("Quality today: up to Gold", text);
    }

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
