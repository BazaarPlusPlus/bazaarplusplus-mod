using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactEntitySnapshotReaderTests
{
    [Theory]
    [InlineData("<style=Radiant>Radiant</style>\nHunter's Journal")]
    [InlineData("<style=Radiant>Hunter's Journal</style>")]
    public void Hunter_journal_never_leaks_native_style_tags(string nativeTitle) =>
        Assert.Equal(
            "Hunter's Journal",
            CombatImpactEntityName.RemoveNativeEnchantmentPrefix(nativeTitle)
        );
}
