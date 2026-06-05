#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Game-card-model side of CollectionCardVm: projects a live TCardBase template into the VM.
// Kept separate from the POCO half so the layout test can compile CollectionCardVm.cs without
// dragging in TCardBase / CollectionLocalizationResolver.
internal sealed partial class CollectionCardVm
{
    public static CollectionCardVm From(TCardBase template) =>
        From(template, CollectionCardClassifier.Classify(template));

    internal static CollectionCardVm From(
        TCardBase template,
        CollectionCardClassification classification
    )
    {
        return new CollectionCardVm
        {
            Id = template.Id,
            Type = template.Type,
            Size = template.Size,
            StartingTier = template.StartingTier,
            Heroes = template.Heroes,
            Tags = template.Tags,
            HiddenTags = template.HiddenTags,
            DisplayName =
                CollectionLocalizationResolver.ResolveTitle(template) ?? template.InternalName,
            InternalName = template.InternalName,
            ArtKey = template.ArtKey,
            IsEnchantable =
                template is TCardItem item
                && item.Enchantments != null
                && item.Enchantments.Count > 0,
            IsPackage = classification.IsPackage,
            Merchants = classification.Merchants,
        };
    }
}
