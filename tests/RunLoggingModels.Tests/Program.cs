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
var completionType = RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogCompletion");
RequireProperty(eventType, "SchemaVersion");
RequireProperty(eventType, "RunId");
RequireProperty(eventType, "Seq");
RequireProperty(eventType, "Ts");
RequireProperty(eventType, "Kind");
RequireProperty(eventType, "SelectedEncounterId");
RequireProperty(eventType, "AbandonedReason");
RequireProperty(completionType, "FinalPlayerRank");
RequireProperty(completionType, "FinalPlayerRating");
RequireProperty(completionType, "FinalPlayerRatingDelta");

var gameDataReaderType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLoggingGameDataReader");
var formatPlayerRank = gameDataReaderType.GetMethod(
    "FormatPlayerRank",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    formatPlayerRank != null,
    "RunLoggingGameDataReader should expose a private static rank formatter."
);
var seasonRankType = RequireExternalType("TheBazaar.ProfileData.SeasonRank", "TheBazaarRuntime");
var rankEnumType = RequireExternalType("BazaarGameShared.TempoNet.Enums.ERank", "BazaarGameShared");
var seasonRank =
    Activator.CreateInstance(seasonRankType)
    ?? throw new InvalidOperationException("SeasonRank should be constructible.");
seasonRankType
    .GetMethod("SetRankData", [typeof(int), rankEnumType, typeof(int), typeof(int), typeof(int)])!
    .Invoke(seasonRank, [1, Enum.Parse(rankEnumType, "Gold"), 2, 0, 1420]);
Assert(
    string.Equals(
        (string?)formatPlayerRank!.Invoke(null, [seasonRank]),
        "Gold",
        StringComparison.Ordinal
    ),
    "Player rank formatting should keep only the tier and ignore division."
);

Console.WriteLine("RunLogging model contract checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static Type RequireExternalType(string fullName, string assemblyName)
{
    return Type.GetType($"{fullName}, {assemblyName}")
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
