#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Pedestal;
using BazaarGameShared.Domain.Cards.Encounter.Pedestal.Behaviors;

namespace BazaarPlusPlus.Game.Encounter;

internal static class ChoiceScreenPedestalResolver
{
    internal static ChoiceScreenPedestalKind Resolve(
        IReadOnlyList<string>? selectionSet,
        Func<Guid, ITCard?> templateLookup
    )
    {
        if (selectionSet == null || selectionSet.Count == 0)
            return ChoiceScreenPedestalKind.None;

        if (templateLookup == null)
            throw new ArgumentNullException(nameof(templateLookup));

        foreach (var id in selectionSet)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            if (!Guid.TryParse(id, out var guid))
                continue;
            if (templateLookup(guid) is not TCardEncounterPedestal pedestal)
                continue;

            var kind = ClassifyBehavior(pedestal.Behavior);
            if (kind != ChoiceScreenPedestalKind.None)
                return kind;
        }

        return ChoiceScreenPedestalKind.None;
    }

    private static ChoiceScreenPedestalKind ClassifyBehavior(ITPedestalBehavior? behavior) =>
        behavior switch
        {
            TPedestalBehaviorUpgrade => ChoiceScreenPedestalKind.Upgrade,
            TPedestalBehaviorEnchant => ChoiceScreenPedestalKind.Enchant,
            TPedestalBehaviorEnchantRandom => ChoiceScreenPedestalKind.Enchant,
            _ => ChoiceScreenPedestalKind.None,
        };
}
