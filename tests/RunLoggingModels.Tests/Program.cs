using System.Reflection;

RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogCreateRequest");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogSessionState");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogEvent");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogCheckpoint");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogPendingSelectionState");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogCompletion");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogAbandonment");
RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingGameDataReader");

var storeType = RequireType("BazaarPlusPlus.Game.RunLogging.Persistence.IRunLogStore");
RequireMethod(storeType, "TryResumeActiveRun");
RequireMethod(storeType, "CreateRun");
RequireMethod(storeType, "AppendEvent");
RequireMethod(storeType, "SaveCheckpoint");
RequireMethod(storeType, "CompleteRun");
RequireMethod(storeType, "MarkRunAbandoned");

var eventType = RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogEvent");
RequireProperty(eventType, "SchemaVersion");
RequireProperty(eventType, "RunId");
RequireProperty(eventType, "Seq");
RequireProperty(eventType, "Ts");
RequireProperty(eventType, "Kind");
RequireProperty(eventType, "SelectedEncounterId");
RequireProperty(eventType, "AbandonedReason");

var controllerSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Game/RunLogging/RunLoggingController.cs")
);
Assert(File.Exists(controllerSourcePath), $"Controller source not found at {controllerSourcePath}");
var controllerSource = File.ReadAllText(controllerSourcePath);
Assert(
    controllerSource.Contains(
        "internal sealed class RunLoggingController : MonoBehaviour",
        StringComparison.Ordinal
    ),
    "RunLoggingController should exist as a MonoBehaviour runtime entry point."
);
Assert(
    controllerSource.Contains("new SqliteRunLogStore(", StringComparison.Ordinal),
    "RunLoggingController should create SqliteRunLogStore."
);
Assert(
    controllerSource.Contains("new RunLoggingModule(", StringComparison.Ordinal),
    "RunLoggingController should compose the unified RunLoggingModule."
);
Assert(
    controllerSource.Contains(
        "() => CombatReplayRuntime.Instance?.HasPendingPersistence == true",
        StringComparison.Ordinal
    ),
    "RunLoggingController should provide replay persistence state to RunLoggingModule through composition."
);
Assert(
    !controllerSource.Contains("JsonRunLogStore", StringComparison.Ordinal),
    "RunLoggingController should no longer reference JsonRunLogStore."
);

var runLoggingModuleSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Game/RunLogging/RunLoggingModule.cs")
);
Assert(
    File.Exists(runLoggingModuleSourcePath),
    $"RunLoggingModule source not found at {runLoggingModuleSourcePath}"
);
var runLoggingModuleSource = File.ReadAllText(runLoggingModuleSourcePath);
Assert(
    runLoggingModuleSource.Contains("catch (Exception ex)", StringComparison.Ordinal),
    "RunLoggingModule should isolate capture failures behind exception guards."
);
Assert(
    runLoggingModuleSource.Contains("BppLog.Error", StringComparison.Ordinal),
    "RunLoggingModule should log capture failures instead of throwing through event handlers."
);
Assert(
    runLoggingModuleSource.Contains("completionAttempted", StringComparison.Ordinal),
    "RunLoggingModule should track whether leave-run completion was attempted before clearing the retry edge."
);
Assert(
    runLoggingModuleSource.Contains("completionSucceeded", StringComparison.Ordinal),
    "RunLoggingModule should only clear the leave-run retry edge after completion succeeds."
);
Assert(
    runLoggingModuleSource.Contains("selection_abandoned", StringComparison.Ordinal),
    "RunLoggingModule should emit selection_abandoned when a pending selection cannot be resolved cleanly."
);
Assert(
    !runLoggingModuleSource.Contains("CombatReplayRuntime.Instance", StringComparison.Ordinal),
    "RunLoggingModule should not reach into CombatReplayRuntime.Instance directly."
);

var pluginSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Plugin.cs")
);
Assert(File.Exists(pluginSourcePath), $"Plugin source not found at {pluginSourcePath}");
var pluginSource = File.ReadAllText(pluginSourcePath);
Assert(
    pluginSource.Contains(
        "gameObject.AddComponent<RunLoggingController>();",
        StringComparison.Ordinal
    ),
    "Plugin.Awake should mount RunLoggingController."
);

var pathServiceSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Core/Paths/BppPathService.cs")
);
Assert(
    File.Exists(pathServiceSourcePath),
    $"Path service source not found at {pathServiceSourcePath}"
);
var pathServiceSource = File.ReadAllText(pathServiceSourcePath);
Assert(
    pathServiceSource.Contains("RunLogDatabasePath", StringComparison.Ordinal),
    "BppPathService should expose a SQLite database path."
);

var runContextSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Core/RunContext/RunContextStore.cs")
);
Assert(
    File.Exists(runContextSourcePath),
    $"Run context source not found at {runContextSourcePath}"
);
var runContextSource = File.ReadAllText(runContextSourcePath);
Assert(
    runContextSource.Contains("CurrentServerRunId", StringComparison.Ordinal),
    "RunContextStore should cache the authoritative server run id."
);
Assert(
    runContextSource.Contains("LastRunExitKind", StringComparison.Ordinal),
    "RunContextStore should track how the active run exited."
);
Assert(
    !pathServiceSource.Contains("RunLogRootPath", StringComparison.Ordinal),
    "Run logging path service should not expose a JSON run log root path."
);

var gameDataReaderSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Game/GameDataReader.cs")
);
Assert(
    File.Exists(gameDataReaderSourcePath),
    $"GameDataReader source not found at {gameDataReaderSourcePath}"
);
var gameDataReaderSource = File.ReadAllText(gameDataReaderSourcePath);
Assert(
    !gameDataReaderSource.Contains("RunLogCreateRequest", StringComparison.Ordinal)
        && !gameDataReaderSource.Contains("RunLogRunProgressInput", StringComparison.Ordinal)
        && !gameDataReaderSource.Contains("RunLogSelectionSnapshotInput", StringComparison.Ordinal),
    "GameDataReader should stay focused on game snapshots instead of run logging DTO assembly."
);
Assert(
    !gameDataReaderSource.Contains("RunIdFactory.Create(", StringComparison.Ordinal),
    "GameDataReader should not synthesize run ids in the runtime logging path."
);
Assert(
    !gameDataReaderSource.Contains("Status = \"completed\"", StringComparison.Ordinal),
    "GameDataReader should not hard-code completed terminal status."
);

var runLoggingReaderSourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Game/RunLogging/RunLoggingGameDataReader.cs"
    )
);
Assert(
    File.Exists(runLoggingReaderSourcePath),
    $"Run logging game-data reader source not found at {runLoggingReaderSourcePath}"
);
var runLoggingReaderSource = File.ReadAllText(runLoggingReaderSourcePath);
Assert(
    runLoggingReaderSource.Contains("CurrentServerRunId", StringComparison.Ordinal),
    "Run logging game-data reader should require the server run id before building a run log create request."
);
Assert(
    runLoggingReaderSource.Contains("Data.Run.Hour", StringComparison.Ordinal),
    "Run logging game-data reader should read the authoritative run hour from Data.Run.Hour."
);
Assert(
    !runLoggingReaderSource.Contains(
        "Data.Run.Victories + Data.Run.Losses + 1",
        StringComparison.Ordinal
    ),
    "Run logging game-data reader should not derive hour from victories plus losses."
);

var historyPanelRepositorySourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Game/HistoryPanel/HistoryPanelRepository.cs"
    )
);
Assert(
    File.Exists(historyPanelRepositorySourcePath),
    $"History panel repository source not found at {historyPanelRepositorySourcePath}"
);
var historyPanelRepositorySource = File.ReadAllText(historyPanelRepositorySourcePath);
Assert(
    !historyPanelRepositorySource.Contains("const string marker = \"\\\"instance_id\\\"\";", StringComparison.Ordinal),
    "History panel snapshot summary should not count raw instance_id markers in JSON text."
);
Assert(
    historyPanelRepositorySource.Contains(
            "BuildSnapshotSummary(",
            StringComparison.Ordinal
        )
        && historyPanelRepositorySource.Contains(
            "PvpBattleCardSetCapture playerHand",
            StringComparison.Ordinal
        )
        && historyPanelRepositorySource.Contains(
            "BuildPreviewData(playerHand, playerSkills, opponentHand, opponentSkills)",
            StringComparison.Ordinal
        ),
    "History panel snapshot summary should operate on parsed structured capture payloads."
);
Assert(
    historyPanelRepositorySource.Contains(
        "Skipping unreadable battle history row",
        StringComparison.Ordinal
    ),
    "History panel repository should skip malformed battle rows instead of failing the whole query."
);

