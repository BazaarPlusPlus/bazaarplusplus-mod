#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Pedestal;
using BazaarGameShared.Domain.Cards.Encounter.Pedestal.Behaviors;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.Encounter;

/// <summary>The kind of pedestal offered on the choice screen plus, for enchant
/// pedestals, the enchant type name(s) the offer would apply. The choice screen can
/// list several pedestals at once, so the names are the union across every offered
/// enchant pedestal (a fixed pedestal contributes its one type, a random one its
/// whole pool). Names are strings so the Core snapshot that stores them stays free
/// of the game's <c>EEnchantmentType</c>.</summary>
internal readonly struct ChoiceScreenPedestalResult
{
    public ChoiceScreenPedestalKind Kind { get; init; }
    public IReadOnlyList<string> EnchantmentTypeNames { get; init; }

    public static ChoiceScreenPedestalResult None { get; } =
        new() { Kind = ChoiceScreenPedestalKind.None, EnchantmentTypeNames = Array.Empty<string>() };
}

internal static class ChoiceScreenPedestalResolver
{
    internal static ChoiceScreenPedestalKind Resolve(
        IReadOnlyList<string>? selectionSet,
        Func<Guid, ITCard?> templateLookup
    ) => ResolveDetailed(selectionSet, templateLookup).Kind;

    internal static ChoiceScreenPedestalResult ResolveDetailed(
        IReadOnlyList<string>? selectionSet,
        Func<Guid, ITCard?> templateLookup
    )
    {
        if (selectionSet == null || selectionSet.Count == 0)
            return ChoiceScreenPedestalResult.None;

        if (templateLookup == null)
            throw new ArgumentNullException(nameof(templateLookup));

        // The choice screen can offer several pedestals at once. Take the first
        // non-None pedestal's kind (a SelectionSet historically never mixes upgrade
        // and enchant), but aggregate enchant type names across EVERY offered enchant
        // pedestal so the preview can match all of them, not just the first.
        var kind = ChoiceScreenPedestalKind.None;
        var enchantNames = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in selectionSet)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            if (!Guid.TryParse(id, out var guid))
                continue;
            if (templateLookup(guid) is not TCardEncounterPedestal pedestal)
                continue;

            var entryKind = ClassifyKind(pedestal.Behavior);
            if (kind == ChoiceScreenPedestalKind.None && entryKind != ChoiceScreenPedestalKind.None)
                kind = entryKind;

            foreach (var name in ExtractEnchantNames(pedestal.Behavior))
            {
                if (seenNames.Add(name))
                    enchantNames.Add(name);
            }
        }

        if (kind == ChoiceScreenPedestalKind.None)
            return ChoiceScreenPedestalResult.None;

        return new ChoiceScreenPedestalResult
        {
            Kind = kind,
            EnchantmentTypeNames =
                enchantNames.Count == 0 ? Array.Empty<string>() : enchantNames.ToArray(),
        };
    }

    private static ChoiceScreenPedestalKind ClassifyKind(ITPedestalBehavior? behavior) =>
        behavior switch
        {
            TPedestalBehaviorUpgrade => ChoiceScreenPedestalKind.Upgrade,
            TPedestalBehaviorEnchant => ChoiceScreenPedestalKind.Enchant,
            TPedestalBehaviorEnchantRandom => ChoiceScreenPedestalKind.Enchant,
            _ => ChoiceScreenPedestalKind.None,
        };

    private static IEnumerable<string> ExtractEnchantNames(ITPedestalBehavior? behavior)
    {
        switch (behavior)
        {
            case TPedestalBehaviorEnchant fixedEnchant:
                yield return fixedEnchant.Enchantment.ToString();
                break;
            case TPedestalBehaviorEnchantRandom randomEnchant
                when randomEnchant.Enchantments != null:
                foreach (var entry in randomEnchant.Enchantments)
                    yield return entry.Enchantment.ToString();
                break;
        }
    }
}
