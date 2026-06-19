#nullable enable
using System;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;

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
            Localization = new TCardLocalization
            {
                Title = new TLocalizableText
                {
                    Text = BppCustomCardText.ResolveOrEnglish(descriptor.Title),
                },
                Description = new TLocalizableText
                {
                    Text = BppCustomCardText.ResolveOrEnglish(descriptor.Description),
                },
            },
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
            Localization = new TCardLocalization
            {
                Title = new TLocalizableText
                {
                    Text = BppCustomCardText.ResolveOrEnglish(descriptor.Title),
                },
                Description = new TLocalizableText
                {
                    Text = BppCustomCardText.ResolveOrEnglish(descriptor.Description),
                },
            },
        };
}
