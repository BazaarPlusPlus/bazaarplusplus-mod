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
