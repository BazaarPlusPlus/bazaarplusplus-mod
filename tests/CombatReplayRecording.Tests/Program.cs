using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

var payloadStoreType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayPayloadStore");
var captureServiceType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayCaptureService");
var loaderType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayLoader");
var controllerType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayController");
var artifactType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureArtifact");
var manifestType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifest");
var payloadType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayload");
var candidateType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplaySequenceCandidate");
var cardSetCaptureType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCardSetCapture");
var captureStatusType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureStatus");
var captureSourceType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureSource");
var matcherType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSequenceMatcher");
var collectorType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSnapshotCollector");
var manifestFactoryType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifestFactory");
var payloadFactoryType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayloadFactory");
var catalogType = RequireType("BazaarPlusPlus.Game.PvpBattles.Persistence.PvpBattleCatalog");
var catalogInterfaceType = RequireType(
    "BazaarPlusPlus.Game.PvpBattles.Persistence.IPvpBattleCatalog"
);
var catalogStoreType = RequireType(
    "BazaarPlusPlus.Game.PvpBattles.Persistence.PvpBattleSqliteStore"
);

Assert(
    Type.GetType("BazaarPlusPlus.Game.CombatReplay.CombatReplayRecord, BazaarPlusPlus") == null,
    "CombatReplayRecord should be removed once battle-first replay storage is in place."
);
Assert(
    Type.GetType("BazaarPlusPlus.Game.CombatReplay.CombatReplayStore, BazaarPlusPlus") == null,
    "CombatReplayStore should be removed once payload storage is battle-first."
);
Assert(
    Type.GetType("BazaarPlusPlus.Game.CombatReplay.PvpBattleSqliteStore, BazaarPlusPlus") == null,
    "The legacy CombatReplay.PvpBattleSqliteStore bridge should be removed."
);

Assert(manifestType.GetProperty("BattleId") != null, "Manifest should expose BattleId.");
Assert(manifestType.GetProperty("ReplayId") == null, "Manifest should not expose ReplayId.");
Assert(payloadType.GetProperty("BattleId") != null, "Replay payload should expose BattleId.");
Assert(
    cardSetCaptureType.GetProperty("Items") != null
        && cardSetCaptureType.GetProperty("Status") != null
        && cardSetCaptureType.GetProperty("Source") != null,
    "PvpBattleCardSetCapture should expose Items, Status, and Source."
);
Assert(
    matcherType != null
        && collectorType != null
        && manifestFactoryType != null
        && payloadFactoryType != null,
    "The PVP battle matcher, collector, and factories should exist."
);
Assert(
    catalogType.GetMethod("Save") != null
        && catalogType.GetMethod("TryLoad") != null
        && catalogType.GetMethod("ListRecentBattles") != null
        && catalogInterfaceType != null
        && catalogStoreType != null,
    "The PVP battle catalog read side should expose Save, TryLoad, and ListRecentBattles."
);
Assert(
    catalogType.GetMethod("Delete") != null
        && catalogType.GetMethod("ListBattleIds") != null
        && catalogInterfaceType.GetMethod("Delete") != null
        && catalogInterfaceType.GetMethod("ListBattleIds") != null,
    "The PVP battle catalog should expose Delete and ListBattleIds so replay maintenance can reconcile stored manifests when needed."
);

var pathServiceSource = File.ReadAllText(
    Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../Core/Paths/BppPathService.cs")
    )
);
Assert(
    pathServiceSource.Contains("CombatReplayDirectoryPath", StringComparison.Ordinal),
    "BppPathService should expose a combat replay storage path."
);

var pluginSource = File.ReadAllText(
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Plugin.cs"))
);
Assert(
    pluginSource.Contains("AddComponent<CombatReplayRuntime>()", StringComparison.Ordinal),
    "Plugin should attach the combat replay runtime component."
);

var capturePatchPath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Patches/Combat/CombatReplayCapturePatch.cs"
    )
);
Assert(File.Exists(capturePatchPath), "Combat replay capture patch should exist.");
var capturePatchSource = File.ReadAllText(capturePatchPath);
Assert(
    capturePatchSource.Contains(
        "HarmonyPatch(typeof(NetMessageProcessor), \"ReceiveOrQueue\")",
        StringComparison.Ordinal
    ),
    "Combat replay capture should hook NetMessageProcessor.ReceiveOrQueue."
);
Assert(
    capturePatchSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal)
        && capturePatchSource.Contains("new NetMessageObserved", StringComparison.Ordinal),
    "Combat replay capture patch should publish observed messages through the runtime event bus."
);
Assert(
    capturePatchSource.Contains("NetMessageGameSim", StringComparison.Ordinal)
        && capturePatchSource.Contains("NetMessageCombatSim", StringComparison.Ordinal)
        && capturePatchSource.Contains("return;", StringComparison.Ordinal),
    "Combat replay capture patch should ignore non-GameSim and non-CombatSim messages before publishing."
);

var runtimeSource = File.ReadAllText(
    Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../Game/CombatReplay/CombatReplayRuntime.cs"
        )
    )
);
var payloadStoreSource = File.ReadAllText(
    Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../Game/CombatReplay/CombatReplayPayloadStore.cs"
        )
    )
);
var captureServiceSource = File.ReadAllText(
    Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../Game/CombatReplay/CombatReplayCaptureService.cs"
        )
    )
);
Assert(
    payloadStoreSource.Contains("Delete(string battleId)", StringComparison.Ordinal)
        && payloadStoreSource.Contains("ListBattleIds()", StringComparison.Ordinal),
    "Combat replay payload store should expose Delete(string battleId) and ListBattleIds() so orphan payload cleanup can enumerate and remove files."
);
Assert(
    captureServiceSource.Contains("CaptureLiveSnapshots(_candidate);", StringComparison.Ordinal)
        && captureServiceSource.Contains(
            "CaptureLiveSnapshots(candidate);",
            StringComparison.Ordinal
        ),
    "Combat replay capture should refresh live card snapshots after the opening GameSim has had time to populate Data."
);
Assert(
    captureServiceSource.Contains("PvpBattleSequenceMatcher", StringComparison.Ordinal)
        && captureServiceSource.Contains("PvpBattleSnapshotCollector", StringComparison.Ordinal)
        && captureServiceSource.Contains("PvpBattleManifestFactory", StringComparison.Ordinal)
        && captureServiceSource.Contains("PvpReplayPayloadFactory", StringComparison.Ordinal),
    "CombatReplayCaptureService should delegate battle assembly to the matcher, collector, and factories."
);
Assert(
    captureServiceSource.Contains("_matcher", StringComparison.Ordinal)
        && captureServiceSource.Contains("_collector", StringComparison.Ordinal)
        && captureServiceSource.Contains("_manifestFactory", StringComparison.Ordinal)
        && captureServiceSource.Contains("_payloadFactory", StringComparison.Ordinal),
    "CombatReplayCaptureService should assemble battle artifacts through the capture pipeline."
);
Assert(
    captureServiceSource.Contains("CreateBattleId", StringComparison.Ordinal),
    "Battle ids should be generated by PvpBattleManifestFactory."
);
Assert(
    captureServiceSource.Contains("CapturedEmpty", StringComparison.Ordinal)
        && captureServiceSource.Contains("LiveRetry", StringComparison.Ordinal)
        && captureServiceSource.Contains("OpeningMessage", StringComparison.Ordinal),
    "Capture pipeline should represent explicit capture status and source semantics."
);
Assert(
    captureServiceSource.Contains(
        "CaptureCurrentHandCardsAtOpening(ECombatantId.Player)",
        StringComparison.Ordinal
    )
        && captureServiceSource.Contains(
            "CaptureCurrentSkillsAtOpening(ECombatantId.Player)",
            StringComparison.Ordinal
        )
        && captureServiceSource.Contains(
            "CaptureOpeningHandCards(message, ECombatantId.Opponent)",
            StringComparison.Ordinal
        )
        && captureServiceSource.Contains(
            "CaptureOpponentSkillsFromOpening(message)",
            StringComparison.Ordinal
        )
        && captureServiceSource.Contains("GameSimEventCardSpawned", StringComparison.Ordinal)
        && captureServiceSource.Contains(
            "GameSimEventPlayerSkillEquipped",
            StringComparison.Ordinal
        ),
    "Combat replay capture should read player skills from current Data and opponent skills from opening GameSim events."
);
Assert(
    captureServiceSource.Contains("return state == ERunState.PVPCombat;", StringComparison.Ordinal)
        && captureServiceSource.Contains("IsAnyCombatOpeningMessage", StringComparison.Ordinal),
    "Combat replay capture should only open new PVP candidates from PVPCombat states and reject other combat openings as closers."
);
Assert(
    captureServiceSource.Contains("OpponentName = candidate.OpponentName", StringComparison.Ordinal)
        && captureServiceSource.Contains(
            "OpponentAccountId = candidate.OpponentAccountId",
            StringComparison.Ordinal
        ),
    "Combat replay capture should bind opponent identity to the opening candidate instead of reading it from global state during record creation."
);
Assert(
    runtimeSource.Contains("EnsureReplayBootstrapReadyAsync", StringComparison.Ordinal),
    "Combat replay runtime should expose a dedicated saved replay bootstrap entrypoint."
);
Assert(
    runtimeSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal)
        && runtimeSource.Contains("new PvpBattleRecorded", StringComparison.Ordinal),
    "Combat replay runtime should publish saved battle metadata through the event bus."
);
Assert(
    !runtimeSource.Contains("RunLoggingController.Instance", StringComparison.Ordinal),
    "Combat replay runtime should not call RunLoggingController directly."
);
Assert(
    runtimeSource.Contains("_battleCatalog = new PvpBattleCatalog", StringComparison.Ordinal)
        && runtimeSource.Contains(
            "_payloadStore = new CombatReplayPayloadStore",
            StringComparison.Ordinal
        )
        && runtimeSource.Contains(
            "_persistenceQueue = new CombatReplayPersistenceQueue",
            StringComparison.Ordinal
        ),
    "Combat replay runtime should construct the payload store, battle catalog, and a dedicated background persistence queue."
);
Assert(
    runtimeSource.Contains("public bool HasPendingPersistence", StringComparison.Ordinal)
        && runtimeSource.Contains("_persistenceQueue?.HasPendingPersistence == true", StringComparison.Ordinal),
    "Combat replay runtime should expose whether replay persistence is still outstanding so run completion can wait for post-persist replay events."
);
var observeMessageBody = ExtractMethodBody(
    runtimeSource,
    "public void ObserveMessage(INetMessage message)"
);
Assert(
    observeMessageBody.Contains("_persistenceQueue.Enqueue(payload, manifest);", StringComparison.Ordinal),
    "Combat replay runtime should enqueue replay persistence work instead of writing synchronously on the message observer hot path."
);
Assert(
    !observeMessageBody.Contains("_payloadStore.Save(payload);", StringComparison.Ordinal)
        && !observeMessageBody.Contains("_battleCatalog.Save(manifest);", StringComparison.Ordinal),
    "Combat replay runtime should not write replay payloads or manifests directly inside ObserveMessage."
);
var runtimeUpdateBody = ExtractMethodBody(runtimeSource, "private void Update()");
Assert(
    runtimeUpdateBody.Contains("DrainPersistenceResults();", StringComparison.Ordinal),
    "Combat replay runtime should drain completed persistence work from Update so replay-recorded events stay on the main thread."
);
var drainPersistenceBody = ExtractMethodBody(
    runtimeSource,
    "private void DrainPersistenceResults()"
);
Assert(
    drainPersistenceBody.Contains("TryDequeueResult", StringComparison.Ordinal)
        && drainPersistenceBody.Contains("new PvpBattleRecorded", StringComparison.Ordinal),
    "Combat replay runtime should publish replay-recorded events only after draining completed persistence work."
);

var persistenceQueuePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Game/CombatReplay/CombatReplayPersistenceQueue.cs"
    )
);
Assert(File.Exists(persistenceQueuePath), "Combat replay persistence queue should exist.");
var persistenceQueueSource = File.ReadAllText(persistenceQueuePath);
Assert(
    persistenceQueueSource.Contains("ConcurrentQueue", StringComparison.Ordinal)
        && persistenceQueueSource.Contains("Task.Run", StringComparison.Ordinal),
    "Combat replay persistence queue should serialize replay saves onto background tasks and surface completions through a thread-safe queue."
);
Assert(
    persistenceQueueSource.Contains("public bool HasPendingPersistence", StringComparison.Ordinal)
        && persistenceQueueSource.Contains("Interlocked.Increment", StringComparison.Ordinal)
        && persistenceQueueSource.Contains("Interlocked.Decrement", StringComparison.Ordinal),
    "Combat replay persistence queue should track outstanding replay writes until the main thread drains completion results."
);
var queueDisposeBody = ExtractMethodBody(persistenceQueueSource, "public void Dispose()");
Assert(
    queueDisposeBody.Contains("_stopAcceptingNewWork", StringComparison.Ordinal)
        && queueDisposeBody.Contains("_worker.Wait(ShutdownDrainTimeout)", StringComparison.Ordinal),
    "Combat replay persistence queue should stop new enqueues and wait briefly for queued work to drain during teardown."
);
Assert(
    queueDisposeBody.Contains("BppLog.Warn", StringComparison.Ordinal)
        && queueDisposeBody.Contains("_shutdown.Cancel();", StringComparison.Ordinal)
        && queueDisposeBody.Contains("_worker.Wait();", StringComparison.Ordinal)
        && queueDisposeBody.Contains("EnqueueAbandonedPendingResults();", StringComparison.Ordinal)
        && queueDisposeBody.Contains("_signal.Dispose();", StringComparison.Ordinal)
        && queueDisposeBody.Contains("_shutdown.Dispose();", StringComparison.Ordinal),
    "Combat replay persistence queue should cancel, wait for the worker to stop, surface abandoned work as completed failures, and only then dispose synchronization primitives."
);
var queueProcessLoopBody = ExtractMethodBody(
    persistenceQueueSource,
    "private async Task ProcessLoopAsync()"
);
Assert(
    queueProcessLoopBody.Contains("ShouldExitWorkerLoop()", StringComparison.Ordinal)
        && queueProcessLoopBody.Contains("WaitAsync(_shutdown.Token)", StringComparison.Ordinal),
    "Combat replay persistence queue worker should only exit after an explicit stop request and a drained queue unless forced shutdown cancels the wait."
);
Assert(
    queueProcessLoopBody.Contains(
        "while (!_shutdown.IsCancellationRequested && _pending.TryDequeue(out var request))",
        StringComparison.Ordinal
    ),
    "Combat replay persistence queue should stop dequeuing new replay requests once forced shutdown cancellation begins so abandoned requests remain visible for teardown result draining."
);
Assert(
    persistenceQueueSource.Contains("Action<string> deletePayload", StringComparison.Ordinal)
        && persistenceQueueSource.Contains("_deletePayload", StringComparison.Ordinal),
    "Combat replay persistence queue should accept a payload-delete callback for manifest-save rollback."
);
Assert(
    queueProcessLoopBody.Contains("var payloadSaved = false;", StringComparison.Ordinal)
        && queueProcessLoopBody.Contains("payloadSaved = true;", StringComparison.Ordinal)
        && queueProcessLoopBody.Contains("_deletePayload(request.Payload.BattleId);", StringComparison.Ordinal),
    "Combat replay persistence queue should delete the just-written payload when manifest persistence fails after payload persistence succeeds."
);
var runtimeAwakeBody = ExtractMethodBody(runtimeSource, "private void Awake()");
Assert(
    runtimeAwakeBody.Contains("CleanupOrphanedPayloads();", StringComparison.Ordinal),
    "Combat replay runtime should trigger orphan payload cleanup during Awake."
);
Assert(
    runtimeSource.Contains("private void CleanupOrphanedPayloads()", StringComparison.Ordinal)
        && runtimeSource.Contains("_payloadStore.ListBattleIds()", StringComparison.Ordinal)
        && runtimeSource.Contains("_battleCatalog.TryLoad", StringComparison.Ordinal)
        && runtimeSource.Contains("_payloadStore.Delete(", StringComparison.Ordinal),
    "Combat replay runtime should compare payload battle ids against the manifest catalog and delete orphaned payloads at startup."
);
Assert(
    !runtimeSource.Contains("_battleCatalog.Delete(", StringComparison.Ordinal),
    "Combat replay runtime should preserve battle manifests whose replay payload file is missing so history views can still render the snapshot data stored in sqlite."
);
var runtimeOnDestroyBody = ExtractMethodBody(runtimeSource, "private void OnDestroy()");
Assert(
    runtimeOnDestroyBody.Contains("DrainPersistenceResults();", StringComparison.Ordinal),
    "Combat replay runtime should drain late persistence completions during teardown so successfully saved replays can still publish their completion event."
);
var persistenceQueueType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.CombatReplayPersistenceQueue"
);
var persistenceResultType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.CombatReplayPersistenceResult"
);
var queueCtor = persistenceQueueType.GetConstructor(
    [
        typeof(Action<>).MakeGenericType(payloadType),
        typeof(Action<>).MakeGenericType(manifestType),
        typeof(Action<string>),
    ]
);
Assert(
    queueCtor != null,
    "Combat replay persistence queue should accept payload-save, manifest-save, and payload-delete callbacks."
);
var queueHarness = new QueuePersistenceHarness();
var queue = queueCtor!.Invoke(
    [
        queueHarness.CreateSavePayloadDelegate(typeof(Action<>).MakeGenericType(payloadType)),
        queueHarness.CreateSaveManifestDelegate(typeof(Action<>).MakeGenericType(manifestType)),
        new Action<string>(queueHarness.DeletePayload),
    ]
);
Assert(queue != null, "Combat replay persistence queue should be constructible.");

