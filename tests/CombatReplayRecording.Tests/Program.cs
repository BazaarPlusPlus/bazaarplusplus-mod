using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.Sqlite;

var payloadStoreType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayPayloadStore");
var captureServiceType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayCaptureService");
var loaderType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayLoader");
var controllerType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayController");
var artifactType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureArtifact");
var manifestType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifest");
var payloadType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayload");
var candidateType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSequenceCandidate");
var cardSetCaptureType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCardSetCapture");
var captureStatusType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureStatus");
var captureSourceType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureSource");
var matcherType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSequenceMatcher");
var collectorType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSnapshotCollector");
var cardDisplayNameType = RequireType("BazaarPlusPlus.GameInterop.StaticCards.BppCardDisplayName");
var manifestFactoryType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifestFactory");
var payloadFactoryType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayloadFactory");
var catalogType = RequireType("BazaarPlusPlus.Game.PvpBattles.Persistence.PvpBattleCatalog");
var catalogInterfaceType = RequireType(
    "BazaarPlusPlus.Game.PvpBattles.Persistence.IPvpBattleCatalog"
);
var catalogStoreType = RequireType(
    "BazaarPlusPlus.Game.PvpBattles.Persistence.PvpBattleSqliteStore"
);
var uploadStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunBundleUploadStore");
var buildBattleProjectionMethod = uploadStoreType.GetMethod(
    "BuildBattleProjection",
    BindingFlags.NonPublic | BindingFlags.Static
);
var buildArtifactBattleMethod = uploadStoreType.GetMethod(
    "BuildArtifactBattle",
    BindingFlags.NonPublic | BindingFlags.Static
);
var audioTapStopperType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.Audio.ReplayAudioTapStopper"
);
var muxerType = RequireType("BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoAudioMuxer");
var muxResultType =
    muxerType.GetNestedType("MuxResult", BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("ReplayVideoAudioMuxer.MuxResult should exist.");
var replayVideoCaptureStatusType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoCaptureStatus"
);
var replaySavedStateNormalizerType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.Bootstrap.ReplaySavedStateNormalizer"
);
var replayOpeningStateRestorerType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.Bootstrap.ReplayOpeningStateRestorer"
);
var replayRunEconomyFallbackType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.Bootstrap.ReplayRunEconomyFallback"
);
var snapshotRehydratorType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.Bootstrap.SnapshotRehydrator"
);
var replayNativeBoardPresentationType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.PlaybackUi.ReplayNativeBoardPresentation"
);
var playerAttributeRepairerType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.PlaybackUi.PlayerAttributeRepairer"
);
RunReplaySavedStateNormalizationChecks(replaySavedStateNormalizerType, manifestType);
RunReplayOpeningStateSelectionChecks(replayOpeningStateRestorerType);
RunReplayRunEconomyFallbackChecks(replayRunEconomyFallbackType, manifestType);
RunReplayPresentationRestorationChecks(replaySavedStateNormalizerType);
RunReplaySpawnSanitizationChecks(snapshotRehydratorType);
RunReplayNativeBoardPresentationChecks(replayNativeBoardPresentationType);
RunPortraitTimingSubscriptionDeduplicationChecks(playerAttributeRepairerType);

RunCurrentReplayRecordingStateChecks();
RunReplayVideoPreflightReasonChecks();
RunCurrentReplayRecordingUiLogChecks();
RunCurrentReplayVideoMetadataChecks();
RunReplayVideoSyncCollectorChecks();
RunSystemFileRevealCommandChecks();
ReportProjectionChecks.Run();

static void RunReplayNativeBoardPresentationChecks(Type presentationType)
{
    Assert(
        presentationType.GetMethod(
            "RebuildOpponentCollectiblesAsync",
            BindingFlags.NonPublic | BindingFlags.Static
        ) != null,
        "Saved replay presentation should explicitly rebuild native PVP collectables after ReplayState is active."
    );
    Assert(
        (bool)InvokeStatic(presentationType, "ShouldShowOpponentBank", new object?[] { false })!,
        "Saved replay playback should keep the opponent bank visible while replay controls are hidden."
    );
    Assert(
        !(bool)InvokeStatic(presentationType, "ShouldShowOpponentBank", new object?[] { true })!,
        "Saved replay playback should hide the opponent bank while replay controls are visible."
    );
}

static void RunPortraitTimingSubscriptionDeduplicationChecks(Type repairerType)
{
    var removedCalls = 0;
    var preservedCalls = 0;
    Action duplicateHandler = () => removedCalls++;
    Action unrelatedHandler = () => preservedCalls++;
    var subscriptions = Delegate.Combine(
        duplicateHandler,
        unrelatedHandler,
        duplicateHandler,
        duplicateHandler
    );

    var remaining = (Delegate?)InvokeStatic(
        repairerType,
        "RemoveAllMatchingHandlers",
        new object?[] { subscriptions, duplicateHandler }
    );
    remaining?.DynamicInvoke();

    Assert(
        removedCalls == 0,
        "Replay BoardUI re-initialization must remove every prior PortraitTimingReady handler owned by that controller."
    );
    Assert(
        preservedCalls == 1,
        "PortraitTimingReady deduplication must preserve unrelated subscribers."
    );
}

Assert(
    (bool)InvokeStatic(audioTapStopperType, "IsUsable", new object?[] { false, "present.wav" })!
        == false,
    "ReplayAudioTapStopper.IsUsable should reject taps that captured no samples."
);
var usableWavRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-audio-tap-stopper-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(usableWavRoot);
try
{
    var presentWavPath = Path.Combine(usableWavRoot, "present.wav");
    File.WriteAllBytes(presentWavPath, [1]);
    Assert(
        (bool)
            InvokeStatic(audioTapStopperType, "IsUsable", new object?[] { true, presentWavPath })!,
        "ReplayAudioTapStopper.IsUsable should accept captured taps with an existing WAV."
    );
    Assert(
        (bool)
            InvokeStatic(
                audioTapStopperType,
                "IsUsable",
                new object?[] { true, Path.Combine(usableWavRoot, "missing.wav") }
            )! == false,
        "ReplayAudioTapStopper.IsUsable should reject missing WAV files."
    );
}
finally
{
    Directory.Delete(usableWavRoot, recursive: true);
}

Assert(
    muxerType.GetConstructor(Type.EmptyTypes) != null,
    "ReplayVideoAudioMuxer should not require an ffmpeg executable at construction."
);
Assert(
    muxerType
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .All(method =>
            method.Name != "MuxOrPromote"
            && (
                method.Name != "DispatchAsync"
                || method
                    .GetParameters()
                    .All(parameter => parameter.ParameterType != typeof(string))
            )
        ),
    "ReplayVideoAudioMuxer should remove the old public single-WAV dispatch and MuxOrPromote surfaces."
);
Assert(
    muxerType
        .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
        .All(field => field.Name != "_ffmpegExecutable"),
    "ReplayVideoAudioMuxer should not store a constructor-resolved ffmpeg executable."
);
var muxer =
    Activator.CreateInstance(muxerType)
    ?? throw new InvalidOperationException("ReplayVideoAudioMuxer should be constructible.");
var failedStatus = Enum.Parse(replayVideoCaptureStatusType, "Failed");
var completedStatus = Enum.Parse(replayVideoCaptureStatusType, "Completed");
var muxResolveRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-mux-resolve-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(muxResolveRoot);
try
{
    var failedTempPath = Path.Combine(muxResolveRoot, "failed.recording.mp4");
    var failedFinalPath = Path.Combine(muxResolveRoot, "failed.mp4");
    File.WriteAllBytes(failedTempPath, [1, 2, 3]);
    var failedResolution = InvokeResolve(
        muxerType,
        muxer,
        failedStatus,
        failedTempPath,
        failedFinalPath,
        Array.Empty<string>(),
        ffmpegExecutable: null
    );
    AssertMuxReason(muxResultType, failedResolution, "CaptureFailed");
    Assert(!File.Exists(failedTempPath), "Not-completed Resolve should delete the temp recording.");

    var noAudioTempPath = Path.Combine(muxResolveRoot, "no-audio.recording.mp4");
    var noAudioFinalPath = Path.Combine(muxResolveRoot, "no-audio.mp4");
    File.WriteAllBytes(noAudioTempPath, [4, 5, 6, 7]);
    var noAudioResolution = InvokeResolve(
        muxerType,
        muxer,
        completedStatus,
        noAudioTempPath,
        noAudioFinalPath,
        Array.Empty<string>(),
        ffmpegExecutable: "/should/not/be/used"
    );
    AssertMuxReason(muxResultType, noAudioResolution, "NoAudio");
    Assert(
        File.Exists(noAudioFinalPath) && !File.Exists(noAudioTempPath),
        "No-audio Resolve should promote the silent temp to the final path inline."
    );

    var noFfmpegTempPath = Path.Combine(muxResolveRoot, "no-ffmpeg.recording.mp4");
    var noFfmpegFinalPath = Path.Combine(muxResolveRoot, "no-ffmpeg.mp4");
    var wavPath = Path.Combine(muxResolveRoot, "no-ffmpeg.wav");
    File.WriteAllBytes(noFfmpegTempPath, [8, 9, 10, 11]);
    File.WriteAllBytes(wavPath, [1]);
    var noFfmpegResolution = InvokeResolve(
        muxerType,
        muxer,
        completedStatus,
        noFfmpegTempPath,
        noFfmpegFinalPath,
        new[] { wavPath },
        ffmpegExecutable: null
    );
    AssertMuxReason(muxResultType, noFfmpegResolution, "FfmpegUnavailable");
    Assert(
        File.Exists(noFfmpegFinalPath) && !File.Exists(noFfmpegTempPath) && !File.Exists(wavPath),
        "No-ffmpeg Resolve should promote silent video and delete the usable WAV."
    );

    var muxFailureTempPath = Path.Combine(muxResolveRoot, "mux-failure.recording.mp4");
    var muxFailureFinalPath = Path.Combine(muxResolveRoot, "mux-failure.mp4");
    var muxFailureWavPath = Path.Combine(muxResolveRoot, "mux-failure.wav");
    File.WriteAllBytes(muxFailureTempPath, [12, 13, 14, 15]);
    File.WriteAllBytes(muxFailureWavPath, [1]);
    var muxFailureResolution = InvokeResolve(
        muxerType,
        muxer,
        completedStatus,
        muxFailureTempPath,
        muxFailureFinalPath,
        new[] { muxFailureWavPath },
        ffmpegExecutable: Path.Combine(muxResolveRoot, "missing-ffmpeg")
    );
    AssertMuxReason(muxResultType, muxFailureResolution, "UnexpectedException");
    Assert(
        File.Exists(muxFailureFinalPath)
            && !File.Exists(muxFailureTempPath)
            && !File.Exists(muxFailureWavPath),
        "Resolve should finish its fallback inline when the mux process cannot start."
    );
}
finally
{
    Directory.Delete(muxResolveRoot, recursive: true);
}

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
Assert(manifestType.GetProperty("RecordedAtUtc") != null, "Manifest should expose RecordedAtUtc.");
Assert(
    manifestType.GetProperty("SavedAtUtc") == null,
    "Manifest should not expose SavedAtUtc once replay timestamps follow the server contract directly."
);
Assert(manifestType.GetProperty("ReplayId") == null, "Manifest should not expose ReplayId.");
Assert(payloadType.GetProperty("BattleId") != null, "Replay payload should expose BattleId.");
Assert(
    cardSetCaptureType.GetProperty("Items") != null
        && cardSetCaptureType.GetProperty("Status") != null
        && cardSetCaptureType.GetProperty("Source") != null,
    "PvpBattleCardSetCapture should expose Items, Status, and Source."
);
var shouldRefreshPlayerCaptureMethod = collectorType.GetMethod(
    "ShouldRefreshPlayerCapture",
    BindingFlags.NonPublic | BindingFlags.Static
);
var resolveCardDisplayNameTextMethod = cardDisplayNameType.GetMethod(
    "ResolveText",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    shouldRefreshPlayerCaptureMethod != null,
    "PvpBattleSnapshotCollector should keep player capture refresh logic testable."
);
Assert(
    resolveCardDisplayNameTextMethod != null,
    "The shared card display-name resolver should keep its precedence directly testable."
);
Assert(
    (string?)
        resolveCardDisplayNameTextMethod!.Invoke(
            null,
            new object?[] { " Localized title ", "Snapshot fallback", "InternalName" }
        ) == "Localized title",
    "Localized card titles should win over snapshot and internal-name fallbacks."
);
Assert(
    (string?)
        resolveCardDisplayNameTextMethod.Invoke(
            null,
            new object?[] { " ", " Snapshot fallback ", "InternalName" }
        ) == "Snapshot fallback",
    "Snapshot names should fill missing localized titles."
);
Assert(
    (string?)
        resolveCardDisplayNameTextMethod.Invoke(
            null,
            new object?[] { null, null, " InternalName " }
        ) == "InternalName",
    "Internal names should be the final non-empty display fallback."
);
Assert(
    catalogType.GetMethod("Save") != null
        && catalogType.GetMethod("TryLoad") != null
        && catalogType.GetMethod("ListRecentBattles") != null,
    "The PVP battle catalog read side should expose Save, TryLoad, and ListRecentBattles."
);
Assert(
    catalogType.GetMethod("Delete") != null
        && catalogType.GetMethod("ListBattleIds") != null
        && catalogInterfaceType.GetMethod("Delete") != null
        && catalogInterfaceType.GetMethod("ListBattleIds") != null,
    "The PVP battle catalog should expose Delete and ListBattleIds so replay maintenance can reconcile stored manifests when needed."
);
Assert(
    buildBattleProjectionMethod != null && buildArtifactBattleMethod != null,
    "RunBundleUploadStore should keep battle projection and artifact mapping testable."
);
var persistenceQueueType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.CombatReplayPersistenceQueue"
);
var persistenceResultType = RequireType(
    "BazaarPlusPlus.Game.CombatReplay.CombatReplayPersistenceResult"
);
var queueCtor = persistenceQueueType.GetConstructor([
    typeof(Action<>).MakeGenericType(payloadType),
    typeof(Action<>).MakeGenericType(manifestType),
    typeof(Action<string>),
]);
Assert(
    queueCtor != null,
    "Combat replay persistence queue should accept payload-save, manifest-save, and payload-delete callbacks."
);
var queueHarness = new QueuePersistenceHarness();
var queue = queueCtor!.Invoke([
    queueHarness.CreateSavePayloadDelegate(typeof(Action<>).MakeGenericType(payloadType)),
    queueHarness.CreateSaveManifestDelegate(typeof(Action<>).MakeGenericType(manifestType)),
    new Action<string>(queueHarness.DeletePayload),
]);
Assert(queue != null, "Combat replay persistence queue should be constructible.");

