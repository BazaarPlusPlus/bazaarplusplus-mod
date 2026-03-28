using System.Reflection;

RequireType("BazaarPlusPlus.Core.Runtime.BppRuntimeHost");
RequireType("BazaarPlusPlus.Core.Runtime.BppRuntimeServices");
RequireType("BazaarPlusPlus.Core.Runtime.IBppFeature");
RequireType("BazaarPlusPlus.Core.Runtime.BppFeatureRegistry");
RequireType("BazaarPlusPlus.Core.Events.IBppEventBus");
RequireType("BazaarPlusPlus.Core.Events.InMemoryBppEventBus");
RequireType("BazaarPlusPlus.Core.Config.IBppConfig");
RequireType("BazaarPlusPlus.Core.Paths.IPathService");
RequireType("BazaarPlusPlus.IMonsterCatalog");
RequireType("BazaarPlusPlus.Core.RunContext.IRunContext");
RequireType("BazaarPlusPlus.Core.GameState.IGameStateProbe");
RequireType("BazaarPlusPlus.Core.Config.BppConfig");
RequireType("BazaarPlusPlus.Core.Paths.BppPathService");
RequireType("BazaarPlusPlus.Core.RunContext.RunContextStore");
RequireType("BazaarPlusPlus.Core.GameState.GameStateProbe");
RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadSqliteStore");
RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadService");
RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadKeyStore");
RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadClientStateStore");
RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadRegistrationClient");
RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadRequestSigner");
RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadApiClient");
RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadEndpointSet");
RequireType("BazaarPlusPlus.Core.Events.RunInitializedObserved");
RequireType("BazaarPlusPlus.Core.Events.NetMessageObserved");
RequireType("BazaarPlusPlus.Core.Events.CombatSimObserved");
RequireType("BazaarPlusPlus.Core.Events.CombatFrameAdvanced");
RequireType("BazaarPlusPlus.Core.Events.RunLifecycleChanged");
RequireType("BazaarPlusPlus.Core.Events.RunLoggingSyncRequested");
RequireType("BazaarPlusPlus.Core.Events.PvpBattleRecorded");
RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayModule");
RequireType("BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBarModule");
RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingModule");
RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterTrackingFeature");
RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterTrackingController");
RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterTracker");

