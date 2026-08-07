using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Xunit;

namespace PostCombatImpact.Tests;

public sealed class CombatImpactEntitySnapshotReaderTests
{
    [Fact]
    public void Hidden_tags_fall_back_to_the_template_for_rehydrated_replay_cards()
    {
        var card = RehydratedItem(templateTags: [], templateHiddenTags: [EHiddenTag.Haste]);

        Assert.Contains(EHiddenTag.Haste, CombatImpactEntityTags.ResolveHiddenTags(card)!);
    }

    [Fact]
    public void Public_tags_fall_back_to_the_template_for_rehydrated_replay_cards()
    {
        var card = RehydratedItem(templateTags: [ECardTag.Weapon], templateHiddenTags: []);

        Assert.Contains(ECardTag.Weapon, CombatImpactEntityTags.ResolveTags(card)!);
    }

    [Theory]
    [InlineData("<style=Radiant>Radiant</style>\nHunter's Journal")]
    [InlineData("<style=Radiant>Hunter's Journal</style>")]
    public void Hunter_journal_never_leaks_native_style_tags(string nativeTitle) =>
        Assert.Equal(
            "Hunter's Journal",
            CombatImpactEntityName.RemoveNativeEnchantmentPrefix(nativeTitle)
        );

    private static ItemCard RehydratedItem(
        IReadOnlyCollection<ECardTag> templateTags,
        IReadOnlyCollection<EHiddenTag> templateHiddenTags
    ) =>
        new()
        {
            Type = ECardType.Item,
            Tags = [],
            HiddenTags = [],
            Template = new TCardItem
            {
                Type = ECardType.Item,
                Tags = templateTags.ToHashSet(),
                HiddenTags = templateHiddenTags.ToHashSet(),
            },
        };
}
