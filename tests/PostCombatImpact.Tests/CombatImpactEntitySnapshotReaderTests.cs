using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactEntitySnapshotReaderTests
{
    [Fact]
    public void Native_enchantment_style_prefix_is_not_used_as_the_entity_name()
    {
        var title = CombatImpactEntityName.RemoveNativeEnchantmentPrefix(
            "<style=Radiant>Radiant</style>\nHunter's Journal"
        );

        Assert.Equal("Hunter's Journal", title);
    }

    [Fact]
    public void Native_rich_text_wrapping_the_name_is_removed()
    {
        var title = CombatImpactEntityName.RemoveNativeEnchantmentPrefix(
            "<style=Radiant>Hunter's Journal</style>"
        );

        Assert.Equal("Hunter's Journal", title);
    }
}