var sqliteStoreSourcePath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Game/RunLogging/Persistence/SqliteRunLogStore.cs"
    )
);
Assert(
    File.Exists(sqliteStoreSourcePath),
    $"Sqlite store source not found at {sqliteStoreSourcePath}"
);
var sqliteStoreSource = File.ReadAllText(sqliteStoreSourcePath);
Assert(
    sqliteStoreSource.Contains(
        "var payloadJson = JsonConvert.SerializeObject",
        StringComparison.Ordinal
    ),
    "SqliteRunLogStore should serialize event payloads before opening the database connection."
);
Assert(
    sqliteStoreSource.Contains("command.CommandTimeout = 2;", StringComparison.Ordinal),
    "SqliteRunLogStore should apply a short command timeout to sqlite operations."
);
Assert(
    sqliteStoreSource.Contains("connection.Dispose();", StringComparison.Ordinal),
    "SqliteRunLogStore should dispose connections when OpenConnection initialization fails."
);
Assert(
    sqliteStoreSource.Contains("pending_selection_json", StringComparison.Ordinal),
    "SqliteRunLogStore should persist pending selection payloads for recovery."
);

var csprojSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../BazaarPlusPlus.csproj")
);
Assert(File.Exists(csprojSourcePath), $"Project file not found at {csprojSourcePath}");
var csprojSource = File.ReadAllText(csprojSourcePath);
Assert(
    csprojSource.Contains(
        "FilesToDelete Include=\"$(GamePath)\\BepInEx\\plugins\\e_sqlite3.dll\"",
        StringComparison.Ordinal
    ),
    "Debug build should delete stale Windows sqlite native runtime files before copy."
);
Assert(
    csprojSource.Contains("WindowsSqliteNativeFile Include=", StringComparison.Ordinal),
    "Debug build should declare the Windows sqlite native runtime file."
);
Assert(
    csprojSource.Contains(
        "DestinationFiles=\"$(GamePath)\\BepInEx\\plugins\\e_sqlite3.dll\"",
        StringComparison.Ordinal
    ),
    "Debug build should copy e_sqlite3.dll into the BepInEx plugins folder."
);
Assert(
    csprojSource.Contains(
        "<MacSqliteRuntimeRid>osx-x64</MacSqliteRuntimeRid>",
        StringComparison.Ordinal
    ),
    "Project file should pin the macOS sqlite runtime to osx-x64 for Rosetta-based macOS support."
);
Assert(
    !csprojSource.Contains("Apple Silicon", StringComparison.Ordinal),
    "Project file should not reject Apple Silicon hosts that run the x64 game through Rosetta."
);

AssertSourceMissing("Game/RunLogging/Persistence/JsonRunLogStore.cs");
AssertSourceMissing("Game/RunLogging/Json/RunLogJsonSchema.cs");
AssertSourceMissing("Game/RunLogging/Json/RunLogPathLayout.cs");
AssertSourceMissing("tests/RunLoggingJsonSchema.Tests/RunLoggingJsonSchema.Tests.csproj");
AssertSourceMissing("tests/RunLoggingJsonSchema.Tests/Program.cs");
AssertSourceMissing("tests/RunLoggingPathLayout.Tests/RunLoggingPathLayout.Tests.csproj");
AssertSourceMissing("tests/RunLoggingPathLayout.Tests/Program.cs");
AssertSourceMissing("tests/RunLoggingJsonStore.Tests/RunLoggingJsonStore.Tests.csproj");
AssertSourceMissing("tests/RunLoggingJsonStore.Tests/Program.cs");
AssertSourceMissing("tests/RunLoggingRecovery.Tests/RunLoggingRecovery.Tests.csproj");
AssertSourceMissing("tests/RunLoggingRecovery.Tests/Program.cs");

Console.WriteLine("RunLogging model contract checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void RequireMethod(Type type, string name)
{
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");
}

static void RequireProperty(Type type, string name)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertSourceMissing(string relativePath)
{
    var fullPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath)
    );
    if (File.Exists(fullPath))
        throw new InvalidOperationException($"File should have been deleted: {relativePath}");
}
