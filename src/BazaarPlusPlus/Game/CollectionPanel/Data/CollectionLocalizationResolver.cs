#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// TCardLocalization carries Title/Description TLocalizableText with Key + Text. The Text
// field is the authored (English) fallback; the current-language translation resolves via
// TooltipExtensions.GetLocalizedText -> LocalizationService.TryGetText keyed lookup, which
// is what the native tooltip pipeline uses. We do the same, falling back to Text (and then
// the key for diagnostic visibility) when the service is unavailable — e.g. in unit tests.
internal static class CollectionLocalizationResolver
{
    public static string? ResolveTitle(TCardBase template)
    {
        var title = template.Localization?.Title;
        if (title == null)
            return null;
        return PickText(title);
    }

    public static string? ResolveDescription(TCardBase template)
    {
        var description = template.Localization?.Description;
        if (description == null)
            return null;
        var text = PickText(description);
        return FormatAbilityPlaceholders(template, text);
    }

    private static string? PickText(TLocalizableText text)
    {
        try
        {
            var localized = TheBazaar.Tooltips.TooltipExtensions.GetLocalizedText(text);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }
        catch (Exception)
        {
            // Localization service unavailable (unit tests / early startup):
            // fall back to the authored text below.
        }

        if (!string.IsNullOrWhiteSpace(text.Text))
            return text.Text;
        if (!string.IsNullOrWhiteSpace(text.Key))
            return text.Key;
        return null;
    }

    // Installed by the tooltip patch layer: maps a canonical attribute keyword
    // ("Heal") to the game's localized display word ("治疗" on zh clients) via
    // TooltipTypography. Null (or a null return) falls back to the English name so
    // the data layer never depends on game UI services directly.
    internal static Func<string, string?>? AttributeUnitLocalizer;

    private static string? FormatAbilityPlaceholders(TCardBase template, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("{ability.", StringComparison.Ordinal))
            return text;