var runtimeHostType = RequireType("BazaarPlusPlus.Core.Runtime.BppRuntimeHost");
var runtimeServicesType = RequireType("BazaarPlusPlus.Core.Runtime.BppRuntimeServices");
var bppFeatureType = RequireType("BazaarPlusPlus.Core.Runtime.IBppFeature");
var bppFeatureRegistryType = RequireType("BazaarPlusPlus.Core.Runtime.BppFeatureRegistry");
var encounterTrackerType = RequireType("BazaarPlusPlus.Game.EncounterTracking.EncounterTracker");
var runLifecycleModuleType = RequireType("BazaarPlusPlus.Game.RunLifecycle.RunLifecycleModule");
var combatReplayModuleType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayModule");
var combatStatusBarModuleType = RequireType("BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBarModule");
var encounterTrackingFeatureType = RequireType(
    "BazaarPlusPlus.Game.EncounterTracking.EncounterTrackingFeature"
);
Assert(
    bppFeatureType.IsAssignableFrom(runLifecycleModuleType),
    "RunLifecycleModule should implement IBppFeature."
);
Assert(
    bppFeatureType.IsAssignableFrom(combatReplayModuleType),
    "CombatReplayModule should implement IBppFeature."
);
Assert(
    bppFeatureType.IsAssignableFrom(combatStatusBarModuleType),
    "CombatStatusBarModule should implement IBppFeature."
);
Assert(
    bppFeatureType.IsAssignableFrom(encounterTrackingFeatureType),
    "EncounterTrackingFeature should implement IBppFeature."
);
AssertHasNonPublicInstanceFieldOfType(
    runtimeHostType,
    bppFeatureRegistryType,
    "BppRuntimeHost should hold a non-public instance BppFeatureRegistry field."
);
AssertPublicInstanceMethod(
    bppFeatureRegistryType,
    "Register",
    [bppFeatureType],
    "BppFeatureRegistry should expose Register(IBppFeature)."
);
AssertPublicInstanceMethod(
    bppFeatureRegistryType,
    "Start",
    Type.EmptyTypes,
    "BppFeatureRegistry should expose Start()."
);
AssertPublicInstanceMethod(
    bppFeatureRegistryType,
    "Stop",
    Type.EmptyTypes,
    "BppFeatureRegistry should expose Stop()."
);
AssertReadableProperty(
    runtimeHostType,
    "Services",
    runtimeServicesType,
    "BppRuntimeHost should expose a readable instance Services property typed as BppRuntimeServices."
);
AssertReadableProperty(
    runtimeServicesType,
    "EventBus",
    RequireType("BazaarPlusPlus.Core.Events.IBppEventBus"),
    "BppRuntimeServices.EventBus should expose IBppEventBus."
);
AssertReadableProperty(
    runtimeServicesType,
    "Config",
    RequireType("BazaarPlusPlus.Core.Config.IBppConfig"),
    "BppRuntimeServices.Config should expose IBppConfig."
);
AssertReadableProperty(
    runtimeServicesType,
    "Paths",
    RequireType("BazaarPlusPlus.Core.Paths.IPathService"),
    "BppRuntimeServices.Paths should expose IPathService."
);
AssertReadableProperty(
    runtimeServicesType,
    "MonsterCatalog",
    RequireType("BazaarPlusPlus.IMonsterCatalog"),
    "BppRuntimeServices.MonsterCatalog should expose IMonsterCatalog."
);
AssertReadableProperty(
    runtimeServicesType,
    "RunContext",
    RequireType("BazaarPlusPlus.Core.RunContext.IRunContext"),
    "BppRuntimeServices.RunContext should expose IRunContext."
);
AssertReadableProperty(
    runtimeServicesType,
    "GameStateProbe",
    RequireType("BazaarPlusPlus.Core.GameState.IGameStateProbe"),
    "BppRuntimeServices.GameStateProbe should expose IGameStateProbe."
);

var pluginSource = ReadSource("Plugin.cs");
Assert(
    pluginSource.Contains("BppRuntimeHost", StringComparison.Ordinal),
    "Plugin should reference BppRuntimeHost."
);
Assert(
    pluginSource.Contains("_runtimeHost.Install();", StringComparison.Ordinal),
    "Plugin should install the runtime host."
);
Assert(
    pluginSource.Contains("_runtimeHost.Start();", StringComparison.Ordinal),
    "Plugin should start the runtime host."
);
Assert(
    pluginSource.Contains("gameObject.AddComponent<RunUploadController>();", StringComparison.Ordinal),
    "Plugin should mount the delayed run-upload controller."
);
Assert(
    !pluginSource.Contains("MonsterDatabase.Load();", StringComparison.Ordinal),
    "Plugin should not preload monster data through a global static database."
);
Assert(
    !pluginSource.Contains("CombatStatusBar.InitializeConfig", StringComparison.Ordinal),
    "Plugin should not initialize CombatStatusBar config directly."
);
Assert(
    !pluginSource.Contains("new HistoryPanelRepository", StringComparison.Ordinal),
    "Plugin should not instantiate HistoryPanelRepository directly."
);
Assert(
    !pluginSource.Contains("new CombatReplayPayloadStore", StringComparison.Ordinal),
    "Plugin should not instantiate CombatReplayPayloadStore directly."
);
Assert(
    !pluginSource.Contains("EncounterTracker.Initialize", StringComparison.Ordinal)
        && !pluginSource.Contains("EncounterTracker.Subscribe", StringComparison.Ordinal),
    "Plugin should not directly initialize or subscribe EncounterTracker once runtime feature composition is in place."
);

Assert(
    !File.Exists(
        Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../Models/ModState.cs")
        )
    ),
    "ModState compatibility shell should be removed."
);

