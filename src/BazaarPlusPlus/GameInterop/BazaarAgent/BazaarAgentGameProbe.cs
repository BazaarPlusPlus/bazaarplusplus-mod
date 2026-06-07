#nullable enable
using System;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop.Encounter;

namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// BazaarPlusPlus-side implementation of <see cref="IBazaarAgentGameProbe"/>. Wraps the
/// internal encounter probe + type resolver, exposing only the public snapshot DTOs across
/// the assembly boundary. Replay state reads live on <see cref="IBazaarAgentReplayRecorder"/>.
/// </summary>
internal sealed class BazaarAgentGameProbe : IBazaarAgentGameProbe
{
    private readonly IEncounterStateProbe _encounterState;

    public BazaarAgentGameProbe(IEncounterStateProbe encounterState)
    {
        _encounterState = encounterState ?? throw new ArgumentNullException(nameof(encounterState));
    }

    public EncounterIdsSnapshot GetEncounterIds() => _encounterState.GetEncounterIds();

    public EncounterTargetingSnapshot GetTargetingState() => _encounterState.GetTargetingState();

    public string? ResolveEncounterType(string? encounterId) =>
        EncounterTypeResolver.Resolve(encounterId);
}
