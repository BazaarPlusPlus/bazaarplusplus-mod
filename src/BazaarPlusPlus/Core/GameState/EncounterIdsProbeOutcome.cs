#nullable enable
using System;

namespace BazaarPlusPlus.Core.GameState;

internal readonly record struct EncounterIdsProbeOutcome(
    bool IsSuccess,
    EncounterIdsSnapshot Snapshot,
    Exception? Exception
)
{
    internal static EncounterIdsProbeOutcome Success(EncounterIdsSnapshot snapshot) =>
        new(true, snapshot, null);

    internal static EncounterIdsProbeOutcome Failure(Exception exception) =>
        new(false, EncounterIdsSnapshot.Empty, exception);
}
