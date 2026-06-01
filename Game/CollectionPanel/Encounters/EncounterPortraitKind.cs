#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.Encounters;

/// <summary>
/// The two curated kinds of shopkeeper-style <c>EventEncounter</c> we render portraits for.
/// Both are <c>EventEncounter</c> cards in the game data; a merchant additionally carries the
/// <c>ECardTag.Merchant</c> tag (sells items), while a trainer does not (teaches/sells skills).
/// </summary>
internal enum EncounterPortraitKind
{
    Merchant,
    Trainer,
}
