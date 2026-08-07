using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
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

    [Fact]
    public void Transformed_replay_cards_retain_their_creation_update_for_rehydration()
    {
        var simulation = new CombatSim();
        var transformed = new SimEventCardTransformation(
            "transformed",
            "16b0d645-3a47-45a1-be2a-1e3ee311f33e",
            ECardType.Item,
            ECombatantId.Player,
            EInventorySection.Hand,
            EContainerSocketId.Socket_2
        );
        simulation
            .Frames[0]
            .Events.Add(new CombatSimEventCardTransformed("context", "original", [transformed]));
        var transformedId = InstanceId.TryParse(transformed.InstanceId);
        simulation.Frames[0].CardUpdates[transformedId] = new CombatSimCardUpdate
        {
            CardInstanceId = transformedId,
            Tags = [],
            HiddenTags = [],
            Tier = ETier.Gold,
        };
        var snapshot = Assert.Single(CombatImpactTransformedCardSnapshotReader.Read(simulation));

        Assert.Equal("transformed", snapshot.Card.InstanceId);
        Assert.Equal(ECombatantId.Player, snapshot.Card.CombatantId);
        Assert.Equal(EInventorySection.Hand, snapshot.Card.Section);
        Assert.Equal(EContainerSocketId.Socket_2, snapshot.Card.Socket);
        Assert.Equal(ETier.Gold, snapshot.Update?.Tier);
        Assert.Empty(snapshot.Update?.Tags!);
        Assert.Empty(snapshot.Update?.HiddenTags!);
    }

    [Fact]
    public void Transform_revert_snapshots_include_each_incarnation_once()
    {
        var simulation = new CombatSim();
        var original = new SimEventCardTransformation(
            "original",
            Guid.NewGuid().ToString(),
            ECardType.Item,
            ECombatantId.Player,
            EInventorySection.Hand,
            EContainerSocketId.Socket_2
        );
        var transformed = original with
        {
            InstanceId = "transformed",
            TemplateId = Guid.NewGuid().ToString(),
        };
        simulation
            .Frames[0]
            .Events.Add(
                new CombatSimEventCardTransformed("context", original.InstanceId, [transformed])
            );
        simulation.Frames.Add(new CombatSimFrame());
        simulation
            .Frames[1]
            .Events.Add(
                new CombatSimEventCardTransformReverted(original, [transformed.InstanceId])
            );

        var snapshots = CombatImpactTransformedCardSnapshotReader.Read(simulation);

        Assert.Equal(["transformed", "original"], snapshots.Select(item => item.Card.InstanceId));
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
