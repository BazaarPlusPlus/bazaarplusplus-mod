#nullable enable
using System;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.CustomCards;

namespace BazaarPlusPlus.Game.Achievements;

internal sealed class AchievementCardDescriptorMapper
{
    private const string ArtResourcePrefix = "BazaarPlusPlus.Resources.CustomCardArt.";
    private readonly Func<Guid, bool> _hasBundledArt;

    public AchievementCardDescriptorMapper()
        : this(DefaultHasBundledArt) { }

    internal AchievementCardDescriptorMapper(Func<Guid, bool> hasBundledArt)
    {
        _hasBundledArt = hasBundledArt ?? throw new ArgumentNullException(nameof(hasBundledArt));
    }

    public BppCustomCardDescriptor Map(AchievementCardDefinition card)
    {
        if (card == null)
            throw new ArgumentNullException(nameof(card));

        return new BppCustomCardDescriptor
        {
            Id = card.TemplateId,
            Type = ECardType.Item,
            Size = card.DisplaySize,
            StartingTier = card.DisplayTier,
            Title = card.Title,
            Description = card.Description,
            HasBundledArt = _hasBundledArt(card.TemplateId),
            InternalName = card.InternalName,
            SortKey = card.SortKey,
        };
    }

    private static bool DefaultHasBundledArt(Guid templateId)
    {
        var expected = $"{ArtResourcePrefix}{templateId}.jpg";
        return Assembly
            .GetExecutingAssembly()
            .GetManifestResourceNames()
            .Any(name => string.Equals(name, expected, StringComparison.Ordinal));
    }
}
