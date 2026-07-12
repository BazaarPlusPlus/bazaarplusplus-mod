#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static partial class CollectionLocalizationResolver
{
    public static string? ResolveTitle(CollectionEncounterPreviewTemplatePlan template)
    {
        if (template == null)
            return null;
        return PickText(template.Title);
    }

    public static string? ResolveDescription(CollectionEncounterPreviewTemplatePlan template)
    {
        if (template == null)
            return null;
        var text = PickText(template.Description);
        return FormatAbilityPlaceholders(template, text);
    }

    internal static IReadOnlyDictionary<
        string,
        CollectionEncounterPreviewAbilityValue
    > CaptureAbilityValues(TCardBase template)
    {
        var result = new Dictionary<string, CollectionEncounterPreviewAbilityValue>(
            StringComparer.Ordinal
        );
        var abilities = template.Abilities;
        if (abilities == null)
            return result;

        foreach (var abilityId in abilities.Keys)
        {
            AddAbilityValue(abilityId);
            AddAbilityValue($"{abilityId}.mod");
        }
        return result;

        void AddAbilityValue(string placeholder)
        {
            if (TryResolveAbilityValue(template, placeholder, out var valueText, out var unit))
                result[placeholder] = new CollectionEncounterPreviewAbilityValue(valueText, unit);
        }
    }

    private static string? PickText(CollectionEncounterPreviewLocalizedText text) =>
        PickText(
            new TLocalizableText
            {
                Key = text.Key ?? string.Empty,
                Text = text.FallbackText ?? string.Empty,
            }
        );

    private static string? FormatAbilityPlaceholders(
        CollectionEncounterPreviewTemplatePlan template,
        string? text
    ) =>
        FormatAbilityPlaceholders(
            text,
            (string abilityId, out string valueText, out string? unit) =>
            {
                if (template.AbilityValues.TryGetValue(abilityId, out var value))
                {
                    valueText = value.ValueText;
                    unit = value.Unit;
                    return true;
                }

                valueText = string.Empty;
                unit = null;
                return false;
            }
        );
}
