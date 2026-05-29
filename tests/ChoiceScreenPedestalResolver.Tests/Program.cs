using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Combat;
using BazaarGameShared.Domain.Cards.Encounter.Pedestal;
using BazaarGameShared.Domain.Cards.Encounter.Pedestal.Behaviors;
using BazaarPlusPlus.Game.Encounter;
using BazaarPlusPlus.Core.GameState;

var upgradeGuid = Guid.NewGuid();
var enchantGuid = Guid.NewGuid();
var enchantRandomGuid = Guid.NewGuid();
var transformGuid = Guid.NewGuid();
var combatGuid = Guid.NewGuid();
var missingGuid = Guid.NewGuid();

var templates = new Dictionary<Guid, ITCard>
{
    [upgradeGuid] = new TCardEncounterPedestal { Behavior = new TPedestalBehaviorUpgrade() },
    [enchantGuid] = new TCardEncounterPedestal { Behavior = new TPedestalBehaviorEnchant() },
    [enchantRandomGuid] = new TCardEncounterPedestal
    {
        Behavior = new TPedestalBehaviorEnchantRandom(),
    },
    [transformGuid] = new TCardEncounterPedestal { Behavior = new TPedestalBehaviorTransform() },
    [combatGuid] = new TCardEncounterCombat(),
};

ITCard? Lookup(Guid id) => templates.TryGetValue(id, out var card) ? card : null;

AssertEqual(
    ChoiceScreenPedestalKind.None,
    ChoiceScreenPedestalResolver.Resolve(null, Lookup),
    "null SelectionSet should resolve to None."
);

AssertEqual(
    ChoiceScreenPedestalKind.None,
    ChoiceScreenPedestalResolver.Resolve(Array.Empty<string>(), Lookup),
    "Empty SelectionSet should resolve to None."
);

AssertEqual(
    ChoiceScreenPedestalKind.Upgrade,
    ChoiceScreenPedestalResolver.Resolve(new[] { upgradeGuid.ToString() }, Lookup),
    "Single upgrade pedestal should resolve to Upgrade."
);

AssertEqual(
    ChoiceScreenPedestalKind.Enchant,
    ChoiceScreenPedestalResolver.Resolve(new[] { enchantGuid.ToString() }, Lookup),
    "Single enchant pedestal should resolve to Enchant."
);

AssertEqual(
    ChoiceScreenPedestalKind.Enchant,
    ChoiceScreenPedestalResolver.Resolve(new[] { enchantRandomGuid.ToString() }, Lookup),
    "Single enchant-random pedestal should resolve to Enchant."
);

AssertEqual(
    ChoiceScreenPedestalKind.None,
    ChoiceScreenPedestalResolver.Resolve(new[] { transformGuid.ToString() }, Lookup),
    "Transform pedestal should resolve to None."
);

AssertEqual(
    ChoiceScreenPedestalKind.None,
    ChoiceScreenPedestalResolver.Resolve(new[] { "not-a-guid", "" }, Lookup),
    "Non-Guid entries should be skipped (and yield None when nothing else matches)."
);

AssertEqual(
    ChoiceScreenPedestalKind.Upgrade,
    ChoiceScreenPedestalResolver.Resolve(new[] { "not-a-guid", upgradeGuid.ToString() }, Lookup),
    "Non-Guid entries should be skipped without affecting later matches."
);

AssertEqual(
    ChoiceScreenPedestalKind.None,
    ChoiceScreenPedestalResolver.Resolve(new[] { missingGuid.ToString() }, Lookup),
    "Unknown Guid (lookup returns null) should be skipped."
);

AssertEqual(
    ChoiceScreenPedestalKind.Enchant,
    ChoiceScreenPedestalResolver.Resolve(
        new[] { missingGuid.ToString(), enchantGuid.ToString() },
        Lookup
    ),
    "Missing lookup result should not stop later entries from being classified."
);

AssertEqual(
    ChoiceScreenPedestalKind.Upgrade,
    ChoiceScreenPedestalResolver.Resolve(
        new[] { combatGuid.ToString(), upgradeGuid.ToString() },
        Lookup
    ),
    "Combat encounter mixed with pedestal should resolve to the pedestal's kind."
);

AssertEqual(
    ChoiceScreenPedestalKind.Upgrade,
    ChoiceScreenPedestalResolver.Resolve(
        new[] { upgradeGuid.ToString(), upgradeGuid.ToString() },
        Lookup
    ),
    "Two pedestals of the same kind should resolve to that kind."
);

Console.WriteLine("ChoiceScreenPedestalResolver checks passed.");

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
}
