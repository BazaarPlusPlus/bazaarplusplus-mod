#nullable enable
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// The narrow, public game-interop surface that the separate-assembly BazaarAgent host
/// plugin consumes from BazaarPlusPlus. It exposes only the encounter reads and the
/// replay-activity signal the agent context reader needs — everything else in
/// BazaarPlusPlus stays <c>internal</c>. BazaarPlusPlus does not reference the agent
/// module; it merely publishes this facade via <see cref="BazaarAgentGameBridge"/>.
/// </summary>
public interface IBazaarAgentGameProbe
{
    /// <summary>Main thread only. Lightweight read of the current/choice-screen encounter ids.</summary>
    EncounterIdsSnapshot GetEncounterIds();

    /// <summary>Main thread only. Reads target-selection legality state.</summary>
    EncounterTargetingSnapshot GetTargetingState();

    /// <summary>Resolves the encounter type (merchant/trainer/event/...) for an encounter id,
    /// or <c>null</c> when it cannot be classified.</summary>
    string? ResolveEncounterType(string? encounterId);

    /// <summary>True while a combat-replay start is in progress (used to defer replay auto-advance).</summary>
    bool IsReplayStartInProgress();
}