var runtimeHostSource = ReadSource("Core/Runtime/BppRuntimeHost.cs");
Assert(
    runtimeHostSource.Contains("Current?.Services.EventBus", StringComparison.Ordinal)
        && runtimeHostSource.Contains("Current?.Services.Config", StringComparison.Ordinal)
        && runtimeHostSource.Contains("Current?.Services.Paths", StringComparison.Ordinal)
        && runtimeHostSource.Contains("Current?.Services.MonsterCatalog", StringComparison.Ordinal)
        && runtimeHostSource.Contains("Current?.Services.RunContext", StringComparison.Ordinal)
        && runtimeHostSource.Contains("Current?.Services.GameStateProbe", StringComparison.Ordinal),
    "BppRuntimeHost static shims should route EventBus/Config/Paths/MonsterCatalog/RunContext/GameStateProbe through Current.Services."
);
Assert(
    runtimeHostSource.Contains("public static IBppConfig Config", StringComparison.Ordinal),
    "BppRuntimeHost should expose configuration through BppConfig."
);

var configInterfaceSource = ReadSource("Core/Config/IBppConfig.cs");
Assert(
    configInterfaceSource.Contains("EnableCombatStatusBarConfig", StringComparison.Ordinal),
    "IBppConfig should expose the CombatStatusBar enabled config."
);
Assert(
    configInterfaceSource.Contains("VisibleCombatStatusBarConfig", StringComparison.Ordinal),
    "IBppConfig should expose the CombatStatusBar visible config."
);
Assert(
    configInterfaceSource.Contains(
        "CombatStatusBarSpeedMultiplierConfig",
        StringComparison.Ordinal
    ),
    "IBppConfig should expose the CombatStatusBar speed config."
);
Assert(
    configInterfaceSource.Contains("EnableRunUploadConfig", StringComparison.Ordinal)
        && configInterfaceSource.Contains("RunUploadEndpointConfig", StringComparison.Ordinal)
        && configInterfaceSource.Contains("RunUploadRegistrationEndpointConfig", StringComparison.Ordinal),
    "IBppConfig should expose run-upload configuration."
);

