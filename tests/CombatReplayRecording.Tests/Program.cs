#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;

ReplayPersistenceStateIsScopedPerRun();
ReplayPayloadStoreRoundTripsAndRejectsCorruption();
CapturedReplayRoutesSeparatePveFromPvpPersistence();
CurrentNativeReplayBecomesReadyWithoutPersistence();
PvpBattleStoreRejectsPveManifests();

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

static void CapturedReplayRoutesSeparatePveFromPvpPersistence()
{
    Assert(
        CapturedReplayRouter.Resolve(
            new PvpBattleManifest { BattleId = "pve", CombatKind = "Combat" }
        ) == CapturedReplayRoute.CurrentNative,
        "PvE captures must stay on the in-memory current-native route."
    );
    Assert(
        CapturedReplayRouter.Resolve(
            new PvpBattleManifest { BattleId = "pvp", CombatKind = "PVPCombat" }
        ) == CapturedReplayRoute.PersistedPvp,
        "PvP captures must use the persisted catalog route."
    );
}

static void CurrentNativeReplayBecomesReadyWithoutPersistence()
{
    var state = new CurrentReplayRecordingState();
    state.LatchBattle("pve");
    state.EnterReplayState();
    state.SetAvailability(ready: true, reason: null);
    Assert(!state.Snapshot().CanStart, "An unrouted capture must not be recordable.");

    state.MarkCurrentNativeReady("pve");
    var snapshot = state.Snapshot();
    Assert(snapshot.CanStart, "A routed current-native capture should be recordable.");
    Assert(
        snapshot.Phase == CurrentReplayRecordingPhase.Ready,
        "The current-native route should transition directly to ready."
    );
}

static void PvpBattleStoreRejectsPveManifests()
{
    var path = Path.Combine(Path.GetTempPath(), $"bpp-pvp-route-{Guid.NewGuid():N}.db");
    var store = new PvpBattleSqliteStore(path);
    try
    {
        store.Save(new PvpBattleManifest { BattleId = "pve", CombatKind = "Combat" });
        throw new InvalidOperationException("The PvP store accepted a PvE manifest.");
    }
    catch (ArgumentException)
    {
        // Expected: routing regressions must fail instead of reporting false persistence success.
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
