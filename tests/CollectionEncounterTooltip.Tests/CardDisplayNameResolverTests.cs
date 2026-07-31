using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Step;
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.GameInterop.Cards;
using Xunit;

namespace EncounterTooltip.Tests;

public class CardDisplayNameResolverTests
{
    [Fact]
    public void Resolve_UsesAuthoredTitleWhenLocalizationServiceIsUnavailable()
    {
        var template = new TCardEncounterStep
        {
            InternalName = "Internal Choice Name",
            Localization = new TCardLocalization
            {
                Title = new TLocalizableText { Text = "Visible Choice Name" },
            },
        };

        var result = CardDisplayNameResolver.Resolve(template);

        Assert.Equal("Visible Choice Name", result);
    }

    [Fact]
    public void Resolve_FallsBackToInternalNameWhenTitleIsEmpty()
    {
        var template = new TCardEncounterStep { InternalName = "Internal Choice Name" };

        var result = CardDisplayNameResolver.Resolve(template);

        Assert.Equal("Internal Choice Name", result);
    }
}