var configSource = ReadSource("Core/Config/BppConfig.cs");
Assert(
    configSource.Contains("EnableCombatStatusBarConfig", StringComparison.Ordinal),
    "BppConfig should bind the CombatStatusBar enabled config."
);
Assert(
    configSource.Contains("VisibleCombatStatusBarConfig", StringComparison.Ordinal),
    "BppConfig should bind the CombatStatusBar visible config."
);
Assert(
    configSource.Contains("CombatStatusBarSpeedMultiplierConfig", StringComparison.Ordinal),
    "BppConfig should bind the CombatStatusBar speed config."
);
Assert(
    configSource.Contains("EnableRunUploadConfig", StringComparison.Ordinal)
        && configSource.Contains("RunUploadEndpointConfig", StringComparison.Ordinal)
        && configSource.Contains("RunUploadRegistrationEndpointConfig", StringComparison.Ordinal),
    "BppConfig should bind run-upload settings."
);
Assert(
    runtimeHostSource.Contains("public static IRunContext RunContext", StringComparison.Ordinal),
    "BppRuntimeHost should expose run context through RunContextStore."
);
Assert(
    runtimeHostSource.Contains("public static IPathService Paths", StringComparison.Ordinal),
    "BppRuntimeHost should expose paths through BppPathService."
);
Assert(
    runtimeHostSource.Contains("public static IMonsterCatalog MonsterCatalog", StringComparison.Ordinal),
    "BppRuntimeHost should expose monster data through an IMonsterCatalog boundary."
);
Assert(
    !runtimeHostSource.Contains("OnNetMessageObserved", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("OnCombatSimObserved", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("OnCombatFrameAdvanced", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("OnRunInitializedObserved", StringComparison.Ordinal),
    "BppRuntimeHost should compose feature modules instead of routing feature events itself."
);
Assert(
    !runtimeHostSource.Contains("_runLifecycle.Start();", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("_combatReplayModule.Start();", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("_combatStatusBarModule.Start();", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("_runLifecycle.Stop();", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("_combatReplayModule.Stop();", StringComparison.Ordinal)
        && !runtimeHostSource.Contains("_combatStatusBarModule.Stop();", StringComparison.Ordinal),
    "BppRuntimeHost should not directly start or stop feature modules once registered."
);

var logSource = ReadSource("Infrastructure/BppLog.cs");
Assert(
    logSource.Contains("Logger =>", StringComparison.Ordinal),
    "BppLog should resolve the logger through an accessor instead of raw state writes."
);

var runInitializedPatchSource = ReadSource("Patches/RunLogging/RunInitializedPatch.cs");
Assert(
    runInitializedPatchSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal),
    "RunInitializedPatch should publish through the event bus."
);
Assert(
    !runInitializedPatchSource.Contains("ModState.CurrentServerRunId", StringComparison.Ordinal),
    "RunInitializedPatch should not write ModState directly."
);

var replayPatchSource = ReadSource("Patches/Combat/CombatReplayCapturePatch.cs");
Assert(
    replayPatchSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal),
    "CombatReplayCapturePatch should publish observed net messages through the event bus."
);
Assert(
    !replayPatchSource.Contains("CombatReplayRuntime.Instance", StringComparison.Ordinal),
    "CombatReplayCapturePatch should no longer call CombatReplayRuntime.Instance directly."
);

var combatPatchSource = ReadSource("Patches/Combat/CombatSimulationPatches.cs");
Assert(
    combatPatchSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal),
    "Combat simulation patches should publish combat events through the event bus."
);
Assert(
    !combatPatchSource.Contains("CombatStatusBar.SetCombatFrameTotal", StringComparison.Ordinal),
    "Combat simulation patches should not call CombatStatusBar state methods directly."
);
Assert(
    !combatPatchSource.Contains("CombatStatusBar.AdvanceCombatFrame", StringComparison.Ordinal),
    "Combat frame advance patch should not call CombatStatusBar directly."
);

var runLifecycleSource = ReadSource("Game/RunLifecycle/RunLifecycleModule.cs");
Assert(
    runLifecycleSource.Contains("Subscribe<RunInitializedObserved>", StringComparison.Ordinal),
    "RunLifecycleModule should consume run initialization through the event bus."
);
Assert(
    runLifecycleSource.Contains("_eventBus.Publish(", StringComparison.Ordinal)
        && runLifecycleSource.Contains("new RunLifecycleChanged", StringComparison.Ordinal),
    "RunLifecycleModule should publish lifecycle changes for dependent modules."
);
Assert(
    !runLifecycleSource.Contains("EncounterTracker.ResetEncounterState", StringComparison.Ordinal),
    "RunLifecycleModule should not reach into encounter tracking directly."
);

var encounterTrackerSource = ReadSource("Game/EncounterTracker.cs");
Assert(
    !encounterTrackerSource.Contains("RunLoggingController.Instance", StringComparison.Ordinal),
    "EncounterTracker should not call run logging directly."
);
Assert(
    !encounterTrackerSource.Contains("OnCardDealt(", StringComparison.Ordinal)
        && !encounterTrackerSource.Contains("Events.CardDealtSimEvent.AddListener", StringComparison.Ordinal)
        && !encounterTrackerSource.Contains("BuildMonsterPreviews(", StringComparison.Ordinal)
        && !encounterTrackerSource.Contains("new EncounterTrackingModule(", StringComparison.Ordinal),
    "EncounterTracker should remain a compatibility shell and delegate orchestration to EncounterTrackingFeature."
);

var encounterTrackerFields = encounterTrackerType.GetFields(
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    !encounterTrackerFields.Any(field => field.FieldType.FullName == "BazaarPlusPlus.Game.EncounterTracking.EncounterTrackingModule"),
    "EncounterTracker should not store EncounterTrackingModule directly."
);
Assert(
    encounterTrackerFields.Any(field => field.FieldType == encounterTrackingFeatureType),
    "EncounterTracker should keep a compatibility reference to EncounterTrackingFeature."
);

var monsterDatabaseSource = ReadSource("Data/MonsterDatabase.cs");
Assert(
    !monsterDatabaseSource.Contains("LegacyMonsterEntry", StringComparison.Ordinal)
        && !monsterDatabaseSource.Contains("TryGet(string encounterInternalName)", StringComparison.Ordinal),
    "MonsterDatabase should remove unused legacy entry APIs once the catalog boundary is in place."
);

var runStateSyncSource = ReadSource("Game/RunStateSyncController.cs");
Assert(
    runStateSyncSource.Contains("BppRuntimeHost.EventBus.Publish", StringComparison.Ordinal)
        || runStateSyncSource.Contains("_eventBus.Publish", StringComparison.Ordinal)
            && runStateSyncSource.Contains("new RunLoggingSyncRequested", StringComparison.Ordinal),
    "RunStateSyncController should publish run-logging sync requests through an event bus instead of calling the controller directly."
);
Assert(
    !runStateSyncSource.Contains("RunLoggingController.Instance", StringComparison.Ordinal),
    "RunStateSyncController should not call RunLoggingController directly."
);
Assert(
    runStateSyncSource.Contains("namespace BazaarPlusPlus.Game.RunLogging;", StringComparison.Ordinal)
        && !runStateSyncSource.Contains("BppRuntimeHost.", StringComparison.Ordinal),
    "RunStateSyncController should live in the run-logging namespace and avoid runtime-host statics."
);

var runLoggingModuleSource = ReadSource("Game/RunLogging/RunLoggingModule.cs");
Assert(
    runLoggingModuleSource.Contains("Subscribe<SelectionObserved>", StringComparison.Ordinal)
        && runLoggingModuleSource.Contains(
            "Subscribe<RunLoggingSyncRequested>",
            StringComparison.Ordinal
        )
        && runLoggingModuleSource.Contains(
            "Subscribe<PvpBattleRecorded>",
            StringComparison.Ordinal
        ),
    "RunLoggingModule should be the unified subscriber for run-logging capture inputs."
);

var historyPanelSource = ReadSource("Game/HistoryPanel/HistoryPanel.cs");
var historyPanelControllerSource = ReadSource("Game/HistoryPanel/HistoryPanelController.cs");
var historyPanelRepositorySource = ReadSource("Game/HistoryPanel/HistoryPanelRepository.cs");
Assert(
    historyPanelSource.Contains("HistoryPanelRepository", StringComparison.Ordinal),
    "HistoryPanel should delegate persistence through HistoryPanelRepository."
);
Assert(
    historyPanelSource.Contains("HistoryPanelPreviewRenderer", StringComparison.Ordinal),
    "HistoryPanel should delegate preview rendering through HistoryPanelPreviewRenderer."
);
Assert(
    historyPanelRepositorySource.Contains("SqliteConnection", StringComparison.Ordinal)
        && historyPanelRepositorySource.Contains("JsonSerializerSettings", StringComparison.Ordinal),
    "HistoryPanelRepository should own low-level sqlite/json concerns."
);
Assert(
    !historyPanelSource.Contains("SqliteConnection", StringComparison.Ordinal)
        && !historyPanelSource.Contains("JsonSerializerSettings", StringComparison.Ordinal),
    "HistoryPanel should avoid direct low-level sqlite/json concerns."
);
Assert(
    historyPanelSource.Contains("namespace BazaarPlusPlus.Game.HistoryPanel;", StringComparison.Ordinal)
        && historyPanelControllerSource.Contains(
            "namespace BazaarPlusPlus.Game.HistoryPanel;",
            StringComparison.Ordinal
        ),
    "HistoryPanel feature files should move into BazaarPlusPlus.Game.HistoryPanel."
);
Assert(
    !historyPanelSource.Contains("BppRuntimeHost.", StringComparison.Ordinal)
        && !historyPanelControllerSource.Contains("BppRuntimeHost.", StringComparison.Ordinal),
    "Migrated HistoryPanel files should not keep runtime-host static access."
);

var tooltipRefreshSource = ReadSource("Game/Tooltips/TooltipModifierRefreshController.cs");
Assert(
    tooltipRefreshSource.Contains(
        "namespace BazaarPlusPlus.Game.Tooltips;",
        StringComparison.Ordinal
    )
        && !tooltipRefreshSource.Contains("BppRuntimeHost.", StringComparison.Ordinal),
    "TooltipModifierRefreshController should move into BazaarPlusPlus.Game.Tooltips and avoid runtime-host statics."
);

var keyBindRowSource = ReadSource("Game/Input/BppKeyBindRowController.cs");
var keyBindingsSource = ReadSource("Game/Input/KeyBindings.cs");
Assert(
    keyBindRowSource.Contains("namespace BazaarPlusPlus.Game.Input;", StringComparison.Ordinal)
        && keyBindingsSource.Contains("namespace BazaarPlusPlus.Game.Input;", StringComparison.Ordinal),
    "Input feature files should live in BazaarPlusPlus.Game.Input."
);

Assert(
    encounterTrackerSource.Contains(
        "namespace BazaarPlusPlus.Game.EncounterTracking;",
        StringComparison.Ordinal
    )
        && !encounterTrackerSource.Contains("BppRuntimeHost.", StringComparison.Ordinal),
    "EncounterTracker should move into encounter-tracking namespace and avoid runtime-host statics."
);

var uploadControllerSource = ReadSource("Game/RunLogging/Upload/RunUploadController.cs");
Assert(
    uploadControllerSource.Contains("BppRuntimeHost.RunContext.IsInGameRun", StringComparison.Ordinal),
    "RunUploadController should avoid uploads during active runs."
);
Assert(
    uploadControllerSource.Contains("UploadPendingRunsAsync", StringComparison.Ordinal),
    "RunUploadController should drive background upload batches through the upload service."
);

var uploadServiceSource = ReadSource("Game/RunLogging/Upload/RunUploadService.cs");
Assert(
    uploadServiceSource.Contains("RunUploadRegistrationClient", StringComparison.Ordinal)
        && uploadServiceSource.Contains("RunUploadApiClient", StringComparison.Ordinal),
    "RunUploadService should coordinate registration and upload through dedicated collaborators."
);

var requestSignerSource = ReadSource("Game/RunLogging/Upload/RunUploadRequestSigner.cs");
Assert(
    requestSignerSource.Contains("BuildCanonicalRequest", StringComparison.Ordinal)
        && requestSignerSource.Contains("ComputeBodyHash", StringComparison.Ordinal),
    "RunUploadRequestSigner should own request canonicalization and hashing."
);

Console.WriteLine("Architecture modularization smoke checks passed.");

static Type RequireType(string fullName)
{
    var assembly = Assembly.Load("BazaarPlusPlus");
    return assembly.GetType(fullName, throwOnError: false)
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static string ReadSource(string relativePath)
{
    var sourcePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath)
    );
    Assert(File.Exists(sourcePath), $"Source file not found at {sourcePath}");
    return File.ReadAllText(sourcePath);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertReadableProperty(
    Type ownerType,
    string propertyName,
    Type expectedType,
    string message
)
{
    var property = ownerType.GetProperty(
        propertyName,
        BindingFlags.Instance | BindingFlags.Public
    );
    if (property == null)
        throw new InvalidOperationException(message);
    Assert(property.CanRead, message);
    Assert(property.PropertyType == expectedType, message);
    Assert(
        property.SetMethod == null || !property.SetMethod.IsPublic,
        $"{message} Property should not have a public setter."
    );
}

static void AssertHasNonPublicInstanceFieldOfType(Type ownerType, Type fieldType, string message)
{
    var field = Array.Find(
        ownerType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
        f => f.FieldType == fieldType
    );
    Assert(field != null, message);
}

static void AssertPublicInstanceMethod(
    Type ownerType,
    string methodName,
    Type[] parameterTypes,
    string message
)
{
    var method = ownerType.GetMethod(
        methodName,
        BindingFlags.Instance | BindingFlags.Public,
        binder: null,
        types: parameterTypes,
        modifiers: null
    );
    Assert(method != null, message);
}
