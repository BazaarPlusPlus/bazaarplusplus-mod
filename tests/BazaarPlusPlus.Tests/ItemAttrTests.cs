using BazaarPlusPlus;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class ItemAttrTests
{
    public ItemAttrTests()
    {
        ModState.CardsJsonPath = null;
    }

    [Fact]
    public void GetCardsJsonPath_ReturnsConfiguredPathForMac()
    {
        var path = CardJsonPathResolver.GetCardsJsonPath("mac");

        Assert.Equal("/Users/yxinyu/codes/BazaarPlusPlus/cards.json", path);
    }

    [Fact]
    public void GetCardsJsonPath_ReturnsConfiguredPathForWindows()
    {
        var path = CardJsonPathResolver.GetCardsJsonPath("windows");

        Assert.Equal(@"C:\Users\yxinyu\codes\BazaarPlusPlus\cards.json", path);
    }

    [Fact]
    public void GetAttributes_ReturnsBronzeTierValues()
    {
        ModState.CardsJsonPath = "/Users/yxinyu/codes/BazaarPlusPlus/cards.json";
        var attributes = ItemAttr.GetAttributes(
            new Guid("da766a09-0352-4966-829f-20bda8da48d7"),
            "Bronze"
        );

        Assert.Single(attributes);
        Assert.Equal(5, attributes["Custom_0"]);
    }

    [Fact]
    public void GetAttributes_MergesStartingTierIntoHigherTier()
    {
        ModState.CardsJsonPath = "/Users/yxinyu/codes/BazaarPlusPlus/cards.json";
        var attributes = ItemAttr.GetAttributes(
            new Guid("14cd589f-5648-4fc7-82d1-c62da0047982"),
            "Diamond"
        );

        Assert.Equal(4, attributes.Count);
        Assert.Equal(7000, attributes["CooldownMax"]);
        Assert.Equal(1, attributes["Multicast"]);
        Assert.Equal(0, attributes["HealAmount"]);
        Assert.Equal(6, attributes["Custom_0"]);
    }

    [Fact]
    public void GetAttributes_ReturnsEmptyForUnknownTemplate()
    {
        ModState.CardsJsonPath = "/Users/yxinyu/codes/BazaarPlusPlus/cards.json";
        var attributes = ItemAttr.GetAttributes(Guid.NewGuid(), "Gold");

        Assert.Empty(attributes);
    }

}
