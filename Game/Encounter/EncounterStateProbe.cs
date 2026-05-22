#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Core.GameState;
using TheBazaar;

namespace BazaarPlusPlus.Game.Encounter;

/// <summary>Aggregates the three encounter-state probes into a single snapshot
/// consumers can pull on demand. One <c>PedestalState.ValidateCards()</c>
/// invocation per call, only when the player is currently on a pedestal.</summary>
internal sealed class EncounterStateProbe : IEncounterStateProbe
{
    public EncounterStateSnapshot GetCurrent()
    {
        var runState = Data.CurrentState;
        var appState = AppState.CurrentState;

        var currentEncounterId = runState?.CurrentEncounterId;
        var currentEncounterType = EncounterTypeResolver.Resolve(currentEncounterId);

        var filter = InteractionFilterProbe.ReadCurrentFilter();

        var pedestalEligible = appState is PedestalState ped
            ? PedestalEligibilityProbe.ReadEligibleInstanceIds(ped)
            : new HashSet<string>();

        return new EncounterStateSnapshot
        {
            CurrentEncounterId = currentEncounterId,
            CurrentEncounterType = currentEncounterType,
            InteractionFilterTemplateIds = filter,
            PedestalEligibleInstanceIds = pedestalEligible,
        };
    }
}
