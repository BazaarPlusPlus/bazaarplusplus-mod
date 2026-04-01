#nullable enable
using System.Reflection;

var contextType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiBootstrapContext");
var tryCreateMethod = contextType.GetMethod(
    "TryCreate",
    BindingFlags.Public | BindingFlags.Static
);
Assert(
    tryCreateMethod != null,
    "ModApiBootstrapContext should expose a static TryCreate factory."
);

var validContext = tryCreateMethod!.Invoke(
    null,
    [
        "/tmp/run-logs.db",
        "/tmp/CombatReplays",
        "/tmp/install-id.txt",
        "/tmp/client.json",
        "/tmp/key.json",
        "https://example.com",
    ]
);
Assert(validContext != null, "TryCreate should accept valid path and API base inputs.");
Assert(
    string.Equals(
        (string?)contextType.GetProperty("DatabasePath")!.GetValue(validContext),
        "/tmp/run-logs.db",
        StringComparison.Ordinal
    ),
    "Bootstrap context should retain the run log database path."
);
Assert(
    string.Equals(
        (string?)contextType.GetProperty("ReplayRootPath")!.GetValue(validContext),
        "/tmp/CombatReplays",
        StringComparison.Ordinal
    ),
    "Bootstrap context should retain the optional replay root path."
);
var routes = contextType.GetProperty("Routes")!.GetValue(validContext);
Assert(routes != null, "Bootstrap context should materialize shared ModApi routes.");
var routesType = routes!.GetType();
Assert(
    string.Equals(
        (string?)routesType.GetProperty("RegisterClient")!.GetValue(routes),
        "https://example.com/clients/register",
        StringComparison.Ordinal
    ),
    "Bootstrap context should build the register route from the API base URL."
);
Assert(
    string.Equals(
        (string?)routesType.GetProperty("UploadRunSummary")!.GetValue(routes),
        "https://example.com/runs",
        StringComparison.Ordinal
    ),
    "Bootstrap context should build the run-summary upload route from the API base URL."
);
var createIdentityStore = contextType.GetMethod("CreateIdentityStore");
var createClientStateStore = contextType.GetMethod("CreateClientStateStore");
var createKeyStore = contextType.GetMethod("CreateKeyStore");
Assert(createIdentityStore != null, "Bootstrap context should create a shared identity store.");
Assert(
    createClientStateStore != null,
    "Bootstrap context should create a shared client state store."
);
Assert(createKeyStore != null, "Bootstrap context should create a shared key store.");
Assert(
    string.Equals(
        createIdentityStore!.ReturnType.FullName,
        "BazaarPlusPlus.Game.ModApi.ModApiIdentityStore",
        StringComparison.Ordinal
    ),
    "Bootstrap context should return ModApiIdentityStore as the primary identity abstraction."
);
Assert(
    string.Equals(
        createClientStateStore!.ReturnType.FullName,
        "BazaarPlusPlus.Game.ModApi.ModApiClientStateStore",
        StringComparison.Ordinal
    ),
    "Bootstrap context should return ModApiClientStateStore as the primary client-state abstraction."
);
Assert(
    string.Equals(
        createKeyStore!.ReturnType.FullName,
        "BazaarPlusPlus.Game.ModApi.ModApiKeyStore",
        StringComparison.Ordinal
    ),
    "Bootstrap context should return ModApiKeyStore as the primary key abstraction."
);
Assert(
    ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadIdentityStore") == null,
    "RunUploadIdentityStore compatibility wrapper should be removed."
);
Assert(
    ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadClientStateStore") == null,
    "RunUploadClientStateStore compatibility wrapper should be removed."
);
Assert(
    ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadKeyStore") == null,
    "RunUploadKeyStore compatibility wrapper should be removed."
);

var missingCredentialPathContext = tryCreateMethod.Invoke(
    null,
    [
        "/tmp/run-logs.db",
        null,
        "",
        "/tmp/client.json",
        "/tmp/key.json",
        "https://example.com",
    ]
);
Assert(
    missingCredentialPathContext == null,
    "TryCreate should reject missing auth/state file paths."
);

var invalidEndpointContext = tryCreateMethod.Invoke(
    null,
    [
        "/tmp/run-logs.db",
        null,
        "/tmp/install-id.txt",
        "/tmp/client.json",
        "/tmp/key.json",
        "ftp://example.com",
    ]
);
Assert(
    invalidEndpointContext == null,
    "TryCreate should reject non-http(s) API base URLs."
);

Console.WriteLine("RunUpload bootstrap tests passed.");

return;

static Type RequireType(string fullName)
{
    var type = Type.GetType($"{fullName}, BazaarPlusPlus");
    if (type == null)
        throw new InvalidOperationException($"Unable to resolve type '{fullName}'.");

    return type;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static Type? ResolveTypeOrNull(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus");
}
