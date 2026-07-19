#nullable enable
using System.Collections;
using System.Globalization;
using BazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.GameInterop.Cards;

internal readonly record struct CardEffectValue(string ValueText, string? Unit);

internal static class CardEffectValueReader
{
    private const string AbilitiesProperty = "Abilities";
    private const string AurasProperty = "Auras";

    private static readonly HashSet<string> KnownAttributeUnits = new(StringComparer.Ordinal)
    {
        "Heal",
        "Poison",
        "Shield",
        "Burn",
        "Damage",
        "Regen",
        "Freeze",
        "Haste",
        "Slow",
        "Charge",
        "Ammo",
        "Lifesteal",
        "Crit",
        "Income",
        "Gold",
        "Experience",
        "Value",
    };

    internal static bool TryReadAbility(
        TCardBase template,
        string effectId,
        out CardEffectValue result
    ) => TryRead(template, AbilitiesProperty, effectId, out result);

    internal static bool TryReadAura(
        TCardBase template,
        string effectId,
        out CardEffectValue result
    ) => TryRead(template, AurasProperty, effectId, out result);

    private static bool TryRead(
        TCardBase template,
        string effectsProperty,
        string effectId,
        out CardEffectValue result
    )
    {
        result = default;
        var effects = template.GetType().GetProperty(effectsProperty)?.GetValue(template);
        if (effects is not IEnumerable enumerable)
            return false;

        var dot = effectId.IndexOf('.');
        var baseEffectId = dot > 0 ? effectId[..dot] : effectId;
        var accessor = dot > 0 ? effectId[(dot + 1)..] : null;
        foreach (var entry in enumerable)
        {
            if (!TryReadEntry(entry, out var key, out var effect))
                continue;
            if (!string.Equals(key?.ToString(), baseEffectId, StringComparison.Ordinal))
                continue;

            var action = effect?.GetType().GetProperty("Action")?.GetValue(effect);
            var value = action?.GetType().GetProperty("Value")?.GetValue(action);
            if (string.Equals(accessor, "mod", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadModifierScalar(value, out var modifierText))
                    return false;
                result = new CardEffectValue(modifierText, null);
                return true;
            }

            var scalar = value?.GetType().GetProperty("Value")?.GetValue(value);
            if (TryFormatScalar(scalar, out var scalarText))
            {
                result = new CardEffectValue(scalarText, ResolveAttributeUnit(action));
                return true;
            }

            if (
                TryReadReferencedCardAttribute(
                    template,
                    value,
                    applyModifier: !string.Equals(
                        accessor,
                        "ref",
                        StringComparison.OrdinalIgnoreCase
                    ),
                    out var attributeText
                )
            )
            {
                result = new CardEffectValue(attributeText, ResolveAttributeUnit(action));
                return true;
            }

            var spawnContext = action?.GetType().GetProperty("SpawnContext")?.GetValue(action);
            var limit = spawnContext?.GetType().GetProperty("Limit")?.GetValue(spawnContext);
            var limitScalar = limit?.GetType().GetProperty("Value")?.GetValue(limit);
            if (TryFormatScalar(limitScalar, out var limitText))
            {
                result = new CardEffectValue(limitText, null);
                return true;
            }

            return TryReadFromCardAttributes(template, action, out result);
        }

        return false;
    }

    private static bool TryReadModifierScalar(object? value, out string valueText)
    {
        var modifier = value?.GetType().GetProperty("Modifier")?.GetValue(value);
        var modifierValue = modifier?.GetType().GetProperty("Value")?.GetValue(modifier);
        var modifierScalar = modifierValue?.GetType().GetProperty("Value")?.GetValue(modifierValue);
        return TryFormatScalar(modifierScalar, out valueText);
    }

    private static bool TryReadReferencedCardAttribute(
        TCardBase template,
        object? value,
        bool applyModifier,
        out string valueText
    )
    {
        valueText = string.Empty;
        var valueTypeName = value?.GetType().Name;
        if (
            !string.Equals(valueTypeName, "TReferenceValueCardAttribute", StringComparison.Ordinal)
            && !string.Equals(
                valueTypeName,
                "TReferenceValueCardAttributeUnscaled",
                StringComparison.Ordinal
            )
        )
            return false;

        var attributeType = value!.GetType().GetProperty("AttributeType")?.GetValue(value);
        if (attributeType == null)
            return false;

        var attributes = template.GetType().GetProperty("Attributes")?.GetValue(template);
        if (attributes is not IEnumerable entries)
            return false;
        foreach (var entry in entries)
        {
            if (!TryReadEntry(entry, out var key, out var attributeValue))
                continue;
            if (!string.Equals(key?.ToString(), attributeType.ToString(), StringComparison.Ordinal))
                continue;

            if (!applyModifier)
                return TryFormatScalar(attributeValue, out valueText);
            return TryApplyModifier(value, attributeValue, out valueText);
        }
        return false;
    }

