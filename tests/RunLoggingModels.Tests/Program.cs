using System.Reflection;

RequireStorageType("BazaarPlusPlus.Storage.RunLog.RunLogCreateRequest");
RequireStorageType("BazaarPlusPlus.Storage.RunLog.RunLogSessionState");
RequireStorageType("BazaarPlusPlus.Storage.RunLog.RunLogEvent");
RequireStorageType("BazaarPlusPlus.Storage.RunLog.RunLogCheckpoint");
RequireStorageType("BazaarPlusPlus.Storage.RunLog.RunLogCompletion");
RequireStorageType("BazaarPlusPlus.Storage.RunLog.RunLogAbandonment");
RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingGameDataReader");

var storeType = RequireStorageType("BazaarPlusPlus.Storage.RunLog.IRunLogStore");
RequireMethod(storeType, "TryResumeActiveRun");
RequireMethod(storeType, "CreateRun");
RequireMethod(storeType, "AppendEvent");
RequireMethod(storeType, "SaveCheckpoint");
RequireMethod(storeType, "CompleteRun");
RequireMethod(storeType, "MarkRunAbandoned");

var eventType = RequireStorageType("BazaarPlusPlus.Storage.RunLog.RunLogEvent");
var completionType = RequireStorageType("BazaarPlusPlus.Storage.RunLog.RunLogCompletion");
RequireProperty(eventType, "SchemaVersion");
RequireProperty(eventType, "RunId");
RequireProperty(eventType, "Seq");
RequireProperty(eventType, "Ts");
RequireProperty(eventType, "Kind");
RequireProperty(eventType, "AbandonedReason");
RequireProperty(completionType, "FinalPlayerRank");
RequireProperty(completionType, "FinalPlayerRating");
RequireProperty(completionType, "FinalPlayerRatingDelta");

// Verify RunLoggingGameDataReader still lives in the main assembly.
RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingGameDataReader");

Console.WriteLine("RunLogging model contract checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static Type RequireStorageType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus.Storage")
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