var slowPayload = Activator.CreateInstance(payloadType);
Assert(slowPayload != null, "Slow replay payload should be constructible.");
SetProperty(payloadType, slowPayload!, "BattleId", "battle-dispose-slow");
var slowManifest = Activator.CreateInstance(manifestType);
Assert(slowManifest != null, "Slow replay manifest should be constructible.");
SetProperty(manifestType, slowManifest!, "BattleId", "battle-dispose-slow");

var abandonedPayload = Activator.CreateInstance(payloadType);
Assert(abandonedPayload != null, "Abandoned replay payload should be constructible.");
SetProperty(payloadType, abandonedPayload!, "BattleId", "battle-dispose-abandoned");
var abandonedManifest = Activator.CreateInstance(manifestType);
Assert(abandonedManifest != null, "Abandoned replay manifest should be constructible.");
SetProperty(manifestType, abandonedManifest!, "BattleId", "battle-dispose-abandoned");

Invoke(
    persistenceQueueType,
    queue!,
    "Enqueue",
    new object?[] { slowPayload!, slowManifest! }
);
Invoke(
    persistenceQueueType,
    queue!,
    "Enqueue",
    new object?[] { abandonedPayload!, abandonedManifest! }
);
Assert(
    queueHarness.FirstPayloadStarted.Wait(TimeSpan.FromSeconds(2)),
    "Queue should begin persisting the first replay request before disposal starts."
);

var disposeTask = Task.Run(() =>
    Invoke(persistenceQueueType, queue!, "Dispose", Array.Empty<object?>())
);
Thread.Sleep(millisecondsTimeout: 650);
queueHarness.AllowFirstPayloadToComplete.Set();
Assert(
    disposeTask.Wait(TimeSpan.FromSeconds(5)),
    "Disposing the persistence queue should return after the in-flight replay write finishes."
);

var queueResults = new List<object>();
while (TryDequeuePersistenceResult(persistenceQueueType, queue!, out var result))
{
    queueResults.Add(result!);
}

Assert(
    queueResults.Count == 2,
    "Queue disposal should surface one result for the completed in-flight replay and one result for the abandoned pending replay."
);
Assert(
    queueResults.Any(result =>
        (bool)GetProperty(persistenceResultType, result, "Succeeded")
            && string.Equals(
                (string?)GetProperty(
                    manifestType,
                    GetProperty(persistenceResultType, result, "Manifest"),
                    "BattleId"
                ),
                "battle-dispose-slow",
                StringComparison.Ordinal
            )
    ),
    "Queue disposal should preserve the successful in-flight replay result even when shutdown cancellation starts before it finishes."
);
Assert(
    queueResults.Any(result =>
        !(bool)GetProperty(persistenceResultType, result, "Succeeded")
            && string.Equals(
                (string?)GetProperty(
                    manifestType,
                    GetProperty(persistenceResultType, result, "Manifest"),
                    "BattleId"
                ),
                "battle-dispose-abandoned",
                StringComparison.Ordinal
            )
    ),
    "Queue disposal should convert abandoned pending replays into completed failure results instead of dropping them silently."
);
Assert(
    queueHarness.SavedManifestBattleIds.SequenceEqual(["battle-dispose-slow"]),
    "Forced shutdown should not begin persisting the second replay manifest after cancellation starts."
);
Assert(
    !queueHarness.DeletedPayloadBattleIds.Any(),
    "Forced shutdown abandonment should not roll back payloads when the manifest save was never attempted."
);
Assert(
    !(bool)GetProperty(persistenceQueueType, queue!, "HasPendingPersistence"),
    "After draining the completed queue results from disposal, replay persistence should no longer report outstanding work."
);
Assert(
    runtimeSource.Contains("ResolveReplayDependencies", StringComparison.Ordinal),
    "Combat replay runtime should centralize replay dependency resolution."
);
Assert(
    runtimeSource.Contains("AppDomain.CurrentDomain.GetAssemblies()", StringComparison.Ordinal),
    "Combat replay runtime should resolve replay types by scanning loaded assemblies instead of relying on a single assembly-qualified lookup."
);
Assert(
    runtimeSource.Contains("EnsureSocketBehavior()", StringComparison.Ordinal)
        && runtimeSource.Contains("ResolveReplayHostType", StringComparison.Ordinal),
    "Combat replay runtime should resolve replay injection through the native replay host manager."
);
Assert(
    runtimeSource.Contains("ReplayBootstrapContext", StringComparison.Ordinal),
    "Combat replay runtime should describe replay bootstrap dependencies with an explicit context type."
);
Assert(
    runtimeSource.Contains(
        "await TryInjectSavedReplayAsync(bootstrapContext, manifest, sequence, battleId);",
        StringComparison.Ordinal
    ),
    "Combat replay runtime should inject saved replays from manifest + payload."
);
Assert(
    runtimeSource.Contains("new ReplayBootstrapContext", StringComparison.Ordinal),
    "Combat replay runtime should materialize a replay bootstrap context from ResolveReplayDependencies."
);
Assert(
    runtimeSource.Contains("Processor", StringComparison.Ordinal)
        && runtimeSource.Contains("TriggerCombatSequenceCreated", StringComparison.Ordinal),
    "Replay bootstrap dependency resolution should expose both processor access and replay trigger access."
);
Assert(
    runtimeSource.Contains("Data.UpdateFromGameSimAsync(spawnMessage);", StringComparison.Ordinal)
        && runtimeSource.Contains(
            "MarkGameSimMessageHandled(gameSimHandler, spawnMessage.MessageId);",
            StringComparison.Ordinal
        ),
    "Combat replay runtime should sync replay spawn data and mark it handled without entering the live GameSim pipeline."
);
Assert(
    runtimeSource.Contains("RehydrateSavedReplayPlayerCards", StringComparison.Ordinal)
        && runtimeSource.Contains("Data.GetOrCreateCard", StringComparison.Ordinal),
    "Combat replay runtime should rehydrate saved player cards before entering ReplayState."
);
Assert(
    runtimeSource.Contains("RehydrateSavedReplayOpponentCards", StringComparison.Ordinal)
        && runtimeSource.Contains("RehydrateSavedReplayPlayerSkills", StringComparison.Ordinal)
        && runtimeSource.Contains("RehydrateSavedReplayOpponentSkills", StringComparison.Ordinal),
    "Combat replay runtime should expose dedicated rehydration paths for both sides' cards and skills."
);
Assert(
    runtimeSource.Contains("card.Size = snapshot.Size;", StringComparison.Ordinal),
    "Combat replay runtime should restore player card size before native replay spawning."
);
Assert(
    runtimeSource.Contains("replayState.Replay();", StringComparison.Ordinal)
        && runtimeSource.Contains(
            "ShowReplayAndRecapButtons(show: false, deactivate: true);",
            StringComparison.Ordinal
        ),
    "Combat replay runtime should auto-start replay playback after entering ReplayState."
);
Assert(
    runtimeSource.Contains(
        "private static void MarkGameSimMessageHandled",
        StringComparison.Ordinal
    ) && runtimeSource.Contains("\"_handledMessages\"", StringComparison.Ordinal),
    "Combat replay runtime should be able to mark the replay spawn message as handled for ReplayState."
);
Assert(
    runtimeSource.Contains("does not contain player-hand snapshots", StringComparison.Ordinal),
    "Combat replay runtime should warn when an old replay does not contain player-hand snapshots."
);
Assert(
    runtimeSource.Contains("does not contain opponent-hand snapshots", StringComparison.Ordinal)
        && runtimeSource.Contains(
            "does not contain player-skill snapshots",
            StringComparison.Ordinal
        )
        && runtimeSource.Contains(
            "does not contain opponent-skill snapshots",
            StringComparison.Ordinal
        ),
    "Combat replay runtime should warn when older replays are missing card or skill snapshots for either side."
);
var snapshotSource = File.ReadAllText(
    Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../Game/CombatReplay/CombatReplayCardSnapshot.cs"
        )
    )
);
Assert(
    snapshotSource.Contains("public ECardSize Size", StringComparison.Ordinal),
    "Combat replay card snapshots should preserve native card size."
);
Assert(
    snapshotSource.Contains("public ECardType Type", StringComparison.Ordinal),
    "Combat replay card snapshots should preserve card type for skill rehydration."
);
Assert(
    runtimeSource.Contains("RollbackReplayBootstrapAsync", StringComparison.Ordinal),
    "Combat replay runtime should centralize replay bootstrap rollback."
);
Assert(
    !runtimeSource.Contains("StartRun()", StringComparison.Ordinal),
    "Combat replay runtime should not start a real run when bootstrapping a saved replay."
);
Assert(
    !runtimeSource.Contains("Events.RunStarted.Trigger()", StringComparison.Ordinal),
    "Combat replay runtime should not trigger run-start events for saved replay bootstrap."
);
Assert(
    runtimeSource.Contains("CanReplaySavedCombats", StringComparison.Ordinal)
        && runtimeSource.Contains(
            "BppRuntimeHost.RunContext.IsInGameRun",
            StringComparison.Ordinal
        ),
    "Combat replay runtime should block saved replays only while local gameplay is active."
);
Assert(
    runtimeSource.Contains("CanReplaySavedBattle", StringComparison.Ordinal),
    "Combat replay runtime should expose selected-battle replay availability checks for non-debug UI surfaces."
);
Assert(
    runtimeSource.Contains("SceneID.GameScene", StringComparison.Ordinal)
        && runtimeSource.Contains("SceneID.GameplayLoading", StringComparison.Ordinal),
    "Combat replay runtime should know how to load gameplay scenes from the lobby."
);
Assert(
    runtimeSource.Contains(
        "AppState.Initialize(sharedVariables, processor);",
        StringComparison.Ordinal
    ),
    "Combat replay runtime should initialize AppState handlers when the lobby bootstrap path does not provide them."
);
Assert(
    runtimeSource.Contains("processor.Handle(spawnMessage)", StringComparison.Ordinal),
    "Combat replay runtime should validate the saved spawn snapshot with NetMessageProcessor before replay injection."
);
Assert(
    runtimeSource.Contains(
        "TryGetAppStateField<GameSimHandler>(\"_gameSimHandler\") != null",
        StringComparison.Ordinal
    ),
    "Combat replay readiness should require GameSimHandler to exist before replay injection starts."
);
Assert(
    runtimeSource.Contains("SetUpBoard", StringComparison.Ordinal)
        && runtimeSource.Contains("Init(boardManager)", StringComparison.Ordinal),
    "Combat replay runtime should bootstrap board and game services locally without RunManager.StartRun()."
);
Assert(
    runtimeSource.Contains(
        "Replay bootstrap scene environment is ready.",
        StringComparison.Ordinal
    ),
    "Combat replay runtime should mark when scene-only replay bootstrap becomes ready."
);
Assert(
    runtimeSource.Contains("Replay bootstrap dependencies resolved.", StringComparison.Ordinal),
    "Combat replay runtime should log when replay bootstrap dependencies are ready."
);
Assert(
    runtimeSource.Contains("Saved replay injection completed", StringComparison.Ordinal),
    "Combat replay runtime should log when the saved replay payload has been injected."
);
Assert(
    runtimeSource.Contains(
        "Returning to main menu after bootstrapped replay exit.",
        StringComparison.Ordinal
    ),
    "Combat replay runtime should log the menu-return path after a bootstrapped replay exits."
);
Assert(
    runtimeSource.Contains("_returnToMenuAfterReplay", StringComparison.Ordinal),
    "Combat replay runtime should track whether a replay should return to menu after exit."
);
Assert(
    runtimeSource.Contains("_bootstrappedReplayActive", StringComparison.Ordinal),
    "Combat replay runtime should scope menu-return behavior to the bootstrapped replay instance."
);
Assert(
    runtimeSource.Contains("RollbackReplayBootstrapAsync", StringComparison.Ordinal)
        && runtimeSource.Contains("ReturnToMainMenu()", StringComparison.Ordinal),
    "Combat replay runtime should roll back to the main menu when lobby bootstrap fails."
);
Assert(
    runtimeSource.Contains("AppState.Reset();", StringComparison.Ordinal)
        && runtimeSource.Contains("Data.ResetRunData();", StringComparison.Ordinal)
        && runtimeSource.Contains("SceneID.HeroSelectScene", StringComparison.Ordinal),
    "Failed replay bootstrap should explicitly reset local run state and reload the lobby scene."
);
Assert(
    runtimeSource.Contains("OnStateChanged", StringComparison.Ordinal)
        && runtimeSource.Contains("ReturnToMainMenu()", StringComparison.Ordinal),
    "Combat replay runtime should return to the main menu after a bootstrapped replay exits ReplayState."
);
var injectReplayBody = ExtractMethodBody(
    runtimeSource,
    "private static async Task TryInjectSavedReplayAsync("
);
Assert(
    injectReplayBody.IndexOf("HandleSpawnMessageAsync", StringComparison.Ordinal)
        < injectReplayBody.IndexOf("TriggerCombatSequenceCreated", StringComparison.Ordinal)
        && injectReplayBody.IndexOf("TriggerCombatSequenceCreated", StringComparison.Ordinal)
            < injectReplayBody.IndexOf(
                "AppState.TryPushState<ReplayState>()",
                StringComparison.Ordinal
            )
        && injectReplayBody.IndexOf(
            "AppState.TryPushState<ReplayState>()",
            StringComparison.Ordinal
        ) < injectReplayBody.IndexOf("replayState.Replay()", StringComparison.Ordinal),
    "Combat replay runtime should notify ReplayState's native sequence handler before entering ReplayState."
);
var triggerReplayBody = ExtractMethodBody(
    runtimeSource,
    "private static Action CreateTriggerCombatSequenceCreated(object processor)"
);
Assert(
    triggerReplayBody.Contains("return () =>", StringComparison.Ordinal)
        && triggerReplayBody.Contains(
            "field?.GetValue(processor) as Action",
            StringComparison.Ordinal
        ),
    "Combat replay runtime should resolve CombatSequenceCreated listeners at trigger time instead of capturing a stale delegate."
);