    private static bool TryApplyModifier(
        object referenceValue,
        object? attributeValue,
        out string valueText
    )
    {
        valueText = string.Empty;
        if (!TryConvertNumber(attributeValue, out var original))
            return false;

        var modifier = referenceValue.GetType().GetProperty("Modifier")?.GetValue(referenceValue);
        if (modifier == null)
        {
            valueText = FormatNumber(original);
            return true;
        }

        var modifierValue = modifier.GetType().GetProperty("Value")?.GetValue(modifier);
        var modifierScalar = modifierValue?.GetType().GetProperty("Value")?.GetValue(modifierValue);
        if (!TryConvertNumber(modifierScalar, out var operand))
            return false;

        var mode = modifier.GetType().GetProperty("ModifyMode")?.GetValue(modifier)?.ToString();
        var modified = mode switch
        {
            "Add" => original + operand,
            "Subtract" => original - operand,
            "Multiply" => RoundIfRequested(original * operand, modifier),
            "Divide" => operand == 0d ? original : RoundIfRequested(original / operand, modifier),
            _ => original,
        };
        valueText = FormatNumber(modified);
        return true;
    }

    private static double RoundIfRequested(double value, object modifier)
    {
        var shouldRound = modifier.GetType().GetProperty("ShouldRound")?.GetValue(modifier);
        if (shouldRound is bool enabled && !enabled)
            return value;
        return value > 0d && value < 1d ? 1d : Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static bool TryReadFromCardAttributes(
        TCardBase template,
        object? action,
        out CardEffectValue result
    )
    {
        result = default;
        var actionName = action?.GetType().Name;
        if (actionName == null)
            return false;

        const string PlayerPrefix = "TActionPlayer";
        const string CardPrefix = "TActionCard";
        var core =
            actionName.StartsWith(PlayerPrefix, StringComparison.Ordinal)
                ? actionName[PlayerPrefix.Length..]
            : actionName.StartsWith(CardPrefix, StringComparison.Ordinal)
                ? actionName[CardPrefix.Length..]
            : null;
        if (string.IsNullOrEmpty(core))
            return false;

        var attributeKey = core + "Amount";
        var attributes = template.GetType().GetProperty("Attributes")?.GetValue(template);
        if (attributes is not IEnumerable entries)
            return false;
        foreach (var entry in entries)
        {
            if (!TryReadEntry(entry, out var key, out var attributeValue))
                continue;
            if (!string.Equals(key?.ToString(), attributeKey, StringComparison.Ordinal))
                continue;
            if (!TryFormatScalar(attributeValue, out var valueText))
                return false;
            result = new CardEffectValue(valueText, NormalizeAttributeUnit(attributeKey));
            return true;
        }
        return false;
    }

    private static string? ResolveAttributeUnit(object? action)
    {
        var attribute = action?.GetType().GetProperty("AttributeType")?.GetValue(action);
        return attribute == null ? null : NormalizeAttributeUnit(attribute.ToString());
    }

    private static string? NormalizeAttributeUnit(string? name)
    {
        if (string.IsNullOrEmpty(name) || name!.Contains('_'))
            return null;
        if (name.EndsWith("Amount", StringComparison.Ordinal))
            name = name[..^"Amount".Length];
        if (name.EndsWith("Apply", StringComparison.Ordinal))
            name = name[..^"Apply".Length];
        return KnownAttributeUnits.Contains(name) ? name : null;
    }

    private static bool TryReadEntry(object entry, out object? key, out object? value)
    {
        if (entry is DictionaryEntry dictionaryEntry)
        {
            key = dictionaryEntry.Key;
            value = dictionaryEntry.Value;
            return true;
        }

        var type = entry.GetType();
        key = type.GetProperty("Key")?.GetValue(entry);
        value = type.GetProperty("Value")?.GetValue(entry);
        return key != null;
    }

    private static bool TryConvertNumber(object? scalar, out double value)
    {
        switch (scalar)
        {
            case float number:
                value = number;
                return true;
            case double number:
                value = number;
                return true;
            case decimal number:
                value = (double)number;
                return true;
            case int number:
                value = number;
                return true;
            case long number:
                value = number;
                return true;
            default:
                value = 0d;
                return false;
        }
    }

    private static bool TryFormatScalar(object? scalar, out string valueText)
    {
        valueText = string.Empty;
        if (TryConvertNumber(scalar, out var number))
        {
            valueText = FormatNumber(number);
            return true;
        }
        if (scalar == null)
            return false;

        valueText = scalar.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(valueText);
    }

    private static string FormatNumber(double value)
    {
        var rounded = Math.Round(value);
        return Math.Abs(value - rounded) < 0.0001
            ? rounded.ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
