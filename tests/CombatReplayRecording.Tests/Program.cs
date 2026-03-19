using System.Reflection;

var recordType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayRecord");
var storeType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayStore");
var captureServiceType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayCaptureService");
var loaderType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayLoader");
var controllerType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayController");

var modStateSource = File.ReadAllText(
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Models/ModState.cs"))
);
Assert(
    modStateSource.Contains("CombatReplayDirectoryPath", StringComparison.Ordinal),
    "ModState should expose a combat replay storage path."
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
    capturePatchSource.Contains("ObserveMessage", StringComparison.Ordinal),
    "Combat replay capture patch should forward messages into the runtime recorder."
);

var runtimeSource = File.ReadAllText(
    Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../Game/CombatReplay/CombatReplayRuntime.cs"
        )
    )
);
Assert(
    runtimeSource.Contains("EnsureReplayBootstrapReadyAsync", StringComparison.Ordinal),
    "Combat replay runtime should expose a dedicated saved replay bootstrap entrypoint."
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
        "await TryInjectSavedReplayAsync(bootstrapContext, record, sequence, replayId);",
        StringComparison.Ordinal
    ),
    "Combat replay runtime should delegate saved replay injection to a dedicated method."
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
        && runtimeSource.Contains("MarkGameSimMessageHandled(gameSimHandler, spawnMessage.MessageId);", StringComparison.Ordinal),
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
        && runtimeSource.Contains("ShowReplayAndRecapButtons(show: false, deactivate: true);", StringComparison.Ordinal),
    "Combat replay runtime should auto-start replay playback after entering ReplayState."
);
Assert(
    runtimeSource.Contains("private static void MarkGameSimMessageHandled", StringComparison.Ordinal)
        && runtimeSource.Contains("\"_handledMessages\"", StringComparison.Ordinal),
    "Combat replay runtime should be able to mark the replay spawn message as handled for ReplayState."
);
Assert(
    runtimeSource.Contains("does not contain player-hand snapshots", StringComparison.Ordinal),
    "Combat replay runtime should warn when an old replay does not contain player-hand snapshots."
);
Assert(
    runtimeSource.Contains("does not contain opponent-hand snapshots", StringComparison.Ordinal)
        && runtimeSource.Contains("does not contain player-skill snapshots", StringComparison.Ordinal)
        && runtimeSource.Contains("does not contain opponent-skill snapshots", StringComparison.Ordinal),
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
        && runtimeSource.Contains("ModState.IsInGameRun", StringComparison.Ordinal),
    "Combat replay runtime should block saved replays only while local gameplay is active."
);
Assert(
    runtimeSource.Contains("SceneID.GameScene", StringComparison.Ordinal)
        && runtimeSource.Contains("SceneID.GameplayLoading", StringComparison.Ordinal),
    "Combat replay runtime should know how to load gameplay scenes from the lobby."
);
Assert(
    runtimeSource.Contains("AppState.Initialize(sharedVariables, processor);", StringComparison.Ordinal),
    "Combat replay runtime should initialize AppState handlers when the lobby bootstrap path does not provide them."
);
Assert(
    runtimeSource.Contains("processor.Handle(spawnMessage)", StringComparison.Ordinal),
    "Combat replay runtime should validate the saved spawn snapshot with NetMessageProcessor before replay injection."
);
Assert(
    runtimeSource.Contains("TryGetAppStateField<GameSimHandler>(\"_gameSimHandler\") != null", StringComparison.Ordinal),
    "Combat replay readiness should require GameSimHandler to exist before replay injection starts."
);
Assert(
    runtimeSource.Contains("SetUpBoard", StringComparison.Ordinal)
        && runtimeSource.Contains("Init(boardManager)", StringComparison.Ordinal),
    "Combat replay runtime should bootstrap board and game services locally without RunManager.StartRun()."
);
Assert(
    runtimeSource.Contains("Replay bootstrap scene environment is ready.", StringComparison.Ordinal),
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
    runtimeSource.Contains("Returning to main menu after bootstrapped replay exit.", StringComparison.Ordinal),
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
var injectReplayBody = ExtractMethodBody(runtimeSource, "private static async Task TryInjectSavedReplayAsync(");
Assert(
    injectReplayBody.IndexOf("HandleSpawnMessageAsync", StringComparison.Ordinal)
        < injectReplayBody.IndexOf("TriggerCombatSequenceCreated", StringComparison.Ordinal)
        && injectReplayBody.IndexOf("TriggerCombatSequenceCreated", StringComparison.Ordinal)
            < injectReplayBody.IndexOf("AppState.TryPushState<ReplayState>()", StringComparison.Ordinal)
        && injectReplayBody.IndexOf("AppState.TryPushState<ReplayState>()", StringComparison.Ordinal)
            < injectReplayBody.IndexOf("replayState.Replay()", StringComparison.Ordinal),
    "Combat replay runtime should notify ReplayState's native sequence handler before entering ReplayState."
);
var triggerReplayBody = ExtractMethodBody(runtimeSource, "private static Action CreateTriggerCombatSequenceCreated(object processor)");
Assert(
    triggerReplayBody.Contains("return () =>", StringComparison.Ordinal)
        && triggerReplayBody.Contains("field?.GetValue(processor) as Action", StringComparison.Ordinal),
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
    debugPanelSource.Contains("ListSavedReplays", StringComparison.Ordinal),
    "Debug panel should read saved combat replays from the runtime."
);
Assert(
    debugPanelSource.Contains("CanReplaySavedCombats", StringComparison.Ordinal),
    "Debug panel should respect replay availability checks instead of always offering replay actions."
);

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-combat-replay-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);

