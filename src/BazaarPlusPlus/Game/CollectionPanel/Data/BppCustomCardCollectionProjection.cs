#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.GameInterop.CustomCards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class BppCustomCardCollectionProjection
{
    public static IReadOnlyList<CollectionCardVm> BuildVms(BppCustomCardRegistry? registry)
    {
        if (registry == null)
            return Array.Empty<CollectionCardVm>();

        var descriptors = registry.GetAll();
        var result = new List<CollectionCardVm>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            result.Add(
                new CollectionCardVm
                {
                    Id = descriptor.Id,
                    Type = descriptor.Type,
                    Size = descriptor.Size,
                    StartingTier = descriptor.StartingTier,
                    DisplayName = BppCustomCardText.ResolveOrEnglish(descriptor.Title),
                    InternalName = descriptor.InternalName,
                    ArtKey = "bpp-custom",
                    IsPackage = false,
                }
            );
        }

        return result;
    }
}
