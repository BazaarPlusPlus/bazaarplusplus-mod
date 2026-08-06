using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactEntitySnapshotReaderTests
{
    [Fact]
    public void Hidden_tags_fall_back_to_the_template_for_rehydrated_replay_cards()
    {
        var hiddenTags = CombatImpactHiddenTags.Merge(
            runtime: [],
            template: [EHiddenTag.Haste],
            enchantment: null
        );

        Assert.Contains(EHiddenTag.Haste, hiddenTags!);
    }

    [Theory]
    [InlineData("<style=Radiant>Radiant</style>\nHunter's Journal")]
    [InlineData("<style=Radiant>Hunter's Journal</style>")]
    public void Hunter_journal_never_leaks_native_style_tags(string nativeTitle) =>
        Assert.Equal(
            "Hunter's Journal",
            CombatImpactEntityName.RemoveNativeEnchantmentPrefix(nativeTitle)
        );
}