try
{
    var store = Activator.CreateInstance(storeType, tempRoot);
    Assert(store != null, "CombatReplayStore should be constructible with a root path.");

    var record = Activator.CreateInstance(recordType);
    Assert(record != null, "CombatReplayRecord should be constructible.");
    SetProperty(recordType, record!, "ReplayId", "replay-001");
    SetProperty(
        recordType,
        record!,
        "SavedAtUtc",
        new DateTimeOffset(2026, 3, 18, 1, 2, 3, TimeSpan.Zero)
    );
    SetProperty(recordType, record!, "RunId", "run-001");
    SetProperty(recordType, record!, "Day", 3);
    SetProperty(recordType, record!, "Hour", 5);
    SetProperty(recordType, record!, "EncounterId", "encounter-abc");
    SetProperty(recordType, record!, "OpponentName", "Test Opponent");
    SetProperty(recordType, record!, "PlayerHandCards", CreateSnapshotList("p-hand-1", "tpl-p-hand"));
    SetProperty(recordType, record!, "PlayerSkills", CreateSnapshotList("p-skill-1", "tpl-p-skill"));
    SetProperty(recordType, record!, "OpponentHandCards", CreateSnapshotList("o-hand-1", "tpl-o-hand"));
    SetProperty(recordType, record!, "OpponentSkills", CreateSnapshotList("o-skill-1", "tpl-o-skill"));
    SetProperty(
        recordType,
        record!,
        "SpawnMessageBase64",
        Convert.ToBase64String(new byte[] { 1, 2, 3 })
    );
    SetProperty(
        recordType,
        record!,
        "CombatMessageBase64",
        Convert.ToBase64String(new byte[] { 4, 5, 6 })
    );
    SetProperty(
        recordType,
        record!,
        "DespawnMessageBase64",
        Convert.ToBase64String(new byte[] { 7, 8, 9 })
    );

    Invoke(storeType, store!, "Save", new object?[] { record! });

    var listed = (
        (System.Collections.IEnumerable)Invoke(storeType, store!, "List", Array.Empty<object?>())
    )
        .Cast<object>()
        .ToList();
    Assert(listed.Count == 1, "List should return the saved replay record.");

    var listedRecord = listed[0];
    Assert(
        string.Equals(
            (string?)GetProperty(recordType, listedRecord, "ReplayId"),
            "replay-001",
            StringComparison.Ordinal
        ),
        "List should preserve the replay id."
    );

    var loaded = Invoke(storeType, store!, "Load", new object?[] { "replay-001" });
    Assert(loaded != null, "Load should return the saved record by replay id.");
    Assert(
        string.Equals(
            (string?)GetProperty(recordType, loaded!, "OpponentName"),
            "Test Opponent",
            StringComparison.Ordinal
        ),
        "Load should preserve metadata fields."
    );
    Assert(
        string.Equals(
            (string?)GetProperty(recordType, loaded!, "CombatMessageBase64"),
            Convert.ToBase64String(new byte[] { 4, 5, 6 }),
            StringComparison.Ordinal
        ),
        "Load should preserve the serialized combat payload."
    );
    Assert(
        ReadSnapshotCount(recordType, loaded!, "PlayerHandCards") == 1
            && ReadSnapshotCount(recordType, loaded!, "PlayerSkills") == 1
            && ReadSnapshotCount(recordType, loaded!, "OpponentHandCards") == 1
            && ReadSnapshotCount(recordType, loaded!, "OpponentSkills") == 1,
        "Load should preserve card and skill snapshots for both sides."
    );

    var replayFilePath = Path.Combine(tempRoot, "replay-001.json");
    File.WriteAllText(
        replayFilePath,
        File.ReadAllText(replayFilePath).Replace("Test Opponent", "Changed Opponent"),
        System.Text.Encoding.UTF8
    );
    var cachedList = (
        (System.Collections.IEnumerable)Invoke(storeType, store!, "List", Array.Empty<object?>())
    )
        .Cast<object>()
        .ToList();
    Assert(cachedList.Count == 1, "Cached list should still return the saved replay record.");
    Assert(
        string.Equals(
            (string?)GetProperty(recordType, cachedList[0], "OpponentName"),
            "Test Opponent",
            StringComparison.Ordinal
        ),
        "Repeated List calls should reuse the in-memory replay cache until the store is mutated."
    );

    var secondRecord = Activator.CreateInstance(recordType);
    Assert(secondRecord != null, "A second combat replay record should be constructible.");
    SetProperty(recordType, secondRecord!, "ReplayId", "replay-002");
    SetProperty(
        recordType,
        secondRecord!,
        "SavedAtUtc",
        new DateTimeOffset(2026, 3, 18, 1, 3, 3, TimeSpan.Zero)
    );
    SetProperty(recordType, secondRecord!, "RunId", "run-002");
    SetProperty(recordType, secondRecord!, "Day", 3);
    SetProperty(recordType, secondRecord!, "Hour", 6);
    SetProperty(recordType, secondRecord!, "EncounterId", "encounter-def");
    SetProperty(recordType, secondRecord!, "OpponentName", "Newest Opponent");
    SetProperty(
        recordType,
        secondRecord!,
        "SpawnMessageBase64",
        Convert.ToBase64String(new byte[] { 10, 11, 12 })
    );
    SetProperty(
        recordType,
        secondRecord!,
        "CombatMessageBase64",
        Convert.ToBase64String(new byte[] { 13, 14, 15 })
    );
    SetProperty(
        recordType,
        secondRecord!,
        "DespawnMessageBase64",
        Convert.ToBase64String(new byte[] { 16, 17, 18 })
    );
    Invoke(storeType, store!, "Save", new object?[] { secondRecord! });

    var refreshedList = (
        (System.Collections.IEnumerable)Invoke(storeType, store!, "List", Array.Empty<object?>())
    )
        .Cast<object>()
        .ToList();
    Assert(refreshedList.Count == 2, "Saving a replay should invalidate the cached replay list.");
    Assert(
        refreshedList.Any(
            entry =>
                string.Equals(
                    (string?)GetProperty(recordType, entry, "ReplayId"),
                    "replay-002",
                    StringComparison.Ordinal
                )
        ),
        "List should include replays saved after the cache was created."
    );

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
    var combatStart = CreateGameSimMessage(
        "Combat",
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
    var completedRecord = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { combatEnd, "run-42" }
    );
    Assert(completedRecord != null, "A combat triplet should produce a replay record.");
    Assert(
        string.Equals(
            (string?)GetProperty(recordType, completedRecord!, "RunId"),
            "run-42",
            StringComparison.Ordinal
        ),
        "Completed replay records should preserve the run id."
    );
    Assert(
        Equals(GetProperty(recordType, completedRecord!, "Day"), 3),
        "Completed replay records should capture the combat day."
    );
    Assert(
        Equals(GetProperty(recordType, completedRecord!, "Hour"), 4),
        "Completed replay records should capture the combat hour from the opening snapshot."
    );
    Assert(
        string.Equals(
            (string?)GetProperty(recordType, completedRecord!, "EncounterId"),
            "encounter-live",
            StringComparison.Ordinal
        ),
        "Completed replay records should preserve the opening encounter id."
    );
    Assert(
        string.Equals(
            (string?)GetProperty(recordType, completedRecord!, "OpponentName"),
            "Rival",
            StringComparison.Ordinal
        ),
        "Completed replay records should preserve opponent metadata."
    );
    Assert(
        !string.IsNullOrWhiteSpace(
            (string?)GetProperty(recordType, completedRecord!, "SpawnMessageBase64")
        ),
        "Completed replay records should serialize the opening GameSim."
    );
    Assert(
        !string.IsNullOrWhiteSpace(
            (string?)GetProperty(recordType, completedRecord!, "CombatMessageBase64")
        ),
        "Completed replay records should serialize the CombatSim."
    );
    Assert(
        !string.IsNullOrWhiteSpace(
            (string?)GetProperty(recordType, completedRecord!, "DespawnMessageBase64")
        ),
        "Completed replay records should serialize the closing GameSim."
    );
    Assert(
        GetProperty(recordType, completedRecord!, "PlayerHandCards") != null
            && GetProperty(recordType, completedRecord!, "PlayerSkills") != null
            && GetProperty(recordType, completedRecord!, "OpponentHandCards") != null
            && GetProperty(recordType, completedRecord!, "OpponentSkills") != null,
        "Completed replay records should capture card and skill snapshot collections for both sides."
    );

    Invoke(storeType, store!, "Save", new object?[] { completedRecord! });

    var loader = Activator.CreateInstance(loaderType);
    Assert(loader != null, "CombatReplayLoader should be constructible.");
    var loadedSequence = Invoke(loaderType, loader!, "Load", new object?[] { completedRecord! });
    Assert(loadedSequence != null, "CombatReplayLoader should deserialize a saved replay record.");
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

var controller = Activator.CreateInstance(controllerType, store, loader);
Assert(controller != null, "CombatReplayController should be constructible.");
    var savedReplays = (
        (System.Collections.IEnumerable)Invoke(
            controllerType,
            controller!,
            "ListSavedReplays",
            Array.Empty<object?>()
        )
    )
        .Cast<object>()
        .ToList();
    Assert(savedReplays.Count == 3, "Controller should expose the saved replay list.");

    var replayId = (string?)GetProperty(recordType, completedRecord!, "ReplayId");
    var loadedRecordFromController = Invoke(
        controllerType,
        controller!,
        "LoadReplayRecord",
        new object?[] { replayId! }
    );
    Assert(loadedRecordFromController != null, "Controller should load a saved replay record by id.");
    var loadedFromController = Invoke(
        controllerType,
        controller!,
        "LoadReplay",
        new[] { loadedRecordFromController }
    );
    Assert(loadedFromController != null, "Controller should deserialize a loaded replay record.");
    Assert(
        string.Equals(
            (string?)GetProperty(controllerType, controller!, "ActiveReplayId"),
            replayId,
            StringComparison.Ordinal
        ),
        "Controller should track the active saved replay id."
    );

    Console.WriteLine("CombatReplayRecording store checks passed.");
}
finally
{
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
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
    var list = Activator.CreateInstance(listType)
        ?? throw new InvalidOperationException("Snapshot list should be constructible.");
    var snapshot = Activator.CreateInstance(snapshotType)
        ?? throw new InvalidOperationException("Snapshot should be constructible.");
    SetProperty(snapshotType, snapshot, "InstanceId", instanceId);
    SetProperty(snapshotType, snapshot, "TemplateId", templateId);
    SetProperty(
        snapshotType,
        snapshot,
        "Type",
        ParseEnum("BazaarGameShared.Domain.Core.Types.ECardType", "BazaarGameShared", "Skill")
    );
    Invoke(listType, list, "Add", new[] { snapshot });
    return list;
}

static int ReadSnapshotCount(Type recordType, object instance, string propertyName)
{
    return ((System.Collections.IEnumerable?)GetProperty(recordType, instance, propertyName))
        ?.Cast<object>()
        .Count()
        ?? 0;
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
    return Activator.CreateInstance(
        simPvpOpponentType,
        opponentName,
        null,
        null,
        null,
        0,
        null,
        null,
        null,
        null,
        null,
        Activator.CreateInstance(loadoutType),
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
