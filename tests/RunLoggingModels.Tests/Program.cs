using System.Reflection;

RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogCreateRequest");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogSessionState");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogEvent");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogCheckpoint");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogCompletion");
RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogAbandonment");

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
    !controllerSource.Contains("JsonRunLogStore", StringComparison.Ordinal),
    "RunLoggingController should no longer reference JsonRunLogStore."
);

var pluginSourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Plugin.cs"));
Assert(File.Exists(pluginSourcePath), $"Plugin source not found at {pluginSourcePath}");
var pluginSource = File.ReadAllText(pluginSourcePath);
Assert(
    pluginSource.Contains("gameObject.AddComponent<RunLoggingController>();", StringComparison.Ordinal),
    "Plugin.Awake should mount RunLoggingController."
);

var modStateSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../Models/ModState.cs")
);
Assert(File.Exists(modStateSourcePath), $"ModState source not found at {modStateSourcePath}");
var modStateSource = File.ReadAllText(modStateSourcePath);
Assert(
    modStateSource.Contains("RunLogDatabasePath", StringComparison.Ordinal),
    "ModState should expose a SQLite database path."
);
Assert(
    !modStateSource.Contains("RunLogRootPath", StringComparison.Ordinal),
    "ModState should no longer expose a JSON run log root path."
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
    var fullPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));
    if (File.Exists(fullPath))
        throw new InvalidOperationException($"File should have been deleted: {relativePath}");
}
