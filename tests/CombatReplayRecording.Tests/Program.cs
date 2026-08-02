#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;

ReplayPersistenceStateIsScopedPerRun();
ReplayPayloadStoreRoundTripsAndRejectsCorruption();

Console.WriteLine("Combat replay recording tests passed.");

static void ReplayPersistenceStateIsScopedPerRun()
{
    ReplayPersistenceStateTracker.Enqueued("run-a");
    ReplayPersistenceStateTracker.Enqueued("run-a");
    ReplayPersistenceStateTracker.Enqueued("run-b");
    Assert(ReplayPersistenceStateTracker.HasPending("run-a"), "run-a should be pending.");
    Assert(ReplayPersistenceStateTracker.HasPending("run-b"), "run-b should be pending.");

    ReplayPersistenceStateTracker.Completed("run-a");
    Assert(ReplayPersistenceStateTracker.HasPending("run-a"), "one run-a write remains.");
    Assert(ReplayPersistenceStateTracker.HasPending("run-b"), "run-b must be independent.");
    ReplayPersistenceStateTracker.Completed("run-a");
    Assert(!ReplayPersistenceStateTracker.HasPending("run-a"), "run-a should drain.");
    Assert(ReplayPersistenceStateTracker.HasPending("run-b"), "run-b must remain pending.");
    ReplayPersistenceStateTracker.Completed("run-b");
    Assert(!ReplayPersistenceStateTracker.HasPending("run-b"), "run-b should drain.");
}

static void ReplayPayloadStoreRoundTripsAndRejectsCorruption()
{
    var root = Path.Combine(Path.GetTempPath(), $"bpp-replay-v5-{Guid.NewGuid():N}");
    try
    {
        var store = new CombatReplayPayloadStore(root);
        var payload = new PvpReplayPayload
        {
            BattleId = "battle-one",
            Version = 1,
            SpawnMessageBytes = [1, 2],
            CombatMessageBytes = [3, 4],
            DespawnMessageBytes = [5, 6],
        };
        store.Save(payload);
        var loaded = store.LoadDetailed(payload.BattleId);
        Assert(loaded.Status == FileBackedPayloadLoadStatus.Loaded, "Saved replay must load.");
        Assert(
            loaded.Payload?.CombatMessageBytes.SequenceEqual(payload.CombatMessageBytes) == true,
            "Replay bytes must round-trip exactly."
        );

        var path = Directory.EnumerateFiles(root).Single();
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        var corrupt = store.LoadDetailed(payload.BattleId);
        Assert(
            corrupt.Status == FileBackedPayloadLoadStatus.Invalid,
            "Corrupt replay files must be classified as invalid."
        );
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
