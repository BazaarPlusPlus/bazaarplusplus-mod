using System.Reflection;
using Microsoft.Data.Sqlite;

var payloadStoreType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayPayloadStore");
var captureServiceType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayCaptureService");
var loaderType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayLoader");
var controllerType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayController");
var artifactType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureArtifact");
var manifestType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifest");
var payloadType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayload");
var cardSetCaptureType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCardSetCapture");
var captureStatusType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureStatus");
var captureSourceType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleCaptureSource");
var matcherType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSequenceMatcher");
var collectorType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleSnapshotCollector");
var manifestFactoryType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifestFactory");
var payloadFactoryType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayloadFactory");
var catalogType = RequireType("BazaarPlusPlus.Game.PvpBattles.Persistence.PvpBattleCatalog");
var catalogInterfaceType = RequireType("BazaarPlusPlus.Game.PvpBattles.Persistence.IPvpBattleCatalog");
var catalogStoreType = RequireType("BazaarPlusPlus.Game.PvpBattles.Persistence.PvpBattleSqliteStore");

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
    matcherType != null && collectorType != null && manifestFactoryType != null && payloadFactoryType != null,
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
    capturePatchSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal)
        && capturePatchSource.Contains("new NetMessageObserved", StringComparison.Ordinal),
    "Combat replay capture patch should publish observed messages through the runtime event bus."
);

var runtimeSource = File.ReadAllText(
    Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../Game/CombatReplay/CombatReplayRuntime.cs"
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
    captureServiceSource.Contains("CaptureLiveSnapshots(_candidate);", StringComparison.Ordinal)
        && captureServiceSource.Contains("CaptureLiveSnapshots(candidate);", StringComparison.Ordinal),
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
    captureServiceSource.Contains("CaptureCurrentHandCardsAtOpening(ECombatantId.Player)", StringComparison.Ordinal)
        && captureServiceSource.Contains("CaptureCurrentSkillsAtOpening(ECombatantId.Player)", StringComparison.Ordinal)
        && captureServiceSource.Contains("CaptureOpeningHandCards(message, ECombatantId.Opponent)", StringComparison.Ordinal)
        && captureServiceSource.Contains("CaptureOpponentSkillsFromOpening(message)", StringComparison.Ordinal)
        && captureServiceSource.Contains("GameSimEventCardSpawned", StringComparison.Ordinal)
        && captureServiceSource.Contains("GameSimEventPlayerSkillEquipped", StringComparison.Ordinal),
    "Combat replay capture should read player skills from current Data and opponent skills from opening GameSim events."
);
Assert(
    captureServiceSource.Contains("return state == ERunState.PVPCombat;", StringComparison.Ordinal)
        && captureServiceSource.Contains("IsAnyCombatOpeningMessage", StringComparison.Ordinal),
    "Combat replay capture should only open new PVP candidates from PVPCombat states and reject other combat openings as closers."
);
Assert(
    captureServiceSource.Contains("OpponentName = candidate.OpponentName", StringComparison.Ordinal)
        && captureServiceSource.Contains("OpponentAccountId = candidate.OpponentAccountId", StringComparison.Ordinal),
    "Combat replay capture should bind opponent identity to the opening candidate instead of reading it from global state during record creation."
);
Assert(
    runtimeSource.Contains("EnsureReplayBootstrapReadyAsync", StringComparison.Ordinal),
    "Combat replay runtime should expose a dedicated saved replay bootstrap entrypoint."
);
Assert(
    runtimeSource.Contains("CapturePvpBattle(manifest)", StringComparison.Ordinal),
    "Combat replay runtime should forward saved battle metadata into run logging."
);
Assert(
    runtimeSource.Contains("_battleCatalog = new PvpBattleCatalog", StringComparison.Ordinal)
        && runtimeSource.Contains("_payloadStore = new CombatReplayPayloadStore", StringComparison.Ordinal)
        && runtimeSource.IndexOf("_payloadStore.Save(payload);", StringComparison.Ordinal)
            < runtimeSource.IndexOf("_battleCatalog.Save(manifest);", StringComparison.Ordinal),
    "Combat replay runtime should persist payloads before making battles visible via the catalog."
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
    debugPanelSource.Contains("ListRecentBattles", StringComparison.Ordinal),
    "Debug panel should read saved battles from the catalog-backed runtime API."
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
    SetProperty(payloadType, payload!, "SpawnMessageBase64", Convert.ToBase64String(new byte[] { 1, 2, 3 }));
    SetProperty(payloadType, payload!, "CombatMessageBase64", Convert.ToBase64String(new byte[] { 4, 5, 6 }));
    SetProperty(payloadType, payload!, "DespawnMessageBase64", Convert.ToBase64String(new byte[] { 7, 8, 9 }));
    Invoke(payloadStoreType, payloadStore!, "Save", new object?[] { payload! });
    Assert(
        Equals(Invoke(payloadStoreType, payloadStore!, "Exists", new object?[] { "battle-001" }), true),
        "Payload store should report saved payloads."
    );
    var loadedPayload = Invoke(payloadStoreType, payloadStore!, "Load", new object?[] { "battle-001" });
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
    var loadedManifest = Invoke(catalogType, battleCatalog!, "TryLoad", new object?[] { "battle-001" });
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
        legacyCommand.CommandText =
            """
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
    Assert(migratedBattleCatalog != null, "PvpBattleCatalog should migrate legacy pvp_battles tables.");
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
    Assert(
        ignoredResult == null,
        "A non-PVP combat opening should not start replay capture."
    );
    ignoredResult = Invoke(
        captureServiceType,
        captureService!,
        "Accept",
        new object?[] { combatMessage, "run-ignore" }
    );
    Assert(
        ignoredResult == null,
        "A CombatSim after a non-PVP opening should still be ignored."
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

    var battleId = (string?)GetProperty(manifestType, completedManifest!, "BattleId");
    var loadedManifestFromController = Invoke(
        controllerType,
        controller!,
        "LoadBattle",
        new object?[] { battleId! }
    );
    Assert(loadedManifestFromController != null, "Controller should load a saved battle manifest by id.");
    var loadedPayloadFromController = Invoke(
        controllerType,
        controller!,
        "LoadPayload",
        new[] { loadedManifestFromController }
    );
    Assert(loadedPayloadFromController != null, "Controller should load a saved replay payload by battle id.");
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
    SetProperty(snapshotType, snapshot, "Name", instanceId.StartsWith("p-skill", StringComparison.Ordinal) ? "Arcane Mastery" : "Sparkblade");
    SetProperty(snapshotType, snapshot, "Tier", "Gold");
    SetProperty(snapshotType, snapshot, "Enchant", "Radiant");
    SetProperty(snapshotType, snapshot, "Tags", new List<string> { "Weapon", "Burst" });
    SetProperty(
        snapshotType,
        snapshot,
        "Attributes",
        new Dictionary<string, int>
        {
            ["Damage"] = 42,
            ["Cooldown"] = 3,
        }
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
