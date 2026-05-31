#nullable enable
using BazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Game-card-model side of CollectionCardVm: projects a live TCardBase template into the VM.
// Kept separate from the POCO half so the layout test can compile CollectionCardVm.cs without
// dragging in TCardBase / CollectionLocalizationResolver.
internal sealed partial class CollectionCardVm
{
    public static CollectionCardVm From(TCardBase template)
    {
        return new CollectionCardVm
        {
            Id = template.Id,
            Type = template.Type,
            Size = template.Size,
            StartingTier = template.StartingTier,
            Heroes = template.Heroes,
            Tags = template.Tags,
            DisplayName =
                CollectionLocalizationResolver.ResolveTitle(template) ?? template.InternalName,
            InternalName = template.InternalName,
            ArtKey = template.ArtKey,
        };
    }
}
