using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Step;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EventPreviewLocalizationTests
{
    [Fact]
    public void Captured_aura_reference_value_renders_in_event_preview_copy()
    {
        var template = new TCardEncounterStep
        {
            Id = Guid.Parse("1f6f3974-ce2d-4e9e-a1d9-9a3367a0adb9"),
            InternalName = "[Shrouded Figure] Choose Knowledge",
            Localization = new TCardLocalization
            {
                Title = new TLocalizableText { Text = "Choose Knowledge" },
                Description = new TLocalizableText
                {
                    Text = "Pick a Chest containing up to {aura.9} XP",
                },
            },
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_0] = 3,
            },
            Auras = new Dictionary<string, TCardAura>
            {
                ["9"] = new TCardAura
                {
                    Action = new TAuraActionPlayerModifyAttribute
                    {
                        AttributeType = EPlayerAttributeType.Experience,
                        Value = new TReferenceValueCardAttribute
                        {
                            AttributeType = ECardAttributeType.Custom_0,
                            Modifier = new TValueModifier
                            {
                                ModifyMode = EValueModifierMode.Multiply,
                                Value = new TFixedValue { Value = 1f },
                            },
                        },
                    },
                },
            },
        };
        var placeholderValues = EventPreviewLocalization.CapturePlaceholderValues(template);
        var plan = new EncounterPreviewTemplatePlan(
            template.Id,
            EncounterPreviewTemplateKind.EncounterStep,
            Array.Empty<EHero>(),
            template.InternalName,
            new EncounterPreviewLocalizedText(null, template.Localization.Title.Text),
            new EncounterPreviewLocalizedText(null, template.Localization.Description.Text),
            placeholderValues,
            rewardFilter: null
        );

        var description = EventPreviewLocalization.ResolveDescription(plan);

        Assert.Equal("Pick a Chest containing up to 3 XP", description);
        Assert.Equal("3", placeholderValues["aura.9"].ValueText);
        Assert.DoesNotContain("{aura.", description);
    }
}
