#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.GameInterop.CustomCards;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.Achievements;

internal static class AchievementCardRegistrar
{
    public static void Register(BppCustomCardRegistry registry) =>
        Register(registry, AchievementCardCatalog.LoadEmbedded);

    internal static void Register(
        BppCustomCardRegistry registry,
        Func<AchievementCardCatalog> loadCatalog
    )
    {
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (loadCatalog == null)
            throw new ArgumentNullException(nameof(loadCatalog));

        try
        {
            var catalog = loadCatalog();
            var mapper = new AchievementCardDescriptorMapper();
            var staged = registry.Copy();
            var descriptors = new List<BppCustomCardDescriptor>(catalog.Cards.Count);
            foreach (var card in catalog.Cards)
            {
                var descriptor = mapper.Map(card);
                staged.Register(descriptor);
                descriptors.Add(descriptor);
            }

            foreach (var descriptor in descriptors)
                registry.Register(descriptor);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "Achievements",
                $"Achievement catalog load failed; achievements disabled: {ex.Message}"
            );
        }
    }
}