var openingEmptySnapshots = (System.Collections.IList)CreateEmptySnapshotList();
Assert(
    (bool)(
        shouldRefreshPlayerCaptureMethod!.Invoke(
            null,
            new object?[] { true, false, openingEmptySnapshots }
        ) ?? false
    ),
    "Player snapshots captured as empty at combat opening should still be eligible for live retry."
);
var populatedSnapshots = (System.Collections.IList)CreateSnapshotList("refresh-1", "tpl-refresh");
Assert(
    !(
        (bool)(
            shouldRefreshPlayerCaptureMethod.Invoke(
                null,
                new object?[] { true, false, populatedSnapshots }
            ) ?? false
        )
    ),
    "Player snapshots captured with opening data should not request a redundant live retry."
);
Assert(
    !(
        (bool)(
            shouldRefreshPlayerCaptureMethod.Invoke(
                null,
                new object?[] { false, true, openingEmptySnapshots }
            ) ?? false
        )
    ),
    "Player snapshots should not request another refresh after live retry already ran."
);

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

Invoke(persistenceQueueType, queue!, "Enqueue", new object?[] { slowPayload!, slowManifest! });
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
var shutdownStopwatch = Stopwatch.StartNew();
Assert(
    disposeTask.Wait(TimeSpan.FromSeconds(2)),
    "Disposing the persistence queue should stop waiting after the shutdown timeout instead of blocking on an in-flight replay write."
);
shutdownStopwatch.Stop();
Assert(
    shutdownStopwatch.Elapsed < TimeSpan.FromSeconds(2),
    "Disposing the persistence queue should return on the bounded shutdown path."
);

var abandonedResults = new List<object>();
while (TryDequeuePersistenceResult(persistenceQueueType, queue!, out var abandonedResult))
{
    abandonedResults.Add(abandonedResult!);
}

Assert(
    abandonedResults.Count == 1,
    "Timed-out shutdown should immediately surface one abandoned result for queued replay work."
);
Assert(
    abandonedResults.Any(result =>
        !(bool)GetRequiredProperty(persistenceResultType, result, "Succeeded")
        && string.Equals(
            (string?)GetProperty(
                manifestType,
                GetRequiredProperty(persistenceResultType, result, "Manifest"),
                "BattleId"
            ),
            "battle-dispose-abandoned",
            StringComparison.Ordinal
        )
    ),
    "Timed-out shutdown should convert queued replay work into a completed failure result."
);

queueHarness.AllowFirstPayloadToComplete.Set();
Assert(
    SpinWait.SpinUntil(
        () => queueHarness.SavedManifestBattleIds.Contains("battle-dispose-slow"),
        millisecondsTimeout: 5000
    ),
    "The in-flight replay write should still be allowed to finish asynchronously after shutdown returns."
);

var queueResults = new List<object>();
object? firstCompletedResult = null;
Assert(
    SpinWait.SpinUntil(
        () => TryDequeuePersistenceResult(persistenceQueueType, queue!, out firstCompletedResult),
        millisecondsTimeout: 5000
    ),
    "Queue disposal should eventually surface the successful in-flight replay result."
);
if (firstCompletedResult != null)
    queueResults.Add(firstCompletedResult);
while (TryDequeuePersistenceResult(persistenceQueueType, queue!, out var result))
{
    queueResults.Add(result!);
}