var debugPanelStateSource = File.ReadAllText(
    Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../Game/DebugPanel/DebugPanelState.cs")
    )
);
Assert(
    debugPanelStateSource.Contains("Replays", StringComparison.Ordinal),
    "DebugPanel state should expose a Replays section."
);

var debugPanelSource = File.ReadAllText(
    Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../Game/DebugPanel/DebugPanel.cs")
    )
);
Assert(
    debugPanelSource.Contains("Replay Latest", StringComparison.Ordinal),
    "Debug panel should expose a replay-latest action."
);
Assert(
    debugPanelSource.Contains("ListRecentBattles", StringComparison.Ordinal),
    "Debug panel should read saved battles from the catalog-backed runtime API."
);
Assert(
    debugPanelSource.Contains("CanReplaySavedCombats", StringComparison.Ordinal),
    "Debug panel should respect replay availability checks instead of always offering replay actions."
);
var replayReferenceSource = File.ReadAllText(
    Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../docs/reference/combat-replay-recording.md"
        )
    )
);
Assert(
    replayReferenceSource.Contains("HistoryPanel", StringComparison.Ordinal)
        && replayReferenceSource.Contains("DebugPanel", StringComparison.Ordinal),
    "Combat replay reference should document both the HistoryPanel and DebugPanel replay entry points."
);
var readmeSource = File.ReadAllText(
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../README.md"))
);
Assert(
    readmeSource.Contains("HistoryPanel", StringComparison.Ordinal)
        && readmeSource.Contains("DebugPanel", StringComparison.Ordinal),
    "README should mention both the HistoryPanel and DebugPanel replay entry points for saved replays."
);

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-combat-replay-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "run-logs.db");