        return Regex.Replace(
            text!,
            @"\{ability\.([^}]+)\}",
            match =>
            {
                if (!TryResolveAbilityValue(template, match.Groups[1].Value, out var value, out var unit))
                    return match.Value;
                if (unit == null)
                    return value;

                var localizedUnit = AttributeUnitLocalizer?.Invoke(unit);
                if (string.IsNullOrWhiteSpace(localizedUnit))
                    localizedUnit = unit;

                var after = match.Index + match.Length;

                // CJK text carries its own unit or measure word right after the
                // placeholder ("获得{ability.0}点经验值" / "…{ability.0}金币"),
                // so appending anything there only produces mixed-language noise.
                var next = after;
                while (next < text!.Length && char.IsWhiteSpace(text[next]))
                    next++;
                if (next < text.Length && IsCjk(text[next]))
                    return value;

                // Some templates spell the unit out right after the placeholder
                // ("Gain {ability.0} Gold" / "Gain {ability.0} XP" for Experience);
                // only append when the text does not already carry it, in either
                // language or a known alias.
                if (FollowingWordEquals(text!, after, unit)
                    || FollowingWordEquals(text!, after, localizedUnit!))
                    return value;
                if (UnitAliases.TryGetValue(unit, out var aliases))
                    foreach (var alias in aliases)
                        if (FollowingWordEquals(text!, after, alias))
                            return value;

                // CJK words join without a space.
                return IsCjk(localizedUnit![0])
                    ? $"{value}{localizedUnit}"
                    : $"{value} {localizedUnit}";
            },
            RegexOptions.CultureInvariant
        );
    }

    private static bool FollowingWordEquals(string text, int index, string word)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        if (index + word.Length > text.Length)
            return false;
        if (string.Compare(text, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;
        // CJK has no word boundaries; for Latin words require the match to end the word.
        if (IsCjk(word[0]))
            return true;
        return index + word.Length == text.Length || !char.IsLetter(text[index + word.Length]);
    }

    private static bool IsCjk(char value) => value >= '⺀';

    private static bool TryResolveAbilityValue(
        TCardBase template,
        string abilityId,
        out string valueText,
        out string? unit
    )
    {
        valueText = string.Empty;
        unit = null;
        var abilities = template.GetType().GetProperty("Abilities")?.GetValue(template);
        if (abilities is not IEnumerable enumerable)
            return false;

        foreach (var entry in enumerable)
        {
            if (!TryReadEntry(entry, out var key, out var ability))
                continue;
            if (!string.Equals(key?.ToString(), abilityId, StringComparison.Ordinal))
                continue;

            var action = ability?.GetType().GetProperty("Action")?.GetValue(ability);
            var value = action?.GetType().GetProperty("Value")?.GetValue(action);
            var scalar = value?.GetType().GetProperty("Value")?.GetValue(value);
            if (TryFormatScalar(scalar, out valueText))
            {
                unit = ResolveAttributeUnit(action);
                return true;
            }

            // Deal-card actions carry the count in their spawn limit
            // ("Get {ability.N} Loot items").
            var spawnContext = action?.GetType().GetProperty("SpawnContext")?.GetValue(action);
            var limit = spawnContext?.GetType().GetProperty("Limit")?.GetValue(spawnContext);
            var limitScalar = limit?.GetType().GetProperty("Value")?.GetValue(limit);
            if (TryFormatScalar(limitScalar, out valueText))
                return true;

            // Apply-style actions ("TActionPlayerRegenApply") carry no value; the
            // amount lives in the card's own Attributes under "<X>ApplyAmount".
            return TryResolveFromCardAttributes(template, action, out valueText, out unit);
        }

        return false;
    }

    private static bool TryResolveFromCardAttributes(
        TCardBase template,
        object? action,
        out string valueText,
        out string? unit
    )
    {
        valueText = string.Empty;
        unit = null;
        var actionName = action?.GetType().Name;
        if (actionName == null)
            return false;

        const string prefixPlayer = "TActionPlayer";
        const string prefixCard = "TActionCard";
        var core = actionName.StartsWith(prefixPlayer, StringComparison.Ordinal)
            ? actionName[prefixPlayer.Length..]
            : actionName.StartsWith(prefixCard, StringComparison.Ordinal)
                ? actionName[prefixCard.Length..]
                : null;
        if (string.IsNullOrEmpty(core))
            return false;

        var attributeKey = core + "Amount"; // e.g. RegenApply -> RegenApplyAmount
        var attributes = template.GetType().GetProperty("Attributes")?.GetValue(template);
        if (attributes is not IEnumerable attributeEntries)
            return false;

        foreach (var entry in attributeEntries)
        {
            if (!TryReadEntry(entry, out var key, out var attributeValue))
                continue;
            if (!string.Equals(key?.ToString(), attributeKey, StringComparison.Ordinal))
                continue;
            if (!TryFormatScalar(attributeValue, out valueText))
                return false;
            unit = NormalizeAttributeUnit(attributeKey);
            return true;
        }

        return false;
    }

    // The game renders "{ability.0}" with attribute-specific styling that conveys the
    // unit; plain text needs the word spelled out. Only well-known keyword attributes
    // are appended (they also pick up native keyword coloring in the tooltip); things
    // like Quest_1 stay silent because the surrounding text already carries the context.
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

    private static readonly Dictionary<string, string[]> UnitAliases = new(StringComparer.Ordinal)
    {
        ["Experience"] = new[] { "XP" },
    };

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

    private static bool TryFormatScalar(object? scalar, out string valueText)
    {
        valueText = string.Empty;
        switch (scalar)
        {
            case null:
                return false;
            case float value:
                valueText = FormatNumber(value);
                return true;
            case double value:
                valueText = FormatNumber(value);
                return true;
            case decimal value:
                valueText = FormatNumber((double)value);
                return true;
            case int value:
                valueText = value.ToString(CultureInfo.InvariantCulture);
                return true;
            case long value:
                valueText = value.ToString(CultureInfo.InvariantCulture);
                return true;
            default:
                valueText = scalar.ToString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(valueText);
        }
    }

    private static string FormatNumber(double value)
    {
        var rounded = Math.Round(value);
        return Math.Abs(value - rounded) < 0.0001
            ? rounded.ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
