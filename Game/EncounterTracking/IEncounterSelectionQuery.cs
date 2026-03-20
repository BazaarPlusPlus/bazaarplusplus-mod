#nullable enable
namespace BazaarPlusPlus.Game.EncounterTracking;

internal interface IEncounterSelectionQuery
{
    EncounterSelectionSnapshot GetSnapshot();
}