try
{
    var payloadStore = Activator.CreateInstance(payloadStoreType, tempRoot);
    Assert(payloadStore != null, "CombatReplayPayloadStore should be constructible.");
    var battleCatalog = Activator.CreateInstance(catalogType, dbPath);
    Assert(battleCatalog != null, "PvpBattleCatalog should be constructible.");

    var payload = Activator.CreateInstance(payloadType);
    Assert(payload != null, "PvpReplayPayload should be constructible.");
    SetProperty(payloadType, payload!, "BattleId", "battle-001");
    SetProperty(payloadType, payload!, "Version", 1);
    SetProperty(
        payloadType,
        payload!,
        "SpawnMessageBase64",
        Convert.ToBase64String(new byte[] { 1, 2, 3 })
    );
    SetProperty(
        payloadType,
        payload!,
        "CombatMessageBase64",
        Convert.ToBase64String(new byte[] { 4, 5, 6 })
    );
    SetProperty(
        payloadType,
        payload!,
        "DespawnMessageBase64",
        Convert.ToBase64String(new byte[] { 7, 8, 9 })
    );
    Invoke(payloadStoreType, payloadStore!, "Save", new object?[] { payload! });
    Assert(
        Equals(
            Invoke(payloadStoreType, payloadStore!, "Exists", new object?[] { "battle-001" }),
            true
        ),
        "Payload store should report saved payloads."
    );
    var loadedPayload = Invoke(
        payloadStoreType,
        payloadStore!,
        "Load",
        new object?[] { "battle-001" }
    );
    Assert(loadedPayload != null, "Payload store should load a saved payload by battle id.");
    Assert(
        string.Equals(
            (string?)GetProperty(payloadType, loadedPayload!, "CombatMessageBase64"),
            Convert.ToBase64String(new byte[] { 4, 5, 6 }),
            StringComparison.Ordinal
        ),
        "Payload store should preserve the serialized combat payload."
    );

    var manifest = CreateManifestFixture(
        manifestType,
        cardSetCaptureType,
        captureStatusType,
        captureSourceType,
        battleId: "battle-001",
        runId: "run-001",
        savedAtUtc: new DateTimeOffset(2026, 3, 18, 1, 2, 3, TimeSpan.Zero),
        combatKind: "PVPCombat",
        day: 3,
        hour: 5,
        encounterId: "encounter-abc",
        playerName: "Local Player",
        playerAccountId: "player-account-001",
        opponentName: "Test Opponent",
        opponentHero: "Vanessa",
        opponentRank: "Legend",
        opponentRating: 2048,
        opponentLevel: 12,
        opponentAccountId: "opponent-account-001",
        result: "win",
        winnerCombatantId: "Player",
        loserCombatantId: "Opponent",
        playerHandCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "LiveRetry",
            CreateSnapshotList("p-hand-1", "tpl-p-hand")
        ),
        playerSkillsCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Missing",
            "Unknown",
            CreateEmptySnapshotList()
        ),
        opponentHandCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "OpeningMessage",
            CreateSnapshotList("o-hand-1", "tpl-o-hand")
        ),
        opponentSkillsCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "OpeningMessage",
            CreateSnapshotList("o-skill-1", "tpl-o-skill")
        )
    );
    Invoke(catalogType, battleCatalog!, "Save", new object?[] { manifest! });
    var loadedManifest = Invoke(
        catalogType,
        battleCatalog!,
        "TryLoad",
        new object?[] { "battle-001" }
    );
    Assert(loadedManifest != null, "Catalog should load a saved manifest by battle id.");
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, loadedManifest!, "EncounterId"),
            "encounter-abc",
            StringComparison.Ordinal
        ),
        "Catalog should preserve manifest metadata."
    );

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            CountRows(connection, "pvp_battles") == 1,
            "Saving a PVP battle manifest should insert one row into pvp_battles."
        );
        Assert(
            !ColumnExists(connection, "pvp_battles", "replay_id"),
            "pvp_battles should no longer expose replay_id."
        );
        Assert(
            GetString(
                connection,
                "SELECT player_name FROM pvp_battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Local Player",
            "pvp_battles should persist the player name."
        );
        Assert(
            GetString(
                connection,
                "SELECT player_account_id FROM pvp_battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "player-account-001",
            "pvp_battles should persist the player account id."
        );
        Assert(
            GetString(
                connection,
                "SELECT opponent_account_id FROM pvp_battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "opponent-account-001",
            "pvp_battles should persist the opponent account id."
        );
        Assert(
            GetString(
                connection,
                "SELECT opponent_hero FROM pvp_battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Vanessa",
            "pvp_battles should persist the opponent hero."
        );
        Assert(
            GetString(
                connection,
                "SELECT opponent_rank FROM pvp_battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Legend",
            "pvp_battles should persist the opponent rank."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT opponent_rating FROM pvp_battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 2048,
            "pvp_battles should persist the opponent rating."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT opponent_level FROM pvp_battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 12,
            "pvp_battles should persist the opponent level."
        );
        Assert(
            GetString(
                connection,
                "SELECT result FROM pvp_battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "win",
            "pvp_battles should persist the player's combat result."
        );
        Assert(
            GetString(
                connection,
                "SELECT winner_combatant_id FROM pvp_battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Player",
            "pvp_battles should persist the winner combatant id."
        );
        var playerHandJson = GetString(
            connection,
            "SELECT player_hand_json FROM pvp_battles WHERE battle_id = $battleId;",
            "battle-001"
        );
        Assert(
            playerHandJson.Contains("status", StringComparison.Ordinal)
                && playerHandJson.Contains("source", StringComparison.Ordinal)
                && playerHandJson.Contains("items", StringComparison.Ordinal)
                && playerHandJson.Contains("Captured", StringComparison.Ordinal)
                && playerHandJson.Contains("LiveRetry", StringComparison.Ordinal)
                && playerHandJson.Contains("p-hand-1", StringComparison.Ordinal)
                && playerHandJson.Contains("Sparkblade", StringComparison.Ordinal)
                && playerHandJson.Contains("Radiant", StringComparison.Ordinal)
                && playerHandJson.Contains("Damage", StringComparison.Ordinal),
            "pvp_battles should persist detailed capture objects for hand-card metadata."
        );
        var playerSkillsJson = GetString(
            connection,
            "SELECT player_skills_json FROM pvp_battles WHERE battle_id = $battleId;",
            "battle-001"
        );
        Assert(
            playerSkillsJson.Contains("Missing", StringComparison.Ordinal)
                && playerSkillsJson.Contains("Unknown", StringComparison.Ordinal)
                && !playerSkillsJson.Contains("CapturedEmpty", StringComparison.Ordinal),
            "pvp_battles should preserve explicit missing capture semantics instead of guessing from empty lists."
        );
    }

    var legacyDbPath = Path.Combine(tempRoot, "legacy-run-logs.db");
    using (var legacyConnection = new SqliteConnection($"Data Source={legacyDbPath}"))
    {
        legacyConnection.Open();
        using var legacyCommand = legacyConnection.CreateCommand();
        legacyCommand.CommandText = """
            CREATE TABLE pvp_battles (
                battle_id TEXT PRIMARY KEY,
                replay_id TEXT NOT NULL,
                run_id TEXT NULL,
                recorded_at_utc TEXT NOT NULL,
                day INTEGER NULL,
                hour INTEGER NULL,
                encounter_id TEXT NULL,
                player_name TEXT NULL,
                player_account_id TEXT NULL,
                opponent_name TEXT NULL,
                opponent_hero TEXT NULL,
                opponent_rank TEXT NULL,
                opponent_rating INTEGER NULL,
                opponent_level INTEGER NULL,
                opponent_account_id TEXT NULL,
                combat_kind TEXT NOT NULL,
                result TEXT NULL,
                winner_combatant_id TEXT NULL,
                loser_combatant_id TEXT NULL,
                player_hand_json TEXT NOT NULL,
                player_skills_json TEXT NOT NULL,
                opponent_hand_json TEXT NOT NULL,
                opponent_skills_json TEXT NOT NULL
            );

            INSERT INTO pvp_battles (
                battle_id,
                replay_id,
                run_id,
                recorded_at_utc,
                day,
                hour,
                encounter_id,
                player_name,
                player_account_id,
                opponent_name,
                opponent_hero,
                opponent_rank,
                opponent_rating,
                opponent_level,
                opponent_account_id,
                combat_kind,
                result,
                winner_combatant_id,
                loser_combatant_id,
                player_hand_json,
                player_skills_json,
                opponent_hand_json,
                opponent_skills_json
            ) VALUES (
                'legacy-battle-001',
                'legacy-replay-001',
                'legacy-run-001',
                '2026-03-17T01:02:03.0000000+00:00',
                2,
                4,
                'legacy-encounter',
                'Legacy Player',
                'legacy-player-account',
                'Legacy Opponent',
                NULL,
                NULL,
                NULL,
                NULL,
                'legacy-opponent-account',
                'PVPCombat',
                'loss',
                'Opponent',
                'Player',
                '{"status":"Captured","source":"OpeningMessage","items":[]}',
                '{"status":"Missing","source":"Unknown","items":[]}',
                '{"status":"Captured","source":"OpeningMessage","items":[]}',
                '{"status":"CapturedEmpty","source":"OpeningMessage","items":[]}'
            );
            """;
        legacyCommand.ExecuteNonQuery();
    }

    var migratedBattleCatalog = Activator.CreateInstance(catalogType, legacyDbPath);
    Assert(
        migratedBattleCatalog != null,
        "PvpBattleCatalog should migrate legacy pvp_battles tables."
    );
    var migratedLegacyManifest = Invoke(
        catalogType,
        migratedBattleCatalog!,
        "TryLoad",
        new object?[] { "legacy-battle-001" }
    );
    Assert(migratedLegacyManifest != null, "Catalog should preserve existing legacy battle rows.");
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, migratedLegacyManifest!, "RunId"),
            "legacy-run-001",
            StringComparison.Ordinal
        ),
        "Legacy battle rows should survive replay_id migration."
    );
    Invoke(catalogType, migratedBattleCatalog!, "Save", new object?[] { manifest! });
    using (var migratedConnection = new SqliteConnection($"Data Source={legacyDbPath}"))
    {
        migratedConnection.Open();
        Assert(
            !ColumnExists(migratedConnection, "pvp_battles", "replay_id"),
            "Legacy pvp_battles tables should be migrated to drop replay_id."
        );
        Assert(
            CountRows(migratedConnection, "pvp_battles") == 2,
            "Migrated catalogs should keep legacy rows and accept new battle manifests."
        );
    }

    var captureService = Activator.CreateInstance(captureServiceType);
    Assert(captureService != null, "CombatReplayCaptureService should be constructible.");

    var nonCombatStart = CreateGameSimMessage(
        "Encounter",
        day: 1,
        hour: 1,
        encounterId: "encounter-ignore",
        opponentName: "Ignored Opponent"
    );
    var combatMessage = CreateCombatSimMessage();
    var pveCombatStart = CreateGameSimMessage(
        "Combat",
        day: 2,
        hour: 4,
        encounterId: "encounter-pve",
        opponentName: "PvE Opponent"
    );
    var combatStart = CreateGameSimMessage(
        "PVPCombat",
        day: 3,
        hour: 4,
        encounterId: "encounter-live",
        opponentName: "Rival"
    );
    var combatEnd = CreateGameSimMessage(
        "Encounter",
        day: 3,
        hour: 5,
        encounterId: null,
        opponentName: null
    );

    var ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { nonCombatStart, "run-ignore" }
    );
    Assert(
        ignoredResult == null,
        "Non-combat opening GameSim should not immediately create a replay."
    );
    ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { combatMessage, "run-ignore" }
    );
    Assert(
        ignoredResult == null,
        "A CombatSim without a combat-opening GameSim should not create a replay."
    );
    ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { combatEnd, "run-ignore" }
    );
    Assert(
        ignoredResult == null,
        "An invalid GameSim/CombatSim/GameSim triplet should be ignored."
    );
    ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { pveCombatStart, "run-ignore" }
    );
    Assert(ignoredResult == null, "A non-PVP combat opening should not start replay capture.");
    ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { combatMessage, "run-ignore" }
    );
    Assert(ignoredResult == null, "A CombatSim after a non-PVP opening should still be ignored.");

    Assert(
        Invoke(
            captureServiceType,
            captureService!,
            "Accept",
            new object?[] { combatStart, "run-42" }
        ) == null,
        "Combat opening GameSim should buffer until the sequence completes."
    );
    Assert(
        Invoke(
            captureServiceType,
            captureService!,
            "Accept",
            new object?[] { combatMessage, "run-42" }
        ) == null,
        "CombatSim should buffer until the closing GameSim arrives."
    );
    var completedArtifact = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { combatEnd, "run-42" }
    );
    Assert(completedArtifact != null, "A combat triplet should produce a battle artifact.");
    var completedManifest = GetProperty(artifactType, completedArtifact!, "Manifest");
    var completedPayload = GetProperty(artifactType, completedArtifact!, "Payload");
    Assert(completedManifest != null, "Battle artifacts should include a manifest.");
    Assert(completedPayload != null, "Battle artifacts should include a replay payload.");
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, completedManifest!, "RunId"),
            "run-42",
            StringComparison.Ordinal
        ),
        "Completed battle manifests should preserve the run id."
    );
    Assert(
        Equals(GetProperty(manifestType, completedManifest!, "Day"), 3),
        "Completed battle manifests should capture the combat day."
    );
    Assert(
        Equals(GetProperty(manifestType, completedManifest!, "Hour"), 4),
        "Completed battle manifests should capture the combat hour from the opening snapshot."
    );
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, completedManifest!, "CombatKind"),
            "PVPCombat",
            StringComparison.Ordinal
        ),
        "Completed battle manifests should preserve the combat kind from the opening snapshot."
    );
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, completedManifest!, "EncounterId"),
            "encounter-live",
            StringComparison.Ordinal
        ),
        "Completed battle manifests should preserve the opening encounter id."
    );
    Assert(
        !string.IsNullOrWhiteSpace(
            (string?)GetProperty(payloadType, completedPayload!, "SpawnMessageBase64")
        ),
        "Completed replay payloads should serialize the opening GameSim."
    );
    Assert(
        !string.IsNullOrWhiteSpace(
            (string?)GetProperty(payloadType, completedPayload!, "CombatMessageBase64")
        ),
        "Completed replay payloads should serialize the CombatSim."
    );
    Assert(
        !string.IsNullOrWhiteSpace(
            (string?)GetProperty(payloadType, completedPayload!, "DespawnMessageBase64")
        ),
        "Completed replay payloads should serialize the closing GameSim."
    );
    Assert(
        GetProperty(manifestType, completedManifest!, "Snapshots") != null,
        "Completed battle manifests should capture snapshot wrappers for both sides."
    );

    var collector = Activator.CreateInstance(collectorType);
    Assert(collector != null, "PvpBattleSnapshotCollector should be constructible.");
    var participantCandidate = Activator.CreateInstance(candidateType);
    Assert(
        participantCandidate != null,
        "CombatReplaySequenceCandidate should be constructible for participant tests."
    );
    SetProperty(candidateType, participantCandidate!, "PlayerRank", "Legendary 5");
    SetProperty(candidateType, participantCandidate!, "PlayerRating", 502);
    SetProperty(candidateType, participantCandidate!, "OpponentName", "Snapshot Opponent");
    SetProperty(candidateType, participantCandidate!, "OpponentHero", "Vanessa");
    SetProperty(candidateType, participantCandidate!, "OpponentRank", "Legendary");
    SetProperty(candidateType, participantCandidate!, "OpponentRating", 728);
    SetProperty(candidateType, participantCandidate!, "OpponentLevel", 9);
    SetProperty(candidateType, participantCandidate!, "OpponentAccountId", "opponent-snapshot-id");
    var participants = Invoke(
        collectorType,
        collector!,
        "BuildParticipants",
        new object?[] { participantCandidate! }
    );
    Assert(participants != null, "BuildParticipants should return a participants snapshot.");
    var participantsType = participants!.GetType();
    Assert(
        string.Equals(
            (string?)GetProperty(participantsType, participants, "PlayerRank"),
            "Legendary 5",
            StringComparison.Ordinal
        )
            && Equals(GetProperty(participantsType, participants, "PlayerRating"), 502),
        "BuildParticipants should preserve the player rank and rating captured on the opening candidate."
    );
    Assert(
        string.Equals(
            (string?)GetProperty(participantsType, participants, "OpponentName"),
            "Snapshot Opponent",
            StringComparison.Ordinal
        )
            && string.Equals(
                (string?)GetProperty(participantsType, participants, "OpponentAccountId"),
                "opponent-snapshot-id",
                StringComparison.Ordinal
            ),
        "BuildParticipants should preserve opponent identity captured on the opening candidate."
    );

    var loader = Activator.CreateInstance(loaderType);
    Assert(loader != null, "CombatReplayLoader should be constructible.");
    var loadedSequence = Invoke(loaderType, loader!, "Load", new object?[] { completedPayload! });
    Assert(loadedSequence != null, "CombatReplayLoader should deserialize a saved replay payload.");
    Assert(
        GetFieldValue(loadedSequence!.GetType(), loadedSequence, "SpawnMessage") != null,
        "Loaded replay sequences should include the opening GameSim."
    );
    Assert(
        GetFieldValue(loadedSequence!.GetType(), loadedSequence, "CombatMessage") != null,
        "Loaded replay sequences should include the CombatSim."
    );
    Assert(
        GetFieldValue(loadedSequence!.GetType(), loadedSequence, "DespawnMessage") != null,
        "Loaded replay sequences should include the closing GameSim."
    );

    Invoke(payloadStoreType, payloadStore!, "Save", new object?[] { completedPayload! });
    Invoke(catalogType, battleCatalog!, "Save", new object?[] { completedManifest! });

    var controller = Activator.CreateInstance(controllerType, battleCatalog, payloadStore, loader);
    Assert(controller != null, "CombatReplayController should be constructible.");
    var missingPayloadManifest = CreateManifestFixture(
        manifestType,
        cardSetCaptureType,
        captureStatusType,
        captureSourceType,
        battleId: "battle-missing-payload",
        runId: "run-missing-payload",
        savedAtUtc: new DateTimeOffset(2026, 3, 19, 1, 2, 3, TimeSpan.Zero),
        combatKind: "PVPCombat",
        day: 4,
        hour: 6,
        encounterId: "encounter-missing-payload",
        playerName: "Local Player",
        playerAccountId: "player-account-001",
        opponentName: "Missing Payload Opponent",
        opponentHero: "Pygmalien",
        opponentRank: "Master",
        opponentRating: 2199,
        opponentLevel: 14,
        opponentAccountId: "opponent-account-missing-payload",
        result: "loss",
        winnerCombatantId: "Opponent",
        loserCombatantId: "Player",
        playerHandCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "LiveRetry",
            CreateSnapshotList("missing-p-hand-1", "tpl-missing-p-hand")
        ),
        playerSkillsCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "CapturedEmpty",
            "LiveRetry",
            CreateEmptySnapshotList()
        ),
        opponentHandCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "OpeningMessage",
            CreateSnapshotList("missing-o-hand-1", "tpl-missing-o-hand")
        ),
        opponentSkillsCapture: CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "OpeningMessage",
            CreateSnapshotList("missing-o-skill-1", "tpl-missing-o-skill")
        )
    );
    Invoke(catalogType, battleCatalog!, "Save", new object?[] { missingPayloadManifest! });
    var savedReplays = (
        (System.Collections.IEnumerable)Invoke(
            controllerType,
            controller!,
            "ListRecentBattles",
            Array.Empty<object?>()
        )
    )
        .Cast<object>()
        .ToList();
    Assert(savedReplays.Count >= 1, "Controller should expose recent battle manifests.");
    Assert(
        savedReplays.All(manifestEntry =>
            !string.Equals(
                (string?)GetProperty(manifestType, manifestEntry, "BattleId"),
                "battle-missing-payload",
                StringComparison.Ordinal
            )
        ),
        "Controller should hide battle manifests whose replay payload file is missing."
    );
    var latestBattle = Invoke(
        controllerType,
        controller!,
        "GetLatestBattle",
        Array.Empty<object?>()
    );
    Assert(
        latestBattle != null
            && !string.Equals(
                (string?)GetProperty(manifestType, latestBattle, "BattleId"),
                "battle-missing-payload",
                StringComparison.Ordinal
            ),
        "Controller should not surface a latest battle whose replay payload file is missing."
    );

    var battleId = (string?)GetProperty(manifestType, completedManifest!, "BattleId");
    var loadedManifestFromController = Invoke(
        controllerType,
        controller!,
        "LoadBattle",
        new object?[] { battleId! }
    );
    Assert(
        loadedManifestFromController != null,
        "Controller should load a saved battle manifest by id."
    );
    Assert(
        Invoke(
            controllerType,
            controller!,
            "LoadBattle",
            new object?[] { "battle-missing-payload" }
        ) == null,
        "Controller should reject loading a battle whose replay payload file is missing."
    );
    var loadedPayloadFromController = Invoke(
        controllerType,
        controller!,
        "LoadPayload",
        new[] { loadedManifestFromController }
    );
    Assert(
        loadedPayloadFromController != null,
        "Controller should load a saved replay payload by battle id."
    );
    var loadedFromController = Invoke(
        controllerType,
        controller!,
        "LoadReplay",
        new[] { loadedPayloadFromController }
    );
    Assert(loadedFromController != null, "Controller should deserialize a loaded replay payload.");
    Assert(
        string.Equals(
            (string?)GetProperty(controllerType, controller!, "ActiveBattleId"),
            battleId,
            StringComparison.Ordinal
        ),
        "Controller should track the active battle id."
    );

    Console.WriteLine("CombatReplayRecording store checks passed.");
}
finally
{
    if (Directory.Exists(tempRoot))
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        catch (IOException)
        {
            try
            {
                System.Threading.Thread.Sleep(200);
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup on Windows when SQLite releases the file handle late.
            }
        }
    }
}

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static object? Invoke(Type type, object instance, string methodName, object?[] args)
{
    var method = type.GetMethod(
        methodName,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(method != null, $"Method not found: {type.FullName}.{methodName}");
    return method!.Invoke(instance, args);
}

static bool TryDequeuePersistenceResult(Type queueType, object queue, out object? result)
{
    var method = queueType.GetMethod(
        "TryDequeueResult",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(method != null, $"Method not found: {queueType.FullName}.TryDequeueResult");
    var args = new object?[] { null };
    var dequeued = (bool)(method!.Invoke(queue, args) ?? false);
    result = args[0];
    return dequeued;
}

static void SetProperty(Type type, object instance, string name, object? value)
{
    var property = type.GetProperty(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(property != null, $"Property not found: {type.FullName}.{name}");
    property!.SetValue(instance, value);
}

static object? GetProperty(Type type, object instance, string name)
{
    var property = type.GetProperty(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(property != null, $"Property not found: {type.FullName}.{name}");
    return property!.GetValue(instance);
}

static object? GetFieldValue(Type type, object instance, string name)
{
    var field = type.GetField(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(field != null, $"Field not found: {type.FullName}.{name}");
    return field!.GetValue(instance);
}

static object CreateSnapshotList(string instanceId, string templateId)
{
    var snapshotType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayCardSnapshot");
    var listType = typeof(List<>).MakeGenericType(snapshotType);
    var list =
        Activator.CreateInstance(listType)
        ?? throw new InvalidOperationException("Snapshot list should be constructible.");
    var snapshot =
        Activator.CreateInstance(snapshotType)
        ?? throw new InvalidOperationException("Snapshot should be constructible.");
    SetProperty(snapshotType, snapshot, "InstanceId", instanceId);
    SetProperty(snapshotType, snapshot, "TemplateId", templateId);
    SetProperty(
        snapshotType,
        snapshot,
        "Type",
        ParseEnum("BazaarGameShared.Domain.Core.Types.ECardType", "BazaarGameShared", "Skill")
    );
    SetProperty(
        snapshotType,
        snapshot,
        "Name",
        instanceId.StartsWith("p-skill", StringComparison.Ordinal) ? "Arcane Mastery" : "Sparkblade"
    );
    SetProperty(snapshotType, snapshot, "Tier", "Gold");
    SetProperty(snapshotType, snapshot, "Enchant", "Radiant");
    SetProperty(snapshotType, snapshot, "Tags", new List<string> { "Weapon", "Burst" });
    SetProperty(
        snapshotType,
        snapshot,
        "Attributes",
        new Dictionary<string, int> { ["Damage"] = 42, ["Cooldown"] = 3 }
    );
    Invoke(listType, list, "Add", new[] { snapshot });
    return list;
}

static object CreateEmptySnapshotList()
{
    var snapshotType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayCardSnapshot");
    var listType = typeof(List<>).MakeGenericType(snapshotType);
    return Activator.CreateInstance(listType)
        ?? throw new InvalidOperationException("Empty snapshot list should be constructible.");
}

static object CreateCardSetCapture(
    Type cardSetCaptureType,
    Type captureStatusType,
    Type captureSourceType,
    string status,
    string source,
    object items
)
{
    var capture =
        Activator.CreateInstance(cardSetCaptureType)
        ?? throw new InvalidOperationException("PvpBattleCardSetCapture should be constructible.");
    SetProperty(cardSetCaptureType, capture, "Items", items);
    SetProperty(cardSetCaptureType, capture, "Status", Enum.Parse(captureStatusType, status));
    SetProperty(cardSetCaptureType, capture, "Source", Enum.Parse(captureSourceType, source));
    return capture;
}

static object CreateManifestFixture(
    Type manifestType,
    Type cardSetCaptureType,
    Type captureStatusType,
    Type captureSourceType,
    string battleId,
    string runId,
    DateTimeOffset savedAtUtc,
    string combatKind,
    int day,
    int hour,
    string encounterId,
    string playerName,
    string playerAccountId,
    string opponentName,
    string opponentHero,
    string opponentRank,
    int opponentRating,
    int opponentLevel,
    string opponentAccountId,
    string result,
    string winnerCombatantId,
    string loserCombatantId,
    object playerHandCapture,
    object playerSkillsCapture,
    object opponentHandCapture,
    object opponentSkillsCapture
)
{
    var participantsType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleParticipants");
    var outcomeType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleOutcome");
    var snapshotsType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSnapshots");

    var participants = Activator.CreateInstance(participantsType)!;
    SetProperty(participantsType, participants, "PlayerName", playerName);
    SetProperty(participantsType, participants, "PlayerAccountId", playerAccountId);
    SetProperty(participantsType, participants, "OpponentName", opponentName);
    SetProperty(participantsType, participants, "OpponentHero", opponentHero);
    SetProperty(participantsType, participants, "OpponentRank", opponentRank);
    SetProperty(participantsType, participants, "OpponentRating", opponentRating);
    SetProperty(participantsType, participants, "OpponentLevel", opponentLevel);
    SetProperty(participantsType, participants, "OpponentAccountId", opponentAccountId);

    var outcome = Activator.CreateInstance(outcomeType)!;
    SetProperty(outcomeType, outcome, "Result", result);
    SetProperty(outcomeType, outcome, "WinnerCombatantId", winnerCombatantId);
    SetProperty(outcomeType, outcome, "LoserCombatantId", loserCombatantId);

    var snapshots = Activator.CreateInstance(snapshotsType)!;
    SetProperty(snapshotsType, snapshots, "PlayerHand", playerHandCapture);
    SetProperty(snapshotsType, snapshots, "PlayerSkills", playerSkillsCapture);
    SetProperty(snapshotsType, snapshots, "OpponentHand", opponentHandCapture);
    SetProperty(snapshotsType, snapshots, "OpponentSkills", opponentSkillsCapture);

    var manifest = Activator.CreateInstance(manifestType)!;
    SetProperty(manifestType, manifest, "BattleId", battleId);
    SetProperty(manifestType, manifest, "RunId", runId);
    SetProperty(manifestType, manifest, "SavedAtUtc", savedAtUtc);
    SetProperty(manifestType, manifest, "CombatKind", combatKind);
    SetProperty(manifestType, manifest, "Day", day);
    SetProperty(manifestType, manifest, "Hour", hour);
    SetProperty(manifestType, manifest, "EncounterId", encounterId);
    SetProperty(manifestType, manifest, "Participants", participants);
    SetProperty(manifestType, manifest, "Outcome", outcome);
    SetProperty(manifestType, manifest, "Snapshots", snapshots);
    return manifest;
}

static int ReadSnapshotCount(Type recordType, object instance, string propertyName)
{
    return ((System.Collections.IEnumerable?)GetProperty(recordType, instance, propertyName))
            ?.Cast<object>()
            .Count()
        ?? 0;
}

static long CountRows(SqliteConnection connection, string tableName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
    return (long)(command.ExecuteScalar() ?? 0L);
}

static string GetString(SqliteConnection connection, string sql, string battleId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$battleId", battleId);
    return (string)(
        command.ExecuteScalar()
        ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
}

static int GetInt32(SqliteConnection connection, string sql, string battleId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$battleId", battleId);
    return Convert.ToInt32(
        command.ExecuteScalar()
            ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
}

static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info({tableName});";
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        if (
            string.Equals(
                reader.GetString(reader.GetOrdinal("name")),
                columnName,
                StringComparison.Ordinal
            )
        )
            return true;
    }

    return false;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static string ExtractMethodBody(string source, string signature)
{
    var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
    Assert(signatureIndex >= 0, $"Method signature not found: {signature}");

    var bodyStart = source.IndexOf('{', signatureIndex);
    Assert(bodyStart >= 0, $"Method body start not found: {signature}");

    var depth = 0;
    for (var index = bodyStart; index < source.Length; index++)
    {
        if (source[index] == '{')
            depth++;
        else if (source[index] == '}')
            depth--;

        if (depth == 0)
            return source.Substring(bodyStart + 1, index - bodyStart - 1);
    }

    throw new InvalidOperationException($"Method body end not found: {signature}");
}

static object CreateGameSimMessage(
    string stateName,
    uint day,
    uint hour,
    string? encounterId,
    string? opponentName
)
{
    var gameSimType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.GameSim",
        "BazaarGameShared"
    );
    var simUpdateRunType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.SimUpdateRun",
        "BazaarGameShared"
    );
    var simUpdateRunStateType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.SimUpdateRunState",
        "BazaarGameShared"
    );
    var runStateType = RequireExternalType(
        "BazaarGameShared.Domain.Runs.ERunState",
        "BazaarGameShared"
    );
    var simPvpOpponentType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.SimPvpOpponent",
        "BazaarGameShared"
    );
    var loadoutType = RequireExternalType(
        "BazaarGameShared.TempoNet.Models.BazaarCollectionLoadout",
        "BazaarGameShared"
    );
    var netMessageGameSimType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.NetMessageGameSim",
        "BazaarGameShared"
    );

    var gameSim = Activator.CreateInstance(gameSimType)!;
    var run = Activator.CreateInstance(simUpdateRunType)!;
    SetField(simUpdateRunType, run, "Day", day);
    SetField(simUpdateRunType, run, "Hour", hour);
    SetField(gameSimType, gameSim, "Run", run);

    var runState = Activator.CreateInstance(simUpdateRunStateType)!;
    SetField(
        simUpdateRunStateType,
        runState,
        "StateName",
        Enum.Parse(runStateType, stateName, ignoreCase: false)
    );
    SetField(simUpdateRunStateType, runState, "CurrentEncounterId", encounterId);
    SetField(
        simUpdateRunStateType,
        runState,
        "PvpOpponent",
        opponentName == null
            ? null
            : CreatePvpOpponent(simPvpOpponentType, loadoutType, opponentName)
    );
    SetField(gameSimType, gameSim, "CurrentState", runState);

    return Activator.CreateInstance(netMessageGameSimType, gameSim)!;
}

static object CreateCombatSimMessage()
{
    var combatSimType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.CombatSimEvents.CombatSim",
        "BazaarGameShared"
    );
    var netMessageCombatSimType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.NetMessageCombatSim",
        "BazaarGameShared"
    );
    var combatSim = Activator.CreateInstance(combatSimType)!;
    return Activator.CreateInstance(netMessageCombatSimType, combatSim)!;
}

