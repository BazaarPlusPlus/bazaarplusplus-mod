#nullable enable
using System.Reflection;

var syncServiceType = RequireType("BazaarPlusPlus.Game.HistoryPanel.GhostBattleSyncService");
var shouldAdvanceCheckpoint = syncServiceType.GetMethod(
    "ShouldAdvanceCheckpoint",
    BindingFlags.NonPublic | BindingFlags.Static
);
var shouldTreatGhostErrorAsBindingFailure = syncServiceType.GetMethod(
    "ShouldTreatGhostErrorAsBindingFailure",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    shouldAdvanceCheckpoint != null,
    "GhostBattleSyncService should expose checkpoint advancement logic."
);
Assert(
    shouldTreatGhostErrorAsBindingFailure != null,
    "GhostBattleSyncService should expose binding-related ghost error classification logic."
);

Assert(
    !(bool)
        shouldAdvanceCheckpoint!.Invoke(
            null,
            [
                200,
                200,
                3,
            ]
        )!,
    "Ghost sync should not advance the checkpoint when the returned batch hits the limit."
);

Assert(
    (bool)
        shouldAdvanceCheckpoint!.Invoke(
            null,
            [
                12,
                200,
                14,
            ]
        )!,
    "Ghost sync should advance the checkpoint after a non-truncated fetch even when the requested window was clamped."
);

Assert(
    (bool)
        shouldAdvanceCheckpoint!.Invoke(
            null,
            [
                12,
                200,
                3,
            ]
        )!,
    "Ghost sync should advance the checkpoint after a non-truncated incremental fetch."
);

Assert(
    (bool)
        shouldTreatGhostErrorAsBindingFailure!.Invoke(
            null,
            ["http_403:{\"error\":\"battle_forbidden\"}"]
        )!,
    "Ghost replay/link failures that report battle_forbidden should be attributed to binding when bind just failed."
);

Assert(
    !(bool)
        shouldTreatGhostErrorAsBindingFailure!.Invoke(
            null,
            ["http_404:{\"error\":\"battle_not_found\"}"]
        )!,
    "Non-binding ghost request failures should keep their original error."
);

Console.WriteLine("Ghost battle sync checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
