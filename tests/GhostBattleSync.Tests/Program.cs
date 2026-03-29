#nullable enable
using System.Reflection;

var syncServiceType = RequireType("BazaarPlusPlus.Game.HistoryPanel.GhostBattleSyncService");
var shouldAdvanceCheckpoint = syncServiceType.GetMethod(
    "ShouldAdvanceCheckpoint",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    shouldAdvanceCheckpoint != null,
    "GhostBattleSyncService should expose checkpoint advancement logic."
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
    !(bool)
        shouldAdvanceCheckpoint!.Invoke(
            null,
            [
                12,
                200,
                14,
            ]
        )!,
    "Ghost sync should not advance the checkpoint when the requested lookback is clamped to the maximum window."
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
