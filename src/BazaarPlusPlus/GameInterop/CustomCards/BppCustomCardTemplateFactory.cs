#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Tooltips;

namespace BazaarPlusPlus.GameInterop.CustomCards;

internal static class BppCustomCardTemplateFactory
{
    public static TCardBase Build(BppCustomCardDescriptor descriptor)
    {
        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));

        return descriptor.Type == ECardType.Skill
            ? ApplySharedFields(
                new TCardSkill { StartingTier = descriptor.StartingTier },
                descriptor
            )
            : ApplySharedFields(
                new TCardItem { Type = ECardType.Item, StartingTier = descriptor.StartingTier },
                descriptor
            );
    }

    private static TCardBase ApplySharedFields(
        TCardItem template,
        BppCustomCardDescriptor descriptor
    ) =>
        template with
        {
            Id = descriptor.Id,
            Version = "bpp-custom",
            InternalName = descriptor.InternalName,
            Size = descriptor.Size,
            ArtKey = string.Empty,
            Localization = BuildLocalization(descriptor),
        };

    private static TCardBase ApplySharedFields(
        TCardSkill template,
        BppCustomCardDescriptor descriptor
    ) =>
        template with
        {
            Id = descriptor.Id,
            Version = "bpp-custom",
            InternalName = descriptor.InternalName,
            Size = descriptor.Size,
            ArtKey = string.Empty,
            Localization = BuildLocalization(descriptor),
        };

    private static TCardLocalization BuildLocalization(BppCustomCardDescriptor descriptor)
    {
        var title = BppCustomCardText.ResolveOrEnglish(descriptor.Title);
        var description = BppCustomCardText.ResolveOrEnglish(descriptor.Description);

        return new TCardLocalization
        {
            Title = new TLocalizableText { Text = title },
            Description = new TLocalizableText { Text = description },
            Tooltips = new List<TTooltip>
            {
                new()
                {
                    TooltipType = ETooltipType.Passive,
                    Content = new TLocalizableText { Text = description },
                },
            },
        };
    }
}
