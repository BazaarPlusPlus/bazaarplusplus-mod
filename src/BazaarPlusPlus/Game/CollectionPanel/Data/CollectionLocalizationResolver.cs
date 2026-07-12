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
internal static partial class CollectionLocalizationResolver
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

    public static IReadOnlyList<string> ResolveTitleSearchTexts(TCardBase template) =>
        SearchTexts(template, template.Localization?.Title, formatAbilityPlaceholders: false);

    public static IReadOnlyList<string> ResolveDescriptionSearchTexts(TCardBase template) =>
        SearchTexts(template, template.Localization?.Description, formatAbilityPlaceholders: true);

    public static IReadOnlyList<string> ResolveTooltipSearchTexts(TCardBase template)
    {
        var tooltips = template.Localization?.Tooltips;
        if (tooltips == null || tooltips.Count == 0)
            return Array.Empty<string>();

        var values = new List<string>(tooltips.Count * 3);
        foreach (var tooltip in tooltips)
            AddSearchTexts(values, template, tooltip?.Content, formatAbilityPlaceholders: true);
        return values;
    }

    private static IReadOnlyList<string> SearchTexts(
        TCardBase template,
        TLocalizableText? text,
        bool formatAbilityPlaceholders
    )
    {
        if (text == null)
            return Array.Empty<string>();

        var values = new List<string>(3);
        AddSearchTexts(values, template, text, formatAbilityPlaceholders);
        return values;
    }

    private static void AddSearchTexts(
        List<string> values,
        TCardBase template,
        TLocalizableText? text,
        bool formatAbilityPlaceholders
    )
    {
        if (text == null)
            return;

        AddSearchText(values, TryGetLocalizedText(text), template, formatAbilityPlaceholders);
        AddSearchText(values, text.Text, template, formatAbilityPlaceholders);
    }

    private static string? PickText(TLocalizableText text)
    {
        var localized = TryGetLocalizedText(text);
        if (!string.IsNullOrWhiteSpace(localized))
            return localized;

        if (!string.IsNullOrWhiteSpace(text.Text))
            return text.Text;
        if (!string.IsNullOrWhiteSpace(text.Key))
            return text.Key;
        return null;
    }

    private static string? TryGetLocalizedText(TLocalizableText text)
    {
        try
        {
            return TheBazaar.Tooltips.TooltipExtensions.GetLocalizedText(text);
        }
        catch (Exception)
        {
            // Localization service unavailable (unit tests / early startup):
            // fall back to the authored text below.
            return null;
        }
    }

    private static void AddSearchText(
        List<string> values,
        string? text,
        TCardBase template,
        bool formatAbilityPlaceholders
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var value = formatAbilityPlaceholders ? FormatAbilityPlaceholders(template, text) : text;
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var existing in values)
            if (string.Equals(existing, value, StringComparison.Ordinal))
                return;

        values.Add(value!);
    }

    // Installed at plugin startup (Plugin.InstallStaticUtilities): maps a canonical attribute keyword
    // ("Heal") to the game's localized display word ("治疗" on zh clients) via
    // TooltipTypography. Null (or a null return) falls back to the English name so
    // the data layer never depends on game UI services directly.
    internal static Func<string, string?>? AttributeUnitLocalizer = null;

    private delegate bool AbilityValueResolver(
        string abilityId,
        out string valueText,
        out string? unit
    );

    private static string? FormatAbilityPlaceholders(TCardBase template, string? text) =>
        FormatAbilityPlaceholders(
            text,
            (string abilityId, out string valueText, out string? unit) =>
                TryResolveAbilityValue(template, abilityId, out valueText, out unit)
        );

    private static string? FormatAbilityPlaceholders(
        string? text,
        AbilityValueResolver resolveAbilityValue
    )
    {
        if (
            string.IsNullOrWhiteSpace(text) || !text.Contains("{ability.", StringComparison.Ordinal)
        )
            return text;

        var formatted = Regex.Replace(
            text!,
            @"\{ability\.([^}]+)\}",
            match =>
            {
                // Unresolvable placeholders (e.g. live-computed totals) degrade to
                // nothing rather than leaking raw tokens; leftover empty brackets
                // are cleaned afterwards.
                if (
                    !resolveAbilityValue(match.Groups[1].Value, out var value, out var unit)
                )
                    return string.Empty;
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
                if (
                    FollowingWordEquals(text!, after, unit)
                    || FollowingWordEquals(text!, after, localizedUnit!)
                )
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
        return CleanDroppedPlaceholders(formatted);
    }

    // After dropping unresolvable placeholders, remove the empty bracket pairs and
    // doubled spaces they leave behind ("... you have [{ability.0}]" -> "... you have").
    private static string CleanDroppedPlaceholders(string text)
    {
        text = text.Replace("[]", string.Empty)
            .Replace("[ ]", string.Empty)
            .Replace("()", string.Empty)
            .Replace("（）", string.Empty);
        var builder = new System.Text.StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var character in text)
        {
            var isSpace = character == ' ';
            if (isSpace && previousWasSpace)
                continue;
            previousWasSpace = isSpace;
            builder.Append(character);
        }
        return builder.ToString().TrimEnd();
    }

    private static bool FollowingWordEquals(string text, int index, string word)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        if (index + word.Length > text.Length)
            return false;
        if (
            string.Compare(text, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase)
            != 0
        )
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

        // Placeholder ids may carry accessor suffixes: "{ability.0.mod}" refers to
        // the value's Modifier ("Gain {ability.0.mod} Gold for each ..."), while the
        // bare id on such computed values is the live total and stays unresolved.
        var dot = abilityId.IndexOf('.');
        var baseAbilityId = dot > 0 ? abilityId[..dot] : abilityId;
        var accessor = dot > 0 ? abilityId[(dot + 1)..] : null;

        foreach (var entry in enumerable)
        {
            if (!TryReadEntry(entry, out var key, out var ability))
                continue;
            if (!string.Equals(key?.ToString(), baseAbilityId, StringComparison.Ordinal))
                continue;

            var action = ability?.GetType().GetProperty("Action")?.GetValue(ability);
            var value = action?.GetType().GetProperty("Value")?.GetValue(action);

            if (string.Equals(accessor, "mod", StringComparison.OrdinalIgnoreCase))
            {
                var modifier = value?.GetType().GetProperty("Modifier")?.GetValue(value);
                var modifierValue = modifier?.GetType().GetProperty("Value")?.GetValue(modifier);
                var modifierScalar = modifierValue
                    ?.GetType()
                    .GetProperty("Value")
                    ?.GetValue(modifierValue);
                return TryFormatScalar(modifierScalar, out valueText);
            }

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
        var core =
            actionName.StartsWith(prefixPlayer, StringComparison.Ordinal)
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
