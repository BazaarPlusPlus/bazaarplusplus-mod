using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Localization;
using Xunit;

namespace CollectionEncounterTooltip.Tests;

public sealed class AggregateItemMissingTypesTextTests
{
    public AggregateItemMissingTypesTextTests()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
    }

    [Fact]
    public void FindMissing_preserves_player_facing_order_and_excludes_system_tags()
    {
        var missing = AggregateItemMissingTypesText.FindMissing(
            new[] { ECardTag.Weapon, ECardTag.Tool, ECardTag.Apparel }
        );

        Assert.DoesNotContain(ECardTag.Weapon, missing);
        Assert.DoesNotContain(ECardTag.Tool, missing);
        Assert.DoesNotContain(ECardTag.Apparel, missing);
        Assert.DoesNotContain(ECardTag.Event, missing);
        Assert.DoesNotContain(ECardTag.Merchant, missing);
        Assert.Equal(ECardTag.Friend, missing[0]);
    }

    [Fact]
    public void Build_uses_native_keyword_colorizer_for_the_complete_line()
    {
        var content = AggregateItemMissingTypesText.Build(
            AggregateItemMissingTypesText.ItemTypes.Where(tag => tag != ECardTag.Food),
            text => $"colored({text})"
        );

        Assert.Equal("colored(Missing Types: Food)", content);
    }

    [Fact]
    public void Build_returns_null_when_every_type_is_present()
    {
        Assert.Null(AggregateItemMissingTypesText.Build(AggregateItemMissingTypesText.ItemTypes));
    }

    [Fact]
    public void Build_localizes_the_heading_for_simplified_chinese()
    {
        L.Install(new TestLanguageProvider("zh-CN"), new TestLocaleModeProvider());

        var content = AggregateItemMissingTypesText.Build(
            AggregateItemMissingTypesText.ItemTypes.Where(tag => tag != ECardTag.Relic)
        );

        Assert.Equal("尚缺类型： Relic", content);
    }

    private sealed class TestLanguageProvider(string languageCode = "en") : ILanguageProvider
    {
        public string CurrentLanguageCode => languageCode;
    }

    private sealed class TestLocaleModeProvider : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}