static object CreatePvpOpponent(Type simPvpOpponentType, Type loadoutType, string opponentName)
{
    var loadout = Activator.CreateInstance(loadoutType)!;
    SetField(loadoutType, loadout, "accountId", "opponent-account-live");
    return Activator.CreateInstance(
        simPvpOpponentType,
        opponentName,
        null,
        null,
        ParseEnum("BazaarGameShared.TempoNet.Enums.ERank", "BazaarGameShared", "Gold"),
        1337,
        null,
        null,
        null,
        9,
        ParseEnum("BazaarGameShared.Domain.Core.Types.EHero", "BazaarGameShared", "Vanessa"),
        loadout,
        null
    )!;
}

static Type RequireExternalType(string fullName, string assemblyName)
{
    return Type.GetType($"{fullName}, {assemblyName}")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static object ParseEnum(string fullName, string assemblyName, string name)
{
    var enumType = RequireExternalType(fullName, assemblyName);
    return Enum.Parse(enumType, name);
}

static void SetField(Type type, object instance, string name, object? value)
{
    var field = type.GetField(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(field != null, $"Field not found: {type.FullName}.{name}");
    field!.SetValue(instance, value);
}

file sealed class QueuePersistenceHarness
{
    private int _payloadSaveCallCount;

    public ManualResetEventSlim FirstPayloadStarted { get; } = new(initialState: false);

    public ManualResetEventSlim AllowFirstPayloadToComplete { get; } = new(initialState: false);

    public ConcurrentQueue<string> SavedManifestBattleIds { get; } = new();

    public ConcurrentQueue<string> DeletedPayloadBattleIds { get; } = new();

    public Delegate CreateSavePayloadDelegate(Type delegateType)
    {
        return CreateObjectForwardingDelegate(delegateType, nameof(SavePayload));
    }

    public Delegate CreateSaveManifestDelegate(Type delegateType)
    {
        return CreateObjectForwardingDelegate(delegateType, nameof(SaveManifest));
    }

    public void SavePayload(object payload)
    {
        var battleId = ReadBattleId(payload);
        if (Interlocked.Increment(ref _payloadSaveCallCount) == 1)
        {
            FirstPayloadStarted.Set();
            AllowFirstPayloadToComplete.Wait(TimeSpan.FromSeconds(5));
        }

        if (string.IsNullOrWhiteSpace(battleId))
            throw new InvalidOperationException("Replay payload should expose a battle id.");
    }

    public void SaveManifest(object manifest)
    {
        SavedManifestBattleIds.Enqueue(ReadBattleId(manifest));
    }

    public void DeletePayload(string battleId)
    {
        DeletedPayloadBattleIds.Enqueue(battleId);
    }

    private static string ReadBattleId(object instance)
    {
        return (string)(
            instance
                .GetType()
                .GetProperty("BattleId", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(instance)
            ?? throw new InvalidOperationException("BattleId property not found.")
        );
    }

    private Delegate CreateObjectForwardingDelegate(Type delegateType, string methodName)
    {
        var argumentType = delegateType.GenericTypeArguments.Single();
        var parameter = Expression.Parameter(argumentType, "value");
        var body = Expression.Call(
            Expression.Constant(this),
            GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Method not found: {methodName}"),
            Expression.Convert(parameter, typeof(object))
        );
        return Expression.Lambda(delegateType, body, parameter).Compile();
    }
}
