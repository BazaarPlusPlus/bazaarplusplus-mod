#nullable enable
using System;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.GameInterop.Encounter;

namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// BazaarPlusPlus-side implementation of <see cref="IBazaarAgentGameProbe"/>. Wraps the
/// internal encounter probe + type resolver and a replay-activity delegate, exposing only
/// the public snapshot DTOs across the assembly boundary.
/// </summary>
internal sealed class BazaarAgentGameProbe : IBazaarAgentGameProbe
{
    private readonly IEncounterStateProbe _encounterState;
    private readonly Func<bool> _isReplayStartInProgress;

    public BazaarAgentGameProbe(
        IEncounterStateProbe encounterState,
        Func<bool> isReplayStartInProgress
    )
    {
        _encounterState = encounterState ?? throw new ArgumentNullException(nameof(encounterState));
        _isReplayStartInProgress =
            isReplayStartInProgress
            ?? throw new ArgumentNullException(nameof(isReplayStartInProgress));
    }

    public EncounterIdsSnapshot GetEncounterIds() => _encounterState.GetEncounterIds();

    public EncounterTargetingSnapshot GetTargetingState() => _encounterState.GetTargetingState();

    public string? ResolveEncounterType(string? encounterId) =>
        EncounterTypeResolver.Resolve(encounterId);

    public bool IsReplayStartInProgress() => _isReplayStartInProgress();
}
