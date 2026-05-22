#nullable enable

namespace BazaarPlusPlus.Core.GameState;

internal interface IEncounterStateProbe
{
    /// <summary>Main thread only. Returns a fresh snapshot of the current encounter
    /// situation. Never throws — failures degrade individual fields to their
    /// empty values.</summary>
    EncounterStateSnapshot GetCurrent();
}