Assert(
    queueResults.Any(result =>
        (bool)GetRequiredProperty(persistenceResultType, result, "Succeeded")
        && string.Equals(
            (string?)GetProperty(
                manifestType,
                GetRequiredProperty(persistenceResultType, result, "Manifest"),
                "BattleId"
            ),
            "battle-dispose-slow",
            StringComparison.Ordinal
        )
    ),
    "Queue disposal should preserve the successful in-flight replay result even when shutdown returns before it finishes."
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
    !(bool)GetRequiredProperty(persistenceQueueType, queue!, "HasPendingPersistence"),
    "After draining the completed queue results from disposal, replay persistence should no longer report outstanding work."
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
    SetProperty(payloadType, payload!, "SpawnMessageBytes", new byte[] { 1, 2, 3 });
    SetProperty(payloadType, payload!, "CombatMessageBytes", new byte[] { 4, 5, 6 });
    SetProperty(payloadType, payload!, "DespawnMessageBytes", new byte[] { 7, 8, 9 });
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
        ((byte[]?)GetProperty(payloadType, loadedPayload!, "CombatMessageBytes"))?.SequenceEqual(
            new byte[] { 4, 5, 6 }
        ) == true,
        "Payload store should preserve the serialized combat payload."
    );
    var payloadFilesAfterFirstSave = Directory
        .EnumerateFiles(tempRoot, "battle-001.payload.mpack.gz*")
        .Select(Path.GetFileName)
        .ToArray();
    Assert(
        payloadFilesAfterFirstSave.SequenceEqual(["battle-001.payload.mpack.gz"]),
        "Payload store should not leave temporary files behind after the initial atomic save."
    );

    SetProperty(payloadType, payload!, "CombatMessageBytes", new byte[] { 9, 8, 7 });
    Invoke(payloadStoreType, payloadStore!, "Save", new object?[] { payload! });
    var reloadedPayload = Invoke(
        payloadStoreType,
        payloadStore!,
        "Load",
        new object?[] { "battle-001" }
    );
    Assert(reloadedPayload != null, "Payload store should reload an overwritten payload.");
    Assert(
        ((byte[]?)GetProperty(payloadType, reloadedPayload!, "CombatMessageBytes"))?.SequenceEqual(
            new byte[] { 9, 8, 7 }
        ) == true,
        "Atomic overwrite should publish the latest replay payload bytes."
    );
    var payloadFilesAfterOverwrite = Directory
        .EnumerateFiles(tempRoot, "battle-001.payload.mpack.gz*")
        .Select(Path.GetFileName)
        .ToArray();
    Assert(
        payloadFilesAfterOverwrite.SequenceEqual(["battle-001.payload.mpack.gz"]),
        "Atomic overwrite should replace the target file without leaving temp artifacts behind."
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
        playerPrestige: 18,
        playerIncome: 11,
        playerGold: 99,
        playerVictories: 3,
        opponentName: "Test Opponent",
        opponentHero: "Vanessa",
        opponentRank: "Legend",
        opponentRating: 2048,
        opponentLevel: 12,
        opponentPrestige: 12,
        opponentVictories: 6,
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
    var battleProjection =
        buildBattleProjectionMethod!.Invoke(null, [manifest!, false])
        ?? throw new InvalidOperationException("BuildBattleProjection should return a projection.");
    var battleProjectionType = battleProjection.GetType();
    Assert(
        Equals(GetProperty(battleProjectionType, battleProjection, "PlayerPrestige"), 18)
            && Equals(GetProperty(battleProjectionType, battleProjection, "PlayerIncome"), 11)
            && Equals(GetProperty(battleProjectionType, battleProjection, "PlayerGold"), 99)
            && Equals(GetProperty(battleProjectionType, battleProjection, "PlayerVictories"), 3)
            && Equals(GetProperty(battleProjectionType, battleProjection, "OpponentPrestige"), 12)
            && Equals(GetProperty(battleProjectionType, battleProjection, "OpponentVictories"), 6),
        "Run bundle battle projection should include participant prestige and victories."
    );
    Assert(
        Equals(GetProperty(battleProjectionType, battleProjection, "IsFinalBattle"), false),
        "Run bundle battle projection should accept the final-battle marker from the caller."
    );
    var artifactBattle =
        buildArtifactBattleMethod!.Invoke(null, [manifest!, payload!])
        ?? throw new InvalidOperationException("BuildArtifactBattle should return an artifact.");
    var artifactBattleType = artifactBattle.GetType();
    var artifactParticipants =
        GetProperty(artifactBattleType, artifactBattle, "Participants")
        ?? throw new InvalidOperationException("Artifact battle should include participants.");
    var artifactParticipantsType = artifactParticipants.GetType();
    Assert(
        Equals(GetProperty(artifactParticipantsType, artifactParticipants, "PlayerPrestige"), 18)
            && Equals(
                GetProperty(artifactParticipantsType, artifactParticipants, "PlayerIncome"),
                11
            )
            && Equals(GetProperty(artifactParticipantsType, artifactParticipants, "PlayerGold"), 99)
            && Equals(
                GetProperty(artifactParticipantsType, artifactParticipants, "PlayerVictories"),
                3
            )
            && Equals(
                GetProperty(artifactParticipantsType, artifactParticipants, "OpponentPrestige"),
                12
            )
            && Equals(
                GetProperty(artifactParticipantsType, artifactParticipants, "OpponentVictories"),
                6
            ),
        "Run bundle artifact participants should include participant prestige and victories."
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
    var loadedParticipants =
        GetProperty(manifestType, loadedManifest!, "Participants")
        ?? throw new InvalidOperationException("Loaded manifest should include participants.");
    var loadedParticipantsType = loadedParticipants.GetType();
    Assert(
        Equals(GetProperty(loadedParticipantsType, loadedParticipants, "PlayerPrestige"), 18)
            && Equals(GetProperty(loadedParticipantsType, loadedParticipants, "PlayerIncome"), 11)
            && Equals(GetProperty(loadedParticipantsType, loadedParticipants, "PlayerGold"), 99)
            && Equals(GetProperty(loadedParticipantsType, loadedParticipants, "PlayerVictories"), 3)
            && Equals(
                GetProperty(loadedParticipantsType, loadedParticipants, "OpponentPrestige"),
                12
            )
            && Equals(
                GetProperty(loadedParticipantsType, loadedParticipants, "OpponentVictories"),
                6
            ),
        "Catalog should preserve battle participant prestige and victories."
    );

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            CountRows(connection, "battles") == 1,
            "Saving a PVP battle manifest should insert one row into battles."
        );
        Assert(
            CountRows(connection, "battle_snapshots") == 1,
            "Saving a PVP battle manifest should persist one snapshot row."
        );
        Assert(
            GetString(
                connection,
                "SELECT player_name FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Local Player",
            "battles should persist the player name."
        );
        Assert(
            GetString(
                connection,
                "SELECT player_account_id FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "player-account-001",
            "battles should persist the player account id."
        );
        Assert(
            GetString(
                connection,
                "SELECT opponent_account_id FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "opponent-account-001",
            "battles should persist the opponent account id."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT player_prestige FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 18,
            "battles should persist the player prestige."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT player_income FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 11,
            "battles should persist the player income."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT player_gold FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 99,
            "battles should persist the player gold."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT player_victories FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 3,
            "battles should persist the player victories."
        );
        Assert(
            GetString(
                connection,
                "SELECT opponent_hero FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Vanessa",
            "battles should persist the opponent hero."
        );
        Assert(
            GetString(
                connection,
                "SELECT opponent_rank FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Legend",
            "battles should persist the opponent rank."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT opponent_rating FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 2048,
            "battles should persist the opponent rating."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT opponent_level FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 12,
            "battles should persist the opponent level."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT opponent_prestige FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 12,
            "battles should persist the opponent prestige."
        );
        Assert(
            GetInt32(
                connection,
                "SELECT opponent_victories FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == 6,
            "battles should persist the opponent victories."
        );
        Assert(
            GetString(
                connection,
                "SELECT result FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "win",
            "battles should persist the player's combat result."
        );
        Assert(
            GetString(
                connection,
                "SELECT winner_combatant_id FROM battles WHERE battle_id = $battleId;",
                "battle-001"
            ) == "Player",
            "battles should persist the winner combatant id."
        );
        var playerHandJson = GetString(
            connection,
            "SELECT player_hand_json FROM battle_snapshots WHERE battle_id = $battleId;",
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
            "battle_snapshots should persist detailed capture objects for hand-card metadata."
        );
        var playerSkillsJson = GetString(
            connection,
            "SELECT player_skills_json FROM battle_snapshots WHERE battle_id = $battleId;",
            "battle-001"
        );
        Assert(
            playerSkillsJson.Contains("Missing", StringComparison.Ordinal)
                && playerSkillsJson.Contains("Unknown", StringComparison.Ordinal)
                && !playerSkillsJson.Contains("CapturedEmpty", StringComparison.Ordinal),
            "battle_snapshots should preserve explicit missing capture semantics instead of guessing from empty lists."
        );
    }

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        using var createRun = connection.CreateCommand();
        createRun.CommandText = """
            INSERT INTO runs (
                run_id,
                started_at_utc,
                last_seen_at_utc,
                status,
                completed,
                hero,
                game_mode,
                last_seq
            ) VALUES (
                $runId,
                $startedAtUtc,
                $lastSeenAtUtc,
                'active',
                0,
                'Vanessa',
                'Ranked',
                0
            );
            """;
        createRun.Parameters.AddWithValue("$runId", "run-001");
        createRun.Parameters.AddWithValue("$startedAtUtc", "2026-03-18T01:00:00.0000000+00:00");
        createRun.Parameters.AddWithValue("$lastSeenAtUtc", "2026-03-18T01:00:00.0000000+00:00");
        createRun.ExecuteNonQuery();

        using var createSyncState = connection.CreateCommand();
        createSyncState.CommandText = """
            INSERT INTO run_sync_state (run_id, dirty, retry_count)
            VALUES ($runId, 1, 7);
            """;
        createSyncState.Parameters.AddWithValue("$runId", "run-001");
        createSyncState.ExecuteNonQuery();
    }

    Invoke(catalogType, battleCatalog!, "AttachToRun", new object?[] { "battle-001", "run-001" });
    var reboundManifest = Invoke(
        catalogType,
        battleCatalog!,
        "TryLoad",
        new object?[] { "battle-001" }
    );
    Assert(reboundManifest != null, "Catalog should still load a rebound battle manifest.");
    Assert(
        string.Equals(
            (string?)GetProperty(manifestType, reboundManifest!, "RunId"),
            "run-001",
            StringComparison.Ordinal
        ),
        "AttachToRun should backfill the saved battle row once the owning run exists."
    );
    var reboundBattles = (
        (System.Collections.IEnumerable)(
            Invoke(catalogType, battleCatalog!, "ListByRunId", new object?[] { "run-001" })
            ?? throw new InvalidOperationException("ListByRunId should return a collection.")
        )
    )
        .Cast<object>()
        .ToList();
    Assert(
        reboundBattles.Any(entry =>
            string.Equals(
                (string?)GetProperty(manifestType, entry, "BattleId"),
                "battle-001",
                StringComparison.Ordinal
            )
        ),
        "ListByRunId should surface battles whose run id was backfilled after persistence."
    );
    var uploadStore = Activator.CreateInstance(uploadStoreType, dbPath, tempRoot, battleCatalog);
    Assert(uploadStore != null, "RunBundleUploadStore should be constructible.");
    var activeSnapshot = Invoke(
        uploadStoreType,
        uploadStore!,
        "TryBuildRunBundleSnapshot",
        new object?[] { "run-001", "player-account-001" }
    );
    Assert(
        activeSnapshot != null,
        "RunBundleUploadStore should build a snapshot for an active run with local payloads."
    );
    var activeProjection = SingleBattleProjection(activeSnapshot!);
    Assert(
        Equals(GetProperty(activeProjection.GetType(), activeProjection, "IsFinalBattle"), false),
        "Run bundle upload should not mark any active-run battle as final."
    );
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        using var finishRun = connection.CreateCommand();
        finishRun.CommandText = """
            UPDATE runs
            SET completed = 1,
                status = 'completed',
                ended_at_utc = $endedAtUtc
            WHERE run_id = $runId;
            """;
        finishRun.Parameters.AddWithValue("$runId", "run-001");
        finishRun.Parameters.AddWithValue("$endedAtUtc", "2026-03-18T01:30:00.0000000+00:00");
        finishRun.ExecuteNonQuery();
    }
    var completedSnapshot = Invoke(
        uploadStoreType,
        uploadStore!,
        "TryBuildRunBundleSnapshot",
        new object?[] { "run-001", "player-account-001" }
    );
    Assert(
        completedSnapshot != null,
        "RunBundleUploadStore should build a snapshot for a completed run with local payloads."
    );
    var finalProjection = SingleBattleProjection(completedSnapshot!);
    Assert(
        Equals(GetProperty(finalProjection.GetType(), finalProjection, "IsFinalBattle"), true),
        "Run bundle upload should mark the sorted last battle of an ended run as final."
    );
    Invoke(
        uploadStoreType,
        uploadStore!,
        "MarkRunUploadPermanentlyFailed",
        new object?[]
        {
            "run-001",
            DateTimeOffset.Parse("2026-03-18T02:00:00.0000000+00:00"),
            "http_409:run_bundle_conflict",
        }
    );
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        using var readSyncState = connection.CreateCommand();
        readSyncState.CommandText = """
            SELECT dirty, retry_count, last_error, uploaded_seq
            FROM run_sync_state
            WHERE run_id = $runId;
            """;
        readSyncState.Parameters.AddWithValue("$runId", "run-001");
        using var reader = readSyncState.ExecuteReader();
        Assert(reader.Read(), "The run sync state should remain available for diagnostics.");
        Assert(reader.GetInt32(0) == 0, "A permanent rejection should leave retry selection.");
        Assert(reader.GetInt32(1) == 8, "A permanent rejection should record its final attempt.");
        Assert(
            reader.GetString(2) == "http_409:run_bundle_conflict",
            "A permanent rejection should retain its structured reason."
        );
        Assert(reader.IsDBNull(3), "A permanent rejection must not masquerade as an upload.");
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
    var openingSocketEffects = new[]
    {
        new ReplaySocketFixture(
            "socket-player-heat",
            "template-player-heat",
            "Player",
            "Hand",
            "Socket_2"
        ),
        new ReplaySocketFixture(
            "socket-opponent-chill",
            "template-opponent-chill",
            "Opponent",
            "Hand",
            "Socket_7"
        ),
    };
    var combatStart = CreateGameSimMessage(
        "PVPCombat",
        day: 3,
        hour: 4,
        encounterId: "encounter-live",
        opponentName: "Rival",
        playerLevel: 7,
        opponentLevel: 9,
        socketEffects: openingSocketEffects
    );
    var combatEnd = CreateGameSimMessage(
        "Encounter",
        day: 3,
        hour: 5,
        encounterId: null,
        opponentName: null,
        playerLevel: 7,
        opponentLevel: 9
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
        ((byte[]?)GetProperty(payloadType, completedPayload!, "SpawnMessageBytes"))?.Length > 0,
        "Completed replay payloads should serialize the opening GameSim."
    );
    Assert(
        ((byte[]?)GetProperty(payloadType, completedPayload!, "CombatMessageBytes"))?.Length > 0,
        "Completed replay payloads should serialize the CombatSim."
    );
    Assert(
        ((byte[]?)GetProperty(payloadType, completedPayload!, "DespawnMessageBytes"))?.Length > 0,
        "Completed replay payloads should serialize the closing GameSim."
    );
    Assert(
        GetProperty(manifestType, completedManifest!, "Snapshots") != null,
        "Completed battle manifests should capture snapshot wrappers for both sides."
    );
    Assert(
        !string.IsNullOrWhiteSpace(
            (string?)GetProperty(manifestType, completedManifest!, "BattleId")
        ),
        "Completed battle manifests should allocate a battle id."
    );

    var collector = Activator.CreateInstance(collectorType);
    Assert(collector != null, "PvpBattleSnapshotCollector should be constructible.");
    var participantCandidate = Activator.CreateInstance(candidateType);
    Assert(
        participantCandidate != null,
        "PvpBattleSequenceCandidate should be constructible for participant tests."
    );
    SetProperty(candidateType, participantCandidate!, "PlayerRank", "Legendary 5");
    SetProperty(candidateType, participantCandidate!, "PlayerRating", 502);
    SetProperty(candidateType, participantCandidate!, "PlayerPrestige", 18);
    SetProperty(candidateType, participantCandidate!, "PlayerIncome", 11);
    SetProperty(candidateType, participantCandidate!, "PlayerGold", 99);
    SetProperty(candidateType, participantCandidate!, "PlayerVictories", 3);
    SetProperty(candidateType, participantCandidate!, "OpponentName", "Snapshot Opponent");
    SetProperty(candidateType, participantCandidate!, "OpponentHero", "Vanessa");
    SetProperty(candidateType, participantCandidate!, "OpponentRank", "Legendary");
    SetProperty(candidateType, participantCandidate!, "OpponentRating", 728);
    SetProperty(candidateType, participantCandidate!, "OpponentLevel", 9);
    SetProperty(candidateType, participantCandidate!, "OpponentPrestige", 12);
    SetProperty(candidateType, participantCandidate!, "OpponentVictories", 6);
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
        ) && Equals(GetProperty(participantsType, participants, "PlayerRating"), 502),
        "BuildParticipants should preserve the player rank and rating captured on the opening candidate."
    );
    Assert(
        Equals(GetProperty(participantsType, participants, "PlayerPrestige"), 18)
            && Equals(GetProperty(participantsType, participants, "PlayerIncome"), 11)
            && Equals(GetProperty(participantsType, participants, "PlayerGold"), 99)
            && Equals(GetProperty(participantsType, participants, "PlayerVictories"), 3),
        "BuildParticipants should preserve player economy, prestige, and victories captured on the opening candidate."
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
    Assert(
        Equals(GetProperty(participantsType, participants, "OpponentPrestige"), 12)
            && Equals(GetProperty(participantsType, participants, "OpponentVictories"), 6),
        "BuildParticipants should preserve opponent prestige and victories captured on the opening candidate."
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
    var loadedSpawnMessage = GetFieldValue(
        loadedSequence.GetType(),
        loadedSequence,
        "SpawnMessage"
    )!;
    var loadedDespawnMessage = GetFieldValue(
        loadedSequence.GetType(),
        loadedSequence,
        "DespawnMessage"
    )!;
    AssertReplaySavedState(
        loadedSpawnMessage,
        expectedDay: 3,
        expectedHour: 4,
        expectedPlayerLevel: 7,
        expectedOpponentLevel: 9,
        failureMessage: "Capture -> payload -> loader should preserve the opening saved state."
    );
    AssertReplaySavedState(
        loadedDespawnMessage,
        expectedDay: 3,
        expectedHour: 5,
        expectedPlayerLevel: 7,
        expectedOpponentLevel: 9,
        failureMessage: "Capture -> payload -> loader should preserve the closing saved state."
    );
    foreach (var socketEffect in openingSocketEffects)
        AssertSocketEffectSpawnPreserved(loadedSpawnMessage, socketEffect);

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
        playerPrestige: 18,
        playerIncome: 11,
        playerGold: 99,
        playerVictories: 3,
        opponentName: "Missing Payload Opponent",
        opponentHero: "Pygmalien",
        opponentRank: "Master",
        opponentRating: 2199,
        opponentLevel: 14,
        opponentPrestige: 12,
        opponentVictories: 6,
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
        (System.Collections.IEnumerable)(
            Invoke(controllerType, controller!, "ListRecentBattles", Array.Empty<object?>())
            ?? throw new InvalidOperationException("ListRecentBattles should return a collection.")
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
    // Active battle id tracking moved off the controller onto the playback session
    // (ReplayPlaybackPublisher.ActiveSessionBattleId): the controller only ever saw the
    // local-saved path, so its id was stale/null for imported ghost battles.
    Assert(
        controllerType.GetProperty("ActiveBattleId") == null,
        "Controller must not re-grow ActiveBattleId; the playback session owns the active id."
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

static void RunReplaySavedStateNormalizationChecks(Type normalizerType, Type manifestType)
{
    Assert(
        Equals(InvokeStatic(normalizerType, "ResolvePositiveUInt", new object?[] { 8u, 3 }), 8u)
            && Equals(
                InvokeStatic(normalizerType, "ResolvePositiveUInt", new object?[] { 0u, 3 }),
                3u
            )
            && Equals(
                InvokeStatic(normalizerType, "ResolvePositiveUInt", new object?[] { 0u, null }),
                0u
            ),
        "Replay saved-state normalization should prefer a positive raw value and only then use the manifest fallback."
    );
    Assert(
        Equals(InvokeStatic(normalizerType, "ResolveLevel", new object?[] { 10, 5 }), 10)
            && Equals(InvokeStatic(normalizerType, "ResolveLevel", new object?[] { 0, 5 }), 5)
            && Equals(
                InvokeStatic(normalizerType, "ResolveLevel", new object?[] { null, null }),
                1
            ),
        "Replay level normalization should prefer positive raw data, use manifest metadata as a fallback, and finally use level one."
    );

    var normalizeMethod = normalizerType.GetMethod(
        "Normalize",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
    );
    Assert(normalizeMethod != null, "ReplaySavedStateNormalizer.Normalize should exist.");
    var sequenceType = normalizeMethod!.GetParameters()[1].ParameterType;
    var manifest = CreateReplayStateManifest(
        manifestType,
        day: 3,
        hour: 4,
        playerLevel: 5,
        opponentLevel: 6
    );

    var validRawSequence = CreateCombatSequence(
        sequenceType,
        CreateGameSimMessage(
            "PVPCombat",
            day: 8,
            hour: 9,
            encounterId: "normalizer-valid-spawn",
            opponentName: "Valid Opponent",
            playerLevel: 10,
            opponentLevel: 11
        ),
        CreateGameSimMessage(
            "Encounter",
            day: 12,
            hour: 13,
            encounterId: null,
            opponentName: null,
            playerLevel: 14,
            opponentLevel: 15
        )
    );
    InvokeStatic(normalizerType, "Normalize", new[] { manifest, validRawSequence });
    AssertSequenceSavedState(
        validRawSequence,
        spawnDay: 8,
        spawnHour: 9,
        spawnPlayerLevel: 10,
        spawnOpponentLevel: 11,
        despawnDay: 12,
        despawnHour: 13,
        despawnPlayerLevel: 14,
        despawnOpponentLevel: 15,
        message: "Positive raw replay state should win over manifest metadata for both saved messages."
    );

    var fallbackSequence = CreateCombatSequence(
        sequenceType,
        CreateGameSimMessage(
            "PVPCombat",
            day: 0,
            hour: 0,
            encounterId: "normalizer-fallback-spawn",
            opponentName: "Fallback Opponent",
            playerLevel: null,
            opponentLevel: 0
        ),
        CreateGameSimMessage(
            "Encounter",
            day: 0,
            hour: 0,
            encounterId: null,
            opponentName: null,
            playerLevel: -2,
            opponentLevel: null
        )
    );
    InvokeStatic(normalizerType, "Normalize", new[] { manifest, fallbackSequence });
    AssertSequenceSavedState(
        fallbackSequence,
        spawnDay: 3,
        spawnHour: 4,
        spawnPlayerLevel: 5,
        spawnOpponentLevel: 6,
        despawnDay: 3,
        despawnHour: 4,
        despawnPlayerLevel: 5,
        despawnOpponentLevel: 6,
        message: "Manifest state should repair missing or invalid values in both saved replay messages."
    );

    var legacySequence = CreateCombatSequence(
        sequenceType,
        CreateGameSimMessage(
            "PVPCombat",
            day: 0,
            hour: 0,
            encounterId: "normalizer-legacy-spawn",
            opponentName: "Legacy Opponent"
        ),
        CreateGameSimMessage("Encounter", day: 0, hour: 0, encounterId: null, opponentName: null)
    );
    InvokeStatic(normalizerType, "Normalize", new object?[] { null, legacySequence });
    AssertSequenceSavedState(
        legacySequence,
        spawnDay: 0,
        spawnHour: 0,
        spawnPlayerLevel: 1,
        spawnOpponentLevel: 1,
        despawnDay: 0,
        despawnHour: 0,
        despawnPlayerLevel: 1,
        despawnOpponentLevel: 1,
        message: "A legacy replay without manifest metadata should remain loadable and receive safe level defaults."
    );
    InvokeStatic(normalizerType, "Normalize", new object?[] { manifest, null });
}

static void RunReplayRunEconomyFallbackChecks(Type fallbackType, Type manifestType)
{
    var tempRoot = Path.Combine(
        Path.GetTempPath(),
        "bpp-replay-run-economy-fallback-tests",
        Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(tempRoot);
    var dbPath = Path.Combine(tempRoot, "bazaarplusplus.db");
    try
    {
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            var schemaType =
                Type.GetType("BazaarPlusPlus.Storage.RunLog.RunLogSchema, BazaarPlusPlus.Storage")
                ?? throw new InvalidOperationException("RunLogSchema should be loadable.");
            InvokeStatic(schemaType, "EnsureInitialized", new object?[] { connection });

            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO runs (
                    run_id,
                    started_at_utc,
                    last_seen_at_utc,
                    status,
                    completed,
                    hero,
                    game_mode,
                    income,
                    gold,
                    last_seq
                ) VALUES (
                    'run-economy-001',
                    '2026-07-17T01:00:00.0000000+00:00',
                    '2026-07-17T01:00:00.0000000+00:00',
                    'active',
                    0,
                    'Jules',
                    'Ranked',
                    5,
                    21,
                    0
                );
                """;
            insert.ExecuteNonQuery();
        }

        var missingEconomyManifest = Activator.CreateInstance(manifestType)!;
        SetProperty(manifestType, missingEconomyManifest, "RunId", "run-economy-001");
        var missingParticipants =
            GetProperty(manifestType, missingEconomyManifest, "Participants")
            ?? throw new InvalidOperationException("Manifest should include participants.");
        var participantsType = missingParticipants.GetType();

        InvokeStatic(
            fallbackType,
            "ApplyMissingRunEconomy",
            new object?[] { missingEconomyManifest, dbPath, null }
        );
        Assert(
            Equals(GetProperty(participantsType, missingParticipants, "PlayerIncome"), 5)
                && Equals(GetProperty(participantsType, missingParticipants, "PlayerGold"), 21),
            "Replay run economy fallback should fill missing legacy manifest income/gold from the run row."
        );

        var preciseEconomyManifest = Activator.CreateInstance(manifestType)!;
        SetProperty(manifestType, preciseEconomyManifest, "RunId", "run-economy-001");
        var preciseParticipants =
            GetProperty(manifestType, preciseEconomyManifest, "Participants")
            ?? throw new InvalidOperationException("Manifest should include participants.");
        SetProperty(preciseParticipants.GetType(), preciseParticipants, "PlayerIncome", 11);
        SetProperty(preciseParticipants.GetType(), preciseParticipants, "PlayerGold", 99);
        InvokeStatic(
            fallbackType,
            "ApplyMissingRunEconomy",
            new object?[] { preciseEconomyManifest, dbPath, null }
        );
        Assert(
            Equals(
                GetProperty(preciseParticipants.GetType(), preciseParticipants, "PlayerIncome"),
                11
            )
                && Equals(
                    GetProperty(preciseParticipants.GetType(), preciseParticipants, "PlayerGold"),
                    99
                ),
            "Replay run economy fallback should not overwrite precise battle-level income/gold."
        );
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}

static void RunReplayOpeningStateSelectionChecks(Type restorerType)
{
    var playerInitialized = CreatePlayerInitializedEvent("Player", "Vanessa");
    var socketsUnlocked = CreateSocketsUnlockedEvent("Hand", "Socket_2");
    var firstPlayerSocket = CreateCardSpawnEvent(
        "opening-player-socket",
        "opening-player-heat",
        "SocketEffect",
        "Player",
        "Hand",
        "Socket_2"
    );
    var duplicatePlayerSocket = CreateCardSpawnEvent(
        "opening-player-socket",
        "opening-player-duplicate",
        "SocketEffect",
        "Player",
        "Hand",
        "Socket_3"
    );
    var opponentSocket = CreateCardSpawnEvent(
        "opening-opponent-socket",
        "opening-opponent-chill",
        "SocketEffect",
        "Opponent",
        "Stash",
        "Socket_7"
    );
    var ordinaryItem = CreateCardSpawnEvent(
        "opening-ordinary-item",
        "opening-ordinary-template",
        "Item",
        "Opponent",
        "Hand",
        "Socket_4"
    );
    var events = CreateGameSimEventList(
        playerInitialized,
        socketsUnlocked,
        firstPlayerSocket,
        duplicatePlayerSocket,
        opponentSocket,
        ordinaryItem
    );

    var selectedOpeningEvents = AsObjects(
        InvokeStatic(restorerType, "SelectOpeningEvents", new[] { events })
    );
    Assert(
        selectedOpeningEvents.Count == 2
            && selectedOpeningEvents.Any(value =>
                value.GetType().Name == "GameSimEventPlayerInitialized"
            )
            && selectedOpeningEvents.Any(value =>
                value.GetType().Name == "GameSimEventSocketsUnlocked"
            ),
        "Replay opening-state restoration should select only the native initialization events."
    );

    var selectedSocketEvents = AsObjects(
        InvokeStatic(restorerType, "SelectSocketEffectSpawnEvents", new[] { events })
    );
    Assert(
        selectedSocketEvents.Count == 2,
        "Replay socket restoration should select one spawn event per socket-effect instance across both combatants."
    );
    var selectedPlayerSocket = selectedSocketEvents.Single(value =>
        string.Equals(
            GetProperty(value.GetType(), value, "InstanceId")?.ToString(),
            "opening-player-socket",
            StringComparison.Ordinal
        )
    );
    Assert(
        string.Equals(
            GetProperty(selectedPlayerSocket.GetType(), selectedPlayerSocket, "TemplateId")
                ?.ToString(),
            "opening-player-heat",
            StringComparison.Ordinal
        ),
        "Replay socket selection should keep the first complete event when duplicate instance ids appear."
    );
    Assert(
        AsObjects(
            InvokeStatic(restorerType, "SelectSocketEffectSpawnEvents", new object?[] { null })
        ).Count == 0,
        "Replay socket selection should accept missing opening event lists from legacy payloads."
    );

    var snapshotManifest = CreateSocketEffectSnapshotManifest(
        playerSnapshots:
        [
            new ReplaySocketFixture(
                "opening-snapshot-only",
                "opening-snapshot-only-heat",
                "Player",
                "Hand",
                "Socket_1"
            ),
            new ReplaySocketFixture(
                "opening-player-socket",
                "opening-player-snapshot-source",
                "Player",
                "Hand",
                "Socket_2"
            ),
        ],
        opponentSnapshots:
        [
            new ReplaySocketFixture(
                "opening-player-socket",
                "opening-opponent-duplicate-source",
                "Opponent",
                "Hand",
                "Socket_5"
            ),
            new ReplaySocketFixture(
                "opening-opponent-snapshot",
                "opening-opponent-snapshot-chill",
                "Opponent",
                "Hand",
                "Socket_7"
            ),
        ]
    );
    var selectedSocketSnapshots = AsObjects(
        InvokeStatic(restorerType, "SelectSocketEffectSnapshots", new[] { snapshotManifest })
    );
    var selectedSnapshotIds = selectedSocketSnapshots
        .Select(value => GetProperty(value.GetType(), value, "InstanceId")?.ToString())
        .ToList();
    Assert(
        selectedSnapshotIds.Count == 3
            && selectedSnapshotIds.Any(value =>
                string.Equals(value, "opening-snapshot-only", StringComparison.Ordinal)
            )
            && selectedSnapshotIds.Any(value =>
                string.Equals(value, "opening-player-socket", StringComparison.Ordinal)
            )
            && selectedSnapshotIds.Any(value =>
                string.Equals(value, "opening-opponent-snapshot", StringComparison.Ordinal)
            ),
        "Replay socket restoration should select snapshot-only effects from both hand captures and deduplicate instance ids."
    );
    var selectedRawDuplicateSnapshot = selectedSocketSnapshots.Single(value =>
        string.Equals(
            GetProperty(value.GetType(), value, "InstanceId")?.ToString(),
            "opening-player-socket",
            StringComparison.Ordinal
        )
    );
    Assert(
        selectedSocketEvents.Any(value =>
            string.Equals(
                GetProperty(value.GetType(), value, "InstanceId")?.ToString(),
                "opening-player-socket",
                StringComparison.Ordinal
            )
        )
            && string.Equals(
                GetProperty(
                    selectedRawDuplicateSnapshot.GetType(),
                    selectedRawDuplicateSnapshot,
                    "TemplateId"
                )
                    ?.ToString(),
                "opening-player-snapshot-source",
                StringComparison.Ordinal
            ),
        "Snapshot selection should retain the first PlayerHand source when a raw event and the opponent snapshot duplicate the same instance id."
    );
    Assert(
        AsObjects(
            InvokeStatic(restorerType, "SelectSocketEffectSnapshots", new object?[] { null })
        ).Count == 0,
        "Replay socket snapshot selection should accept legacy manifests without snapshots."
    );
}

static void RunReplayPresentationRestorationChecks(Type normalizerType)
{
    Assert(
        Equals(InvokeStatic(normalizerType, "ResolveOpeningLevel", new object?[] { 7, 8 }), 7),
        "Replay presentation should restore a valid opening level instead of the closing level."
    );
    Assert(
        Equals(InvokeStatic(normalizerType, "ResolveOpeningLevel", new object?[] { 0, 8 }), 8)
            && Equals(
                InvokeStatic(normalizerType, "ResolveOpeningLevel", new object?[] { -1, 8 }),
                8
            )
            && Equals(
                InvokeStatic(normalizerType, "ResolveOpeningLevel", new object?[] { null, 8 }),
                8
            ),
        "Replay presentation should preserve the current level when the opening level is missing or invalid."
    );
}

static void RunReplaySpawnSanitizationChecks(Type snapshotRehydratorType)
{
    var sanitizeMethod = snapshotRehydratorType.GetMethod(
        "SanitizeSpawnEvents",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
    );
    Assert(sanitizeMethod != null, "SnapshotRehydrator.SanitizeSpawnEvents should exist.");
    var sequenceType = sanitizeMethod!.GetParameters()[0].ParameterType;
    var spawnMessage = CreateGameSimMessage(
        "PVPCombat",
        day: 3,
        hour: 4,
        encounterId: "sanitizer-socket-preservation",
        opponentName: "Sanitizer Opponent"
    );
    AddGameSimEvent(
        spawnMessage,
        CreateCardSpawnEvent(
            "sanitizer-player-socket",
            "sanitizer-player-heat",
            "SocketEffect",
            "Player",
            "Stash",
            "Socket_2"
        )
    );
    AddGameSimEvent(
        spawnMessage,
        CreateCardSpawnEvent(
            "sanitizer-opponent-socket",
            "sanitizer-opponent-chill",
            "SocketEffect",
            "Opponent",
            "Stash",
            "Socket_7"
        )
    );
    AddGameSimEvent(
        spawnMessage,
        CreateCardSpawnEvent(
            "sanitizer-opponent-skill",
            "sanitizer-opponent-skill-template",
            "Skill",
            "Opponent",
            "Stash",
            null
        )
    );
    var sequence = CreateCombatSequence(
        sequenceType,
        spawnMessage,
        CreateGameSimMessage("Encounter", day: 3, hour: 5, encounterId: null, opponentName: null)
    );

    InvokeStatic(
        snapshotRehydratorType,
        "SanitizeSpawnEvents",
        new object?[] { sequence, "sanitizer-test" }
    );
    var remainingIds = ReadGameSimEvents(spawnMessage)
        .Where(value => value.GetType().Name == "GameSimEventCardSpawned")
        .Select(value => GetProperty(value.GetType(), value, "InstanceId")?.ToString())
        .ToHashSet(StringComparer.Ordinal);
    Assert(
        remainingIds.Contains("sanitizer-player-socket")
            && remainingIds.Contains("sanitizer-opponent-socket"),
        "Spawn sanitization must preserve player and opponent socket-effect data even outside the hand section."
    );
    Assert(
        !remainingIds.Contains("sanitizer-opponent-skill"),
        "Spawn sanitization should continue removing unrelated opponent non-hand spawns."
    );
}

static void RunCurrentReplayRecordingStateChecks()
{
    var stateType = RequireType("BazaarPlusPlus.Game.CombatReplay.CurrentReplayRecordingState");
    var completedType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.CombatReplayVideoRecordingCompleted"
    );
    var sourceType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayPlaybackSource");
    var metadataType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoMetadataStatus"
    );
    var reasonType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingReasonCode"
    );
    var state = Activator.CreateInstance(stateType, nonPublic: true)!;

    Invoke(stateType, state, "LatchBattle", new object?[] { "battle-current" });
    Invoke(stateType, state, "EnterReplayState", Array.Empty<object?>());
    Invoke(stateType, state, "MarkBattlePersistence", new object?[] { "battle-stale", true, null });
    var awaiting = Invoke(stateType, state, "Snapshot", Array.Empty<object?>())!;
    Assert(
        GetProperty(awaiting.GetType(), awaiting, "Phase")!.ToString()
            == "AwaitingBattlePersistence",
        "A stale persistence completion must not unlock the current battle."
    );

    Invoke(
        stateType,
        state,
        "MarkBattlePersistence",
        new object?[] { "battle-current", true, null }
    );
    Invoke(stateType, state, "SetAvailability", new object?[] { true, null });
    var ready = Invoke(stateType, state, "Snapshot", Array.Empty<object?>())!;
    Assert(
        (bool)GetProperty(ready.GetType(), ready, "CanStart")!,
        "A persisted current battle with a ready recorder should become recordable."
    );

    Assert(
        (bool)Invoke(stateType, state, "TryArm", new object?[] { "recording-current" })!,
        "The ready current battle should arm once."
    );
    Assert(
        !(bool)Invoke(stateType, state, "TryArm", new object?[] { "recording-second" })!,
        "A current battle must reject a second arm while the first is active."
    );
    Assert(
        (bool)Invoke(stateType, state, "MarkNativeReplayStarted", Array.Empty<object?>())!,
        "The native replay start should bind to the armed recording."
    );
    Invoke(
        stateType,
        state,
        "MarkRecordingStarted",
        new object?[] { "recording-current", "battle-current" }
    );
    Invoke(stateType, state, "MarkReplayEnded", new object?[] { null });

    var completed = Activator.CreateInstance(completedType, nonPublic: true)!;
    SetProperty(completedType, completed, "RecordingId", "recording-current");
    SetProperty(completedType, completed, "BattleId", "battle-current");
    SetProperty(completedType, completed, "Source", Enum.Parse(sourceType, "CurrentNative"));
    SetProperty(completedType, completed, "FinalFilePath", "/tmp/current-replay.mp4");
    SetProperty(completedType, completed, "ArtifactUsable", true);
    SetProperty(completedType, completed, "MetadataStatus", Enum.Parse(metadataType, "Complete"));
    SetProperty(completedType, completed, "ReasonCode", Enum.Parse(reasonType, "Completed"));
    Invoke(stateType, state, "ApplyCompletion", new object?[] { completed });
    Invoke(stateType, state, "SetAvailability", new object?[] { true, null });
    var succeeded = Invoke(stateType, state, "Snapshot", Array.Empty<object?>())!;
    Assert(
        (bool)GetProperty(succeeded.GetType(), succeeded, "CanReveal")!,
        "A usable completed artifact should provide the reveal-video fallback while its report publishes."
    );
    Assert(
        !(bool)GetProperty(succeeded.GetType(), succeeded, "CanOpenReport")!,
        "A video completion must not advertise the report before its committed HTML exists."
    );
    Assert(
        (bool)GetProperty(succeeded.GetType(), succeeded, "CanStart")!,
        "A completed current battle should remain recordable so the player can record it again."
    );

    Invoke(
        stateType,
        state,
        "ApplyReportCompletion",
        new object?[] { "recording-stale", "battle-current", "/tmp/stale.report.html" }
    );
    var staleReport = Invoke(stateType, state, "Snapshot", Array.Empty<object?>())!;
    Assert(
        !(bool)GetProperty(staleReport.GetType(), staleReport, "CanOpenReport")!,
        "A report terminal from a stale recording must not replace the current completion action."
    );

    Invoke(
        stateType,
        state,
        "ApplyReportCompletion",
        new object?[] { "recording-current", "battle-current", "/tmp/current-replay.report.html" }
    );
    var reportReady = Invoke(stateType, state, "Snapshot", Array.Empty<object?>())!;
    Assert(
        (bool)GetProperty(reportReady.GetType(), reportReady, "CanOpenReport")!,
        "The matching report terminal should promote the primary completion action to HTML."
    );
    Assert(
        (string?)GetProperty(reportReady.GetType(), reportReady, "ReportHtmlFilePath")
            == "/tmp/current-replay.report.html",
        "The state should preserve the committed physical report path."
    );

    Assert(
        (bool)Invoke(stateType, state, "TryArm", new object?[] { "recording-again" })!,
        "A completed current battle should arm a second recording."
    );
    var rearmed = Invoke(stateType, state, "Snapshot", Array.Empty<object?>())!;
    Assert(
        !(bool)GetProperty(rearmed.GetType(), rearmed, "CanReveal")!,
        "The previous artifact should not be revealed while a replacement recording is active."
    );
    Assert(
        !(bool)GetProperty(rearmed.GetType(), rearmed, "CanOpenReport")!,
        "The previous report should not open while a replacement recording is active."
    );
    Invoke(stateType, state, "MarkNativeReplayStarted", Array.Empty<object?>());
    Invoke(
        stateType,
        state,
        "MarkRecordingStarted",
        new object?[] { "recording-again", "battle-current" }
    );
    Invoke(stateType, state, "MarkReplayEnded", new object?[] { null });

    var failedReplacement = Activator.CreateInstance(completedType, nonPublic: true)!;
    SetProperty(completedType, failedReplacement, "RecordingId", "recording-again");
    SetProperty(completedType, failedReplacement, "BattleId", "battle-current");
    SetProperty(
        completedType,
        failedReplacement,
        "Source",
        Enum.Parse(sourceType, "CurrentNative")
    );
    SetProperty(completedType, failedReplacement, "FinalFilePath", null);
    SetProperty(completedType, failedReplacement, "ArtifactUsable", false);
    SetProperty(
        completedType,
        failedReplacement,
        "MetadataStatus",
        Enum.Parse(metadataType, "Failed")
    );
    SetProperty(
        completedType,
        failedReplacement,
        "ReasonCode",
        Enum.Parse(reasonType, "CaptureFailed")
    );
    Invoke(stateType, state, "ApplyCompletion", new object?[] { failedReplacement });
    var replacementFailed = Invoke(stateType, state, "Snapshot", Array.Empty<object?>())!;
    Assert(
        (bool)GetProperty(replacementFailed.GetType(), replacementFailed, "CanReveal")!,
        "A failed re-recording must preserve access to the previous usable artifact."
    );
    Assert(
        (bool)GetProperty(replacementFailed.GetType(), replacementFailed, "CanOpenReport")!,
        "A failed re-recording must preserve access to the previous committed report."
    );
    Assert(
        (bool)GetProperty(replacementFailed.GetType(), replacementFailed, "CanStart")!,
        "A failed re-recording should remain retryable."
    );

    Assert(
        (bool)Invoke(stateType, state, "TryArm", new object?[] { "recording-third" })!,
        "The battle should remain recordable after a failed replacement."
    );
    Invoke(stateType, state, "MarkNativeReplayStarted", Array.Empty<object?>());
    Invoke(
        stateType,
        state,
        "MarkRecordingStarted",
        new object?[] { "recording-third", "battle-current" }
    );
    Invoke(stateType, state, "MarkReplayEnded", new object?[] { null });
    var successfulReplacement = Activator.CreateInstance(completedType, nonPublic: true)!;
    SetProperty(
        successfulReplacement.GetType(),
        successfulReplacement,
        "RecordingId",
        "recording-third"
    );
    SetProperty(
        successfulReplacement.GetType(),
        successfulReplacement,
        "BattleId",
        "battle-current"
    );
    SetProperty(
        successfulReplacement.GetType(),
        successfulReplacement,
        "Source",
        Enum.Parse(sourceType, "CurrentNative")
    );
    SetProperty(
        successfulReplacement.GetType(),
        successfulReplacement,
        "FinalFilePath",
        "/tmp/current-replay-third.mp4"
    );
    SetProperty(successfulReplacement.GetType(), successfulReplacement, "ArtifactUsable", true);
    SetProperty(
        successfulReplacement.GetType(),
        successfulReplacement,
        "MetadataStatus",
        Enum.Parse(metadataType, "Complete")
    );
    SetProperty(
        successfulReplacement.GetType(),
        successfulReplacement,
        "ReasonCode",
        Enum.Parse(reasonType, "Completed")
    );
    Invoke(stateType, state, "ApplyCompletion", new object?[] { successfulReplacement });
    var replacementAwaitingReport = Invoke(stateType, state, "Snapshot", Array.Empty<object?>())!;
    Assert(
        !(bool)
            GetProperty(
                replacementAwaitingReport.GetType(),
                replacementAwaitingReport,
                "CanOpenReport"
            )!,
        "A successful replacement video must clear the previous report while its own report publishes."
    );
    Assert(
        (bool)
            GetProperty(
                replacementAwaitingReport.GetType(),
                replacementAwaitingReport,
                "CanReveal"
            )!,
        "The replacement MP4 should remain the completion fallback while report publication is pending."
    );

    Invoke(stateType, state, "LeaveReplayState", Array.Empty<object?>());
    var reset = Invoke(stateType, state, "Snapshot", Array.Empty<object?>())!;
    Assert(
        !(bool)GetProperty(reset.GetType(), reset, "Visible")!,
        "Leaving ReplayState should remove the temporary current-battle button."
    );

    var reorderedTerminalState = Activator.CreateInstance(stateType, nonPublic: true)!;
    Invoke(stateType, reorderedTerminalState, "LatchBattle", new object?[] { "battle-current" });
    Invoke(stateType, reorderedTerminalState, "EnterReplayState", Array.Empty<object?>());
    Invoke(
        stateType,
        reorderedTerminalState,
        "MarkBattlePersistence",
        new object?[] { "battle-current", true, null }
    );
    Invoke(stateType, reorderedTerminalState, "SetAvailability", new object?[] { true, null });
    Assert(
        (bool)
            Invoke(
                stateType,
                reorderedTerminalState,
                "TryArm",
                new object?[] { "recording-current" }
            )!,
        "The terminal-order regression fixture should arm recording A."
    );
    Invoke(stateType, reorderedTerminalState, "MarkNativeReplayStarted", Array.Empty<object?>());
    Invoke(
        stateType,
        reorderedTerminalState,
        "MarkRecordingStarted",
        new object?[] { "recording-current", "battle-current" }
    );
    Invoke(stateType, reorderedTerminalState, "MarkReplayEnded", new object?[] { null });
    Invoke(stateType, reorderedTerminalState, "ApplyCompletion", new object?[] { completed });
    Invoke(stateType, reorderedTerminalState, "SetAvailability", new object?[] { true, null });
    Assert(
        (bool)
            Invoke(
                stateType,
                reorderedTerminalState,
                "TryArm",
                new object?[] { "recording-again" }
            )!,
        "Recording B should be armable while recording A's report is still publishing."
    );
    Invoke(
        stateType,
        reorderedTerminalState,
        "ApplyReportCompletion",
        new object?[] { "recording-current", "battle-current", "/tmp/late-a.report.html" }
    );
    Invoke(stateType, reorderedTerminalState, "MarkNativeReplayStarted", Array.Empty<object?>());
    Invoke(
        stateType,
        reorderedTerminalState,
        "MarkRecordingStarted",
        new object?[] { "recording-again", "battle-current" }
    );
    Invoke(stateType, reorderedTerminalState, "MarkReplayEnded", new object?[] { null });
    Invoke(
        stateType,
        reorderedTerminalState,
        "ApplyCompletion",
        new object?[] { failedReplacement }
    );
    var reorderedTerminalResult = Invoke(
        stateType,
        reorderedTerminalState,
        "Snapshot",
        Array.Empty<object?>()
    )!;
    Assert(
        (bool)
            GetProperty(
                reorderedTerminalResult.GetType(),
                reorderedTerminalResult,
                "CanOpenReport"
            )!
            && (string?)GetProperty(
                reorderedTerminalResult.GetType(),
                reorderedTerminalResult,
                "ReportHtmlFilePath"
            ) == "/tmp/late-a.report.html",
        "A late report terminal for usable recording A must survive an armed then failed recording B."
    );
}

static void RunReplayVideoPreflightReasonChecks()
{
    var operationType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingOperation"
    );
    var completionType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingCompletion"
    );
    var terminalType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingTerminal"
    );
    var sourceType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayPlaybackSource");
    var reasonType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingReasonCode"
    );
    var operation = Activator.CreateInstance(
        operationType,
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        args:
        [
            "recording-preflight-reason",
            "battle-preflight-reason",
            Enum.Parse(sourceType, "CurrentNative"),
            DateTimeOffset.UtcNow,
        ],
        culture: null
    )!;
    var completion = Activator.CreateInstance(completionType, nonPublic: true)!;
    SetProperty(completionType, completion, "ReasonCode", Enum.Parse(reasonType, "Aborted"));
    SetProperty(completionType, completion, "Reason", "native-recap-close-timeout");

    var tryComplete =
        operationType.GetMethod(
            "TryComplete",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [completionType, terminalType.MakeByRefType()],
            modifiers: null
        )
        ?? throw new InvalidOperationException(
            "ReplayVideoRecordingOperation.TryComplete should expose its terminal result."
        );
    object?[] arguments = [completion, null];
    Assert(
        (bool)tryComplete.Invoke(operation, arguments)!,
        "A preflight cancellation should complete its recording operation."
    );
    var terminal = arguments[1]!;
    Assert(
        string.Equals(
            GetProperty(terminalType, terminal, "Reason")?.ToString(),
            "native-recap-close-timeout",
            StringComparison.Ordinal
        ),
        "A preflight cancellation should preserve its specific ended reason for downstream diagnostics."
    );
}

static void RunCurrentReplayRecordingUiLogChecks()
{
    var logStateType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.CurrentReplayRecordingUiLogState"
    );

    var ready = InvokeStatic(logStateType, "ResolveLayoutReason", new object?[] { true, null });
    Assert(
        ready?.ToString() == "None",
        "An available current-recording layout should have no failure reason."
    );

    var missingFootprint = InvokeStatic(
        logStateType,
        "ResolveLayoutReason",
        new object?[] { false, "collection-footprint-unavailable" }
    );
    Assert(
        missingFootprint?.ToString() == "TargetFootprintUnavailable",
        "A missing clone footprint should remain distinguishable in Release diagnostics."
    );

    var obstructed = InvokeStatic(
        logStateType,
        "ResolveLayoutReason",
        new object?[] { false, "native-button-path" }
    );
    Assert(
        obstructed?.ToString() == "Obstructed",
        "A native layout blocker should map to the bounded obstruction reason."
    );
}

static void RunSystemFileRevealCommandChecks()
{
    var revealerType = RequireType("BazaarPlusPlus.GameInterop.Files.SystemFileRevealer");
    var platformType = RequireType("BazaarPlusPlus.GameInterop.Files.SystemFileRevealPlatform");
    foreach (var platformName in new[] { "MacOS", "Windows", "Linux" })
    {
        var command = InvokeStatic(
            revealerType,
            "BuildCommand",
            new object?[] { Enum.Parse(platformType, platformName), "/tmp/video file.mp4" }
        )!;
        var commandType = command.GetType();
        var fileName = (string)GetProperty(commandType, command, "FileName")!;
        var arguments = (string)GetProperty(commandType, command, "Arguments")!;
        Assert(
            !string.IsNullOrWhiteSpace(fileName),
            $"{platformName} reveal command needs an executable."
        );
        Assert(
            arguments.Contains('"'),
            $"{platformName} reveal command must quote paths with spaces."
        );
    }
}

static void RunCurrentReplayVideoMetadataChecks()
{
    var storeType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.CombatReplayVideoMetadataStore"
    );
    var startedType = RequireType("BazaarPlusPlus.Game.CombatReplay.Video.VideoRecordingStarted");
    var finishedType = RequireType("BazaarPlusPlus.Game.CombatReplay.Video.VideoRecordingFinished");
    var syncAnchorType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoSyncAnchor"
    );
    var root = Path.Combine(
        Path.GetTempPath(),
        "bpp-current-replay-video-metadata-tests",
        Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(root);
    var databasePath = Path.Combine(root, "run.db");
    try
    {
        var store = Activator.CreateInstance(storeType, databasePath)!;
        var started = Activator.CreateInstance(startedType, nonPublic: true)!;
        SetProperty(startedType, started, "VideoId", "video-current");
        SetProperty(startedType, started, "BattleId", "battle-current");
        SetProperty(startedType, started, "Source", "CurrentNative");
        SetProperty(startedType, started, "VideoRelativePath", "2026-07-15/battle-current.mp4");
        SetProperty(startedType, started, "Width", 1920);
        SetProperty(startedType, started, "Height", 1080);
        SetProperty(startedType, started, "Fps", 60);
        SetProperty(startedType, started, "Codec", "h264_videotoolbox");
        SetProperty(startedType, started, "StartedAtUtc", DateTimeOffset.UtcNow);
        Invoke(storeType, store, "SaveStart", new object?[] { started });

        var finished = Activator.CreateInstance(finishedType, nonPublic: true)!;
        SetProperty(finishedType, finished, "VideoId", "video-current");
        SetProperty(finishedType, finished, "BattleId", "battle-current");
        SetProperty(finishedType, finished, "VideoRelativePath", "2026-07-15/battle-current.mp4");
        SetProperty(finishedType, finished, "EndedAtUtc", DateTimeOffset.UtcNow);
        SetProperty(finishedType, finished, "DurationMs", 1234L);
        SetProperty(finishedType, finished, "CapturedFrames", 60);
        SetProperty(finishedType, finished, "DroppedFrames", 0);
        SetProperty(finishedType, finished, "FileSizeBytes", 4096L);
        SetProperty(finishedType, finished, "Status", "COMPLETED");
        var syncAnchors = Array.CreateInstance(syncAnchorType, 2);
        syncAnchors.SetValue(
            Activator.CreateInstance(
                syncAnchorType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object?[] { "video-current", "battle-current", 0, 0, 0L, 0L },
                culture: null
            ),
            0
        );
        syncAnchors.SetValue(
            Activator.CreateInstance(
                syncAnchorType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object?[] { "video-current", "battle-current", 1, 50, 17L, 1L },
                culture: null
            ),
            1
        );
        SetProperty(finishedType, finished, "SyncAnchors", syncAnchors);
        Invoke(storeType, store, "SaveFinish", new object?[] { finished });

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT battle_id, source, video_relative_path, fps, status
            FROM combat_replay_videos
            WHERE video_id = 'video-current';
            """;
        using var reader = command.ExecuteReader();
        Assert(reader.Read(), "The current replay recording should create a database row.");
        Assert(
            reader.GetString(0) == "battle-current",
            "The video row must keep the exact battle id."
        );
        Assert(
            reader.GetString(1) == "CurrentNative",
            "The video row must identify the current native replay source."
        );
        Assert(
            reader.GetString(2).EndsWith("battle-current.mp4", StringComparison.Ordinal),
            "The video row must retain the exported file path."
        );
        Assert(
            reader.GetInt32(3) == 60,
            "The video row must retain the resolved recording frame rate."
        );
        Assert(
            reader.GetString(4) == "COMPLETED",
            "Completed current replay videos must be listable by the Tauri query."
        );
        reader.Close();

        command.CommandText = """
            SELECT output_ordinal, combat_frame, combat_ms, media_pts_ms
            FROM combat_replay_video_sync_anchors
            WHERE video_id = 'video-current'
            ORDER BY output_ordinal;
            """;
        using var anchorReader = command.ExecuteReader();
        Assert(anchorReader.Read(), "The first exact video sync anchor should be persisted.");
        Assert(
            anchorReader.GetInt64(0) == 0
                && anchorReader.GetInt32(1) == 0
                && anchorReader.GetInt32(2) == 0
                && anchorReader.GetInt64(3) == 0,
            "The first video sync anchor should preserve combat and media coordinates."
        );
        Assert(anchorReader.Read(), "The second exact video sync anchor should be persisted.");
        Assert(
            anchorReader.GetInt64(0) == 1
                && anchorReader.GetInt32(1) == 1
                && anchorReader.GetInt32(2) == 50
                && anchorReader.GetInt64(3) == 17,
            "The second video sync anchor should preserve monotonic output coordinates."
        );
        Assert(!anchorReader.Read(), "Only committed output frames should create sync anchors.");
        anchorReader.Close();

        Invoke(storeType, store, "SaveFinish", new object?[] { finished });
        command.CommandText = """
            SELECT COUNT(*)
            FROM combat_replay_video_sync_anchors
            WHERE video_id = 'video-current';
            """;
        Assert(
            Convert.ToInt32(command.ExecuteScalar()) == 2,
            "Replaying an identical completion must be idempotent without duplicating anchors."
        );

        var conflictingAnchors = Array.CreateInstance(syncAnchorType, 2);
        conflictingAnchors.SetValue(syncAnchors.GetValue(0), 0);
        conflictingAnchors.SetValue(
            Activator.CreateInstance(
                syncAnchorType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object?[] { "video-current", "battle-current", 2, 100, 34L, 1L },
                culture: null
            ),
            1
        );
        SetProperty(finishedType, finished, "SyncAnchors", conflictingAnchors);
        var conflictRejected = false;
        try
        {
            Invoke(storeType, store, "SaveFinish", new object?[] { finished });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            conflictRejected = ex.InnerException.Message.Contains(
                "conflict",
                StringComparison.OrdinalIgnoreCase
            );
        }
        Assert(
            conflictRejected,
            "A duplicate output ordinal with different persisted coordinates must fail loudly."
        );

        command.CommandText = """
            SELECT COUNT(*), MAX(CASE WHEN output_ordinal = 1 THEN media_pts_ms END)
            FROM combat_replay_video_sync_anchors
            WHERE video_id = 'video-current';
            """;
        using (var conflictReader = command.ExecuteReader())
        {
            Assert(
                conflictReader.Read()
                    && conflictReader.GetInt32(0) == 2
                    && conflictReader.GetInt64(1) == 17,
                "A rejected duplicate conflict must leave the original anchor sequence unchanged."
            );
        }

        var recoveryPage = Invoke(
            storeType,
            store,
            "ListCompletedForReportRecovery",
            new object?[] { 10, null }
        )!;
        var recoveryPageType = recoveryPage.GetType();
        var recoveryCandidates = (System.Collections.IEnumerable)
            GetProperty(recoveryPageType, recoveryPage, "Candidates")!;
        var candidate = recoveryCandidates.Cast<object>().Single();
        var candidateType = candidate.GetType();
        Assert(
            GetProperty(candidateType, candidate, "RecordingId") as string == "video-current"
                && GetProperty(candidateType, candidate, "BattleId") as string == "battle-current"
                && GetProperty(candidateType, candidate, "Source") as string == "CurrentNative"
                && GetProperty(candidateType, candidate, "VideoRelativePath") as string
                    == "2026-07-15/battle-current.mp4"
                && (
                    (System.Collections.IEnumerable)
                        GetProperty(candidateType, candidate, "SyncAnchors")!
                )
                    .Cast<object>()
                    .Count() == 2,
            "Report startup recovery must receive the typed recording, battle, source, and relative video identity."
        );
        var recoveryCursor = GetProperty(recoveryPageType, recoveryPage, "NextCursor");
        Assert(
            recoveryCursor != null
                && GetProperty(recoveryCursor.GetType(), recoveryCursor, "RecordingId") as string
                    == "video-current",
            "A non-empty recovery page must expose a stable continuation cursor."
        );
        var exhaustedRecoveryPage = Invoke(
            storeType,
            store,
            "ListCompletedForReportRecovery",
            new object?[] { 10, recoveryCursor }
        )!;
        Assert(
            !(
                (System.Collections.IEnumerable)
                    GetProperty(
                        exhaustedRecoveryPage.GetType(),
                        exhaustedRecoveryPage,
                        "Candidates"
                    )!
            )
                .Cast<object>()
                .Any(),
            "Recovery keyset pagination must exclude the cursor row from the following page."
        );
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void RunReplayVideoSyncCollectorChecks()
{
    var trackerType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoCombatPositionTracker"
    );
    var collectorType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoSyncAnchorCollector"
    );
    var tracker = Activator.CreateInstance(
        trackerType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: new object?[] { 50 },
        culture: null
    )!;
    var collector = Activator.CreateInstance(
        collectorType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: new object?[] { "recording-sync", "battle-sync", 60 },
        culture: null
    )!;

    var capturedAtA = Invoke(trackerType, tracker, "Snapshot", Array.Empty<object?>())!;
    Invoke(trackerType, tracker, "Advance", Array.Empty<object?>());
    Invoke(trackerType, tracker, "Advance", Array.Empty<object?>());
    var trackerNowAtB = Invoke(trackerType, tracker, "Snapshot", Array.Empty<object?>())!;
    Assert(
        (bool)
            Invoke(
                collectorType,
                collector,
                "TryAdoptCapturedFrame",
                new object?[] { 1L, capturedAtA }
            )!,
        "The first captured frame position should be adopted."
    );

    var anchorA = Invoke(collectorType, collector, "RecordOutput", Array.Empty<object?>())!;
    Assert(
        (int)GetProperty(anchorA.GetType(), anchorA, "CombatFrame")! == 0
            && (int)GetProperty(anchorA.GetType(), anchorA, "CombatMs")! == 0,
        "An async readback enqueued after combat advanced to B must retain capture-time position A."
    );

    Assert(
        (bool)
            Invoke(
                collectorType,
                collector,
                "TryAdoptCapturedFrame",
                new object?[] { 2L, trackerNowAtB }
            )!,
        "The next captured source frame should adopt position B."
    );
    var anchorB = Invoke(collectorType, collector, "RecordOutput", Array.Empty<object?>())!;
    Assert(
        (int)GetProperty(anchorB.GetType(), anchorB, "CombatFrame")! == 1
            && (int)GetProperty(anchorB.GetType(), anchorB, "CombatMs")! == 50
            && (long)GetProperty(anchorB.GetType(), anchorB, "OutputOrdinal")! == 1L,
        "A later captured frame should advance both combat position and output ordinal."
    );
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

static object? InvokeStatic(Type type, string methodName, object?[] args)
{
    var method = type.GetMethod(
        methodName,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
    );
    Assert(method != null, $"Static method not found: {type.FullName}.{methodName}");
    return method!.Invoke(null, args);
}

static object InvokeResolve(
    Type muxerType,
    object muxer,
    object status,
    string tempVideoPath,
    string finalPath,
    IReadOnlyList<string> usableWavPaths,
    string? ffmpegExecutable
)
{
    var method = muxerType.GetMethod(
        "Resolve",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(method != null, "ReplayVideoAudioMuxer should expose Resolve.");
    return method!.Invoke(
            muxer,
            new object?[]
            {
                "recording-mux-test-00000001",
                status,
                tempVideoPath,
                finalPath,
                usableWavPaths,
                ffmpegExecutable,
            }
        ) ?? throw new InvalidOperationException("Resolve should return a MuxResult.");
}

static void AssertMuxReason(Type muxResultType, object result, string expectedReason)
{
    Assert(
        string.Equals(
            GetFieldValue(muxResultType, result, "ReasonCode")?.ToString(),
            expectedReason,
            StringComparison.Ordinal
        ),
        $"Resolve should return reason {expectedReason}."
    );
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

static object GetRequiredProperty(Type type, object instance, string name)
{
    return GetProperty(type, instance, name)
        ?? throw new InvalidOperationException($"Property returned null: {type.FullName}.{name}");
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

static object CreateSocketEffectSnapshotManifest(
    IReadOnlyList<ReplaySocketFixture> playerSnapshots,
    IReadOnlyList<ReplaySocketFixture> opponentSnapshots
)
{
    var manifestType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifest");
    var snapshotsType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSnapshots");
    var cardSetCaptureType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCardSetCapture");
    var captureStatusType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureStatus");
    var captureSourceType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureSource");
    var snapshots = Activator.CreateInstance(snapshotsType)!;
    SetProperty(
        snapshotsType,
        snapshots,
        "PlayerHand",
        CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "OpeningMessage",
            CreateSocketSnapshotList(playerSnapshots)
        )
    );
    SetProperty(
        snapshotsType,
        snapshots,
        "OpponentHand",
        CreateCardSetCapture(
            cardSetCaptureType,
            captureStatusType,
            captureSourceType,
            "Captured",
            "LiveRetry",
            CreateSocketSnapshotList(opponentSnapshots)
        )
    );

    var manifest = Activator.CreateInstance(manifestType)!;
    SetProperty(manifestType, manifest, "Snapshots", snapshots);
    return manifest;
}

static object CreateSocketSnapshotList(IReadOnlyList<ReplaySocketFixture> fixtures)
{
    var snapshotType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCardSnapshot");
    var listType = typeof(List<>).MakeGenericType(snapshotType);
    var list = Activator.CreateInstance(listType)!;
    foreach (var fixture in fixtures)
    {
        var snapshot = Activator.CreateInstance(snapshotType)!;
        SetProperty(snapshotType, snapshot, "InstanceId", fixture.InstanceId);
        SetProperty(snapshotType, snapshot, "TemplateId", fixture.TemplateId);
        SetProperty(
            snapshotType,
            snapshot,
            "Type",
            ParseEnum(
                "BazaarGameShared.Domain.Core.Types.ECardType",
                "BazaarGameShared",
                "SocketEffect"
            )
        );
        SetProperty(
            snapshotType,
            snapshot,
            "Section",
            ParseEnum(
                "BazaarGameShared.Domain.Core.Types.EInventorySection",
                "BazaarGameShared",
                fixture.Section
            )
        );
        SetProperty(
            snapshotType,
            snapshot,
            "Socket",
            ParseEnum(
                "BazaarGameShared.Domain.Core.Types.EContainerSocketId",
                "BazaarGameShared",
                fixture.Socket
            )
        );
        Invoke(listType, list, "Add", new[] { snapshot });
    }

    return list;
}

static object CreateSnapshotList(string instanceId, string templateId)
{
    var snapshotType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCardSnapshot");
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
    var snapshotType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCardSnapshot");
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
    int playerPrestige,
    int playerIncome,
    int playerGold,
    int playerVictories,
    string opponentName,
    string opponentHero,
    string opponentRank,
    int opponentRating,
    int opponentLevel,
    int opponentPrestige,
    int opponentVictories,
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
    SetProperty(participantsType, participants, "PlayerPrestige", playerPrestige);
    SetProperty(participantsType, participants, "PlayerIncome", playerIncome);
    SetProperty(participantsType, participants, "PlayerGold", playerGold);
    SetProperty(participantsType, participants, "PlayerVictories", playerVictories);
    SetProperty(participantsType, participants, "OpponentName", opponentName);
    SetProperty(participantsType, participants, "OpponentHero", opponentHero);
    SetProperty(participantsType, participants, "OpponentRank", opponentRank);
    SetProperty(participantsType, participants, "OpponentRating", opponentRating);
    SetProperty(participantsType, participants, "OpponentLevel", opponentLevel);
    SetProperty(participantsType, participants, "OpponentPrestige", opponentPrestige);
    SetProperty(participantsType, participants, "OpponentVictories", opponentVictories);
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
    SetProperty(manifestType, manifest, "RecordedAtUtc", savedAtUtc);
    SetProperty(manifestType, manifest, "CombatKind", combatKind);
    SetProperty(manifestType, manifest, "Day", day);
    SetProperty(manifestType, manifest, "Hour", hour);
    SetProperty(manifestType, manifest, "EncounterId", encounterId);
    SetProperty(manifestType, manifest, "Participants", participants);
    SetProperty(manifestType, manifest, "Outcome", outcome);
    SetProperty(manifestType, manifest, "Snapshots", snapshots);
    return manifest;
}

static object SingleBattleProjection(object uploadSnapshot)
{
    var metadata =
        GetProperty(uploadSnapshot.GetType(), uploadSnapshot, "Metadata")
        ?? throw new InvalidOperationException("Upload snapshot should expose metadata.");
    var projections =
        (System.Collections.IEnumerable?)GetProperty(
            metadata.GetType(),
            metadata,
            "BattleProjections"
        )
        ?? throw new InvalidOperationException("Upload metadata should expose battle projections.");
    return projections.Cast<object>().Single();
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

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static object CreateGameSimMessage(
    string stateName,
    uint day,
    uint hour,
    string? encounterId,
    string? opponentName,
    int? playerLevel = null,
    int? opponentLevel = null,
    IReadOnlyList<ReplaySocketFixture>? socketEffects = null
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

    SetGameSimPlayerLevel(gameSimType, gameSim, "Player", playerLevel);
    SetGameSimPlayerLevel(gameSimType, gameSim, "Opponent", opponentLevel);
    if (socketEffects != null)
    {
        foreach (var socketEffect in socketEffects)
        {
            AddGameSimEventToData(
                gameSimType,
                gameSim,
                CreateCardSpawnEvent(
                    socketEffect.InstanceId,
                    socketEffect.TemplateId,
                    "SocketEffect",
                    socketEffect.CombatantId,
                    socketEffect.Section,
                    socketEffect.Socket
                )
            );
        }
    }

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

static object CreateReplayStateManifest(
    Type manifestType,
    int day,
    int hour,
    int? playerLevel,
    int? opponentLevel
)
{
    var participantsType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleParticipants");
    var participants = Activator.CreateInstance(participantsType)!;
    SetProperty(participantsType, participants, "PlayerLevel", playerLevel);
    SetProperty(participantsType, participants, "OpponentLevel", opponentLevel);

    var manifest = Activator.CreateInstance(manifestType)!;
    SetProperty(manifestType, manifest, "Day", day);
    SetProperty(manifestType, manifest, "Hour", hour);
    SetProperty(manifestType, manifest, "Participants", participants);
    return manifest;
}

static object CreateCombatSequence(Type sequenceType, object spawnMessage, object despawnMessage)
{
    return Activator.CreateInstance(
            sequenceType,
            spawnMessage,
            despawnMessage,
            CreateCombatSimMessage()
        ) ?? throw new InvalidOperationException("CombatSequenceMessages should be constructible.");
}

static void AssertSequenceSavedState(
    object sequence,
    uint spawnDay,
    uint spawnHour,
    int spawnPlayerLevel,
    int spawnOpponentLevel,
    uint despawnDay,
    uint despawnHour,
    int despawnPlayerLevel,
    int despawnOpponentLevel,
    string message
)
{
    var sequenceType = sequence.GetType();
    var spawnMessage = GetFieldValue(sequenceType, sequence, "SpawnMessage")!;
    var despawnMessage = GetFieldValue(sequenceType, sequence, "DespawnMessage")!;
    AssertReplaySavedState(
        spawnMessage,
        spawnDay,
        spawnHour,
        spawnPlayerLevel,
        spawnOpponentLevel,
        message
    );
    AssertReplaySavedState(
        despawnMessage,
        despawnDay,
        despawnHour,
        despawnPlayerLevel,
        despawnOpponentLevel,
        message
    );
}

static void AssertReplaySavedState(
    object netMessage,
    uint expectedDay,
    uint expectedHour,
    int expectedPlayerLevel,
    int expectedOpponentLevel,
    string failureMessage
)
{
    var gameSim = GetGameSimData(netMessage);
    var gameSimType = gameSim.GetType();
    var run = GetFieldValue(gameSimType, gameSim, "Run")!;
    var runType = run.GetType();
    Assert(
        Equals(GetFieldValue(runType, run, "Day"), expectedDay)
            && Equals(GetFieldValue(runType, run, "Hour"), expectedHour)
            && ReadGameSimPlayerLevel(gameSim, "Player") == expectedPlayerLevel
            && ReadGameSimPlayerLevel(gameSim, "Opponent") == expectedOpponentLevel,
        failureMessage
    );
}

static void AssertSocketEffectSpawnPreserved(object message, ReplaySocketFixture expected)
{
    var socketEvent = ReadGameSimEvents(message)
        .SingleOrDefault(value =>
            value.GetType().Name == "GameSimEventCardSpawned"
            && string.Equals(
                GetProperty(value.GetType(), value, "InstanceId")?.ToString(),
                expected.InstanceId,
                StringComparison.Ordinal
            )
        );
    Assert(socketEvent != null, $"Socket-effect spawn should preserve {expected.InstanceId}.");
    var eventType = socketEvent!.GetType();
    Assert(
        string.Equals(
            GetProperty(eventType, socketEvent, "TemplateId")?.ToString(),
            expected.TemplateId,
            StringComparison.Ordinal
        )
            && string.Equals(
                GetProperty(eventType, socketEvent, "Type")?.ToString(),
                "SocketEffect",
                StringComparison.Ordinal
            )
            && string.Equals(
                GetProperty(eventType, socketEvent, "CombatantId")?.ToString(),
                expected.CombatantId,
                StringComparison.Ordinal
            )
            && string.Equals(
                GetProperty(eventType, socketEvent, "Section")?.ToString(),
                expected.Section,
                StringComparison.Ordinal
            )
            && string.Equals(
                GetProperty(eventType, socketEvent, "Socket")?.ToString(),
                expected.Socket,
                StringComparison.Ordinal
            ),
        $"Socket-effect spawn {expected.InstanceId} should preserve template, combatant, section, and socket."
    );
}

static object GetGameSimData(object message)
{
    return GetProperty(message.GetType(), message, "Data")
        ?? throw new InvalidOperationException("NetMessageGameSim should expose Data.");
}

static int? ReadGameSimPlayerLevel(object gameSim, string playerFieldName)
{
    var player = GetFieldValue(gameSim.GetType(), gameSim, playerFieldName)!;
    var attributes = (System.Collections.IDictionary?)GetFieldValue(
        player.GetType(),
        player,
        "Attributes"
    );
    var levelType = ParseEnum(
        "BazaarGameShared.Domain.Core.Types.EPlayerAttributeType",
        "BazaarGameShared",
        "Level"
    );
    return attributes != null && attributes.Contains(levelType)
        ? Convert.ToInt32(attributes[levelType])
        : null;
}

static void SetGameSimPlayerLevel(
    Type gameSimType,
    object gameSim,
    string playerFieldName,
    int? level
)
{
    if (!level.HasValue)
        return;

    var player = GetFieldValue(gameSimType, gameSim, playerFieldName)!;
    var attributes = (System.Collections.IDictionary?)GetFieldValue(
        player.GetType(),
        player,
        "Attributes"
    );
    Assert(attributes != null, "SimUpdatePlayer should expose an attributes dictionary.");
    attributes![
        ParseEnum(
            "BazaarGameShared.Domain.Core.Types.EPlayerAttributeType",
            "BazaarGameShared",
            "Level"
        )
    ] = level.Value;
}

static object CreatePlayerInitializedEvent(string combatantId, string hero)
{
    var eventType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.GameSimEventPlayerInitialized",
        "BazaarGameShared"
    );
    var value = Activator.CreateInstance(eventType)!;
    SetField(
        eventType,
        value,
        "CombatantId",
        ParseEnum(
            "BazaarGameShared.Domain.Core.Types.ECombatantId",
            "BazaarGameShared",
            combatantId
        )
    );
    SetField(
        eventType,
        value,
        "Hero",
        ParseEnum("BazaarGameShared.Domain.Core.Types.EHero", "BazaarGameShared", hero)
    );
    return value;
}

static object CreateSocketsUnlockedEvent(string section, string socket)
{
    var eventType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.GameSimEventSocketsUnlocked",
        "BazaarGameShared"
    );
    var socketType = RequireExternalType(
        "BazaarGameShared.Domain.Core.Types.EContainerSocketId",
        "BazaarGameShared"
    );
    var socketSetType = typeof(HashSet<>).MakeGenericType(socketType);
    var socketSet = Activator.CreateInstance(socketSetType)!;
    Invoke(socketSetType, socketSet, "Add", new[] { Enum.Parse(socketType, socket) });
    return Activator.CreateInstance(
            eventType,
            socketSet,
            (ushort)1,
            ParseEnum(
                "BazaarGameShared.Domain.Core.Types.EInventorySection",
                "BazaarGameShared",
                section
            )
        )
        ?? throw new InvalidOperationException(
            "GameSimEventSocketsUnlocked should be constructible."
        );
}

static object CreateCardSpawnEvent(
    string instanceId,
    string templateId,
    string cardType,
    string combatantId,
    string? section,
    string? socket
)
{
    var eventType = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.GameSimEventCardSpawned",
        "BazaarGameShared"
    );
    return Activator.CreateInstance(
            eventType,
            instanceId,
            templateId,
            ParseEnum("BazaarGameShared.Domain.Core.Types.ECardType", "BazaarGameShared", cardType),
            ParseEnum(
                "BazaarGameShared.Domain.Core.Types.ECombatantId",
                "BazaarGameShared",
                combatantId
            ),
            section == null
                ? null
                : ParseEnum(
                    "BazaarGameShared.Domain.Core.Types.EInventorySection",
                    "BazaarGameShared",
                    section
                ),
            socket == null
                ? null
                : ParseEnum(
                    "BazaarGameShared.Domain.Core.Types.EContainerSocketId",
                    "BazaarGameShared",
                    socket
                )
        )
        ?? throw new InvalidOperationException("GameSimEventCardSpawned should be constructible.");
}

static object CreateGameSimEventList(params object[] events)
{
    var eventInterface = RequireExternalType(
        "BazaarGameShared.Infra.Messages.GameSimEvents.IGameSimEvent",
        "BazaarGameShared"
    );
    var listType = typeof(List<>).MakeGenericType(eventInterface);
    var list = (System.Collections.IList?)Activator.CreateInstance(listType);
    Assert(list != null, "GameSim event list should be constructible.");
    foreach (var value in events)
        list!.Add(value);
    return list!;
}

static void AddGameSimEvent(object message, object gameSimEvent)
{
    var gameSim = GetGameSimData(message);
    AddGameSimEventToData(gameSim.GetType(), gameSim, gameSimEvent);
}

static void AddGameSimEventToData(Type gameSimType, object gameSim, object gameSimEvent)
{
    var events = (System.Collections.IList?)GetFieldValue(gameSimType, gameSim, "Events");
    Assert(events != null, "GameSim should expose an event list.");
    events!.Add(gameSimEvent);
}

static List<object> ReadGameSimEvents(object message)
{
    var gameSim = GetGameSimData(message);
    return AsObjects(GetFieldValue(gameSim.GetType(), gameSim, "Events"));
}

static List<object> AsObjects(object? values)
{
    return ((System.Collections.IEnumerable?)values)?.Cast<object>().ToList() ?? [];
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

file sealed record ReplaySocketFixture(
    string InstanceId,
    string TemplateId,
    string CombatantId,
    string Section,
    string Socket
);

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
