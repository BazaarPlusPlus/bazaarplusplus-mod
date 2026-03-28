#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-run-upload-sync-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);

var dbPath = Path.Combine(tempRoot, "run-logs.db");
var installIdPath = Path.Combine(tempRoot, "install-id.txt");
var clientStatePath = Path.Combine(tempRoot, "client.json");
var privateKeyPath = Path.Combine(tempRoot, "key.json");

try
{
    const string runId = "run-upload-001";
    var startedAt = new DateTimeOffset(2026, 3, 26, 12, 0, 0, TimeSpan.Zero);

    var sqliteStore = new SqliteRunLogStore(dbPath);
    var uploadStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadSqliteStore");
    var uploadStore = Activator.CreateInstance(uploadStoreType, dbPath)
        ?? throw new InvalidOperationException("Failed to create RunUploadSqliteStore.");
    var replicatedStoreType = RequireType(
        "BazaarPlusPlus.Game.RunLogging.Persistence.ReplicatedRunLogStore"
    );
    var replicatedStore = Activator.CreateInstance(replicatedStoreType, sqliteStore, uploadStore)
        ?? throw new InvalidOperationException("Failed to create ReplicatedRunLogStore.");

    Invoke<RunLogSessionState>(
        replicatedStoreType,
        replicatedStore,
        "CreateRun",
        [
            new RunLogCreateRequest
            {
                SchemaVersion = 1,
                RunId = runId,
                StartedAtUtc = startedAt,
                Hero = "Vanessa",
                GameMode = "Ranked",
                Day = 1,
                Hour = 1,
            },
        ]
    );
    InvokeVoid(
        replicatedStoreType,
        replicatedStore,
        "AppendEvent",
        [
            runId,
            new RunLogEvent
            {
                SchemaVersion = 1,
                RunId = runId,
                Seq = 1,
                Ts = startedAt,
                Kind = "run_started",
                Day = 1,
                Hour = 1,
                Hero = "Vanessa",
                GameMode = "Ranked",
            },
        ]
    );
    InvokeVoid(
        replicatedStoreType,
        replicatedStore,
        "SaveCheckpoint",
        [
            runId,
            new RunLogCheckpoint
            {
                SchemaVersion = 1,
                RunId = runId,
                LastSeq = 1,
                LastSeenAtUtc = startedAt,
                Day = 1,
                Hour = 1,
                State = "Encounter",
                Completed = false,
            },
        ]
    );
    InvokeVoid(
        replicatedStoreType,
        replicatedStore,
        "CompleteRun",
        [
            runId,
            new RunLogCompletion
            {
                SchemaVersion = 1,
                RunId = runId,
                Status = "completed",
                EndedAtUtc = startedAt.AddMinutes(10),
                FinalDay = 3,
                FinalHour = 1,
            },
        ]
    );

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetInt64(connection, "SELECT dirty FROM run_sync_state WHERE run_id = $runId;", runId)
                == 1,
            "ReplicatedRunLogStore should mark completed runs as dirty for upload."
        );
    }

    var pendingRunIds = (IReadOnlyList<string>)Invoke<object>(
        uploadStoreType,
        uploadStore,
        "GetPendingCompletedRunIds",
        [2]
    );
    Assert(
        pendingRunIds.Count == 1 && pendingRunIds[0] == runId,
        "RunUploadSqliteStore should return dirty completed runs."
    );

    var snapshot = Invoke<object>(
        uploadStoreType,
        uploadStore,
        "TryBuildSnapshot",
        [runId, "install-123", null]
    );
    Assert(snapshot != null, "RunUploadSqliteStore should build an upload snapshot for a completed run.");
    var payload = snapshot!.GetType().GetProperty("Payload")!.GetValue(snapshot)
        ?? throw new InvalidOperationException("Snapshot payload should be populated.");
    var payloadType = payload.GetType();
    Assert(
        (string)payloadType.GetProperty("InstallId")!.GetValue(payload)! == "install-123",
        "Upload payload should include the install id."
    );
    var events = (JArray)payloadType.GetProperty("Events")!.GetValue(payload)!;
    Assert(events.Count == 1, "Upload payload should include persisted run events.");
    var status = (JObject?)payloadType.GetProperty("Status")!.GetValue(payload);
    Assert(
        status?["status"]?.Value<string>() == "completed",
        "Upload payload should include the terminal run status."
    );

    InvokeVoid(
        uploadStoreType,
        uploadStore,
        "MarkRunUploaded",
        [runId, 1L, "completed", startedAt.AddMinutes(12)]
    );
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetInt64(connection, "SELECT dirty FROM run_sync_state WHERE run_id = $runId;", runId)
                == 0,
            "MarkRunUploaded should clear the dirty sync flag."
        );
        Assert(
            GetString(
                connection,
                "SELECT uploaded_status FROM run_sync_state WHERE run_id = $runId;",
                runId
            ) == "completed",
            "MarkRunUploaded should persist the uploaded terminal status."
        );
    }

    var identityStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadIdentityStore");
    var identityStore = Activator.CreateInstance(identityStoreType, installIdPath)
        ?? throw new InvalidOperationException("Failed to create RunUploadIdentityStore.");
    var installId1 = (string)Invoke<object>(
        identityStoreType,
        identityStore,
        "GetOrCreateInstallId",
        []
    );
    var installId2 = (string)Invoke<object>(
        identityStoreType,
        identityStore,
        "GetOrCreateInstallId",
        []
    );
    Assert(
        !string.IsNullOrWhiteSpace(installId1) && installId1 == installId2,
        "RunUploadIdentityStore should persist a stable install id."
    );
    Assert(
        File.Exists(installIdPath) && File.ReadAllText(installIdPath).Trim() == installId1,
        "RunUploadIdentityStore should write the install id to disk."
    );

    var clientStateStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadClientStateStore");
    var clientStateStore = Activator.CreateInstance(clientStateStoreType, clientStatePath)
        ?? throw new InvalidOperationException("Failed to create RunUploadClientStateStore.");
    var routeKindType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadRouteKind");
    var globalRouteKind = Enum.Parse(routeKindType, "Global");
    Assert(
        Invoke<object?>(clientStateStoreType, clientStateStore, "TryGetClientId", [globalRouteKind]) == null,
        "Client state store should return null before registration."
    );
    InvokeVoid(clientStateStoreType, clientStateStore, "SaveClientId", [globalRouteKind, "client-abc"]);
    Assert(
        (string)Invoke<object>(clientStateStoreType, clientStateStore, "TryGetClientId", [globalRouteKind]) == "client-abc",
        "Client state store should persist client id."
    );
    InvokeVoid(clientStateStoreType, clientStateStore, "ClearClientId", [globalRouteKind]);
    Assert(
        Invoke<object?>(clientStateStoreType, clientStateStore, "TryGetClientId", [globalRouteKind]) == null,
        "Client state store should clear a route-scoped client id."
    );

    File.WriteAllText(clientStatePath, "{ not-valid-json");
    clientStateStore = Activator.CreateInstance(clientStateStoreType, clientStatePath)
        ?? throw new InvalidOperationException("Failed to recreate RunUploadClientStateStore.");
    Assert(
        Invoke<object?>(clientStateStoreType, clientStateStore, "TryGetClientId", [globalRouteKind]) == null,
        "Client state store should recover from corrupted JSON by treating it as empty state."
    );

    var routeStateStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadRouteStateStore");
    var routeStatePath = Path.Combine(tempRoot, "route.json");
    var routeStateStore = Activator.CreateInstance(routeStateStoreType, routeStatePath)
        ?? throw new InvalidOperationException("Failed to create RunUploadRouteStateStore.");
    var routeSelectorType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadRouteSelector");
    var modeType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadMode");
    var autoMode = Enum.Parse(modeType, "Auto");
    var routeSelector = Activator.CreateInstance(
        routeSelectorType,
        autoMode,
        routeStateStore,
        2,
        TimeSpan.FromMinutes(60)
    ) ?? throw new InvalidOperationException("Failed to create RunUploadRouteSelector.");
    var endpointSetType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadEndpointSet");
    var globalEndpoint = Activator.CreateInstance(endpointSetType)!;
    endpointSetType.GetProperty("RouteKind")!.SetValue(globalEndpoint, globalRouteKind);
    endpointSetType.GetProperty("RegistrationEndpoint")!.SetValue(globalEndpoint, "https://global.example/register");
    endpointSetType.GetProperty("UploadEndpoint")!.SetValue(globalEndpoint, "https://global.example/upload");
    var cnRouteKind = Enum.Parse(routeKindType, "CN");
    var cnEndpoint = Activator.CreateInstance(endpointSetType)!;
    endpointSetType.GetProperty("RouteKind")!.SetValue(cnEndpoint, cnRouteKind);
    endpointSetType.GetProperty("RegistrationEndpoint")!.SetValue(cnEndpoint, "https://cn.example/register");
    endpointSetType.GetProperty("UploadEndpoint")!.SetValue(cnEndpoint, "https://cn.example/upload");
    var routeOrder = (System.Collections.IEnumerable)Invoke<object>(
        routeSelectorType,
        routeSelector,
        "GetRouteOrder",
        [globalEndpoint, cnEndpoint]
    );
    var firstRoute = routeOrder.Cast<object>().First();
    Assert(
        endpointSetType.GetProperty("RouteKind")!.GetValue(firstRoute)!.ToString() == "Global",
        "Auto mode should try Global before CN."
    );
    InvokeVoid(routeSelectorType, routeSelector, "RecordRouteFailure", [globalRouteKind]);
    InvokeVoid(routeSelectorType, routeSelector, "RecordRouteFailure", [globalRouteKind]);
    routeOrder = (System.Collections.IEnumerable)Invoke<object>(
        routeSelectorType,
        routeSelector,
        "GetRouteOrder",
        [globalEndpoint, cnEndpoint]
    );
    firstRoute = routeOrder.Cast<object>().First();
    Assert(
        endpointSetType.GetProperty("RouteKind")!.GetValue(firstRoute)!.ToString() == "CN",
        "Auto mode should fall back to CN after repeated Global failures."
    );

    File.WriteAllText(routeStatePath, "{ not-valid-json");
    routeStateStore = Activator.CreateInstance(routeStateStoreType, routeStatePath)
        ?? throw new InvalidOperationException("Failed to recreate RunUploadRouteStateStore.");
    routeSelector = Activator.CreateInstance(
        routeSelectorType,
        autoMode,
        routeStateStore,
        2,
        TimeSpan.FromMinutes(60)
    ) ?? throw new InvalidOperationException("Failed to recreate RunUploadRouteSelector.");
    routeOrder = (System.Collections.IEnumerable)Invoke<object>(
        routeSelectorType,
        routeSelector,
        "GetRouteOrder",
        [globalEndpoint, cnEndpoint]
    );
    firstRoute = routeOrder.Cast<object>().First();
    Assert(
        endpointSetType.GetProperty("RouteKind")!.GetValue(firstRoute)!.ToString() == "Global",
        "Route selector should recover from corrupted route state by reverting to default ordering."
    );

    var keyStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadKeyStore");
    var keyStore = Activator.CreateInstance(keyStoreType, privateKeyPath)
        ?? throw new InvalidOperationException("Failed to create RunUploadKeyStore.");
    var keyMaterial1 = Invoke<object>(keyStoreType, keyStore, "GetOrCreateKeyMaterial", []);
    var keyMaterial2 = Invoke<object>(keyStoreType, keyStore, "GetOrCreateKeyMaterial", []);
    var fingerprintProperty = keyMaterial1.GetType().GetProperty("Fingerprint")
        ?? throw new InvalidOperationException("Key material should expose fingerprint.");
    Assert(
        (string)fingerprintProperty.GetValue(keyMaterial1)! == (string)fingerprintProperty.GetValue(keyMaterial2)!,
        "Key store should persist a stable RSA keypair."
    );
    var signature = (string)Invoke<object>(keyStoreType, keyStore, "Sign", ["hello"]);
    Assert(
        !string.IsNullOrWhiteSpace(signature) && File.Exists(privateKeyPath),
        "Key store should sign payloads and persist the local private key."
    );

    File.WriteAllText(privateKeyPath, "{ not-valid-json");
    keyStore = Activator.CreateInstance(keyStoreType, privateKeyPath)
        ?? throw new InvalidOperationException("Failed to recreate RunUploadKeyStore.");
    var recoveredKeyMaterial = Invoke<object>(keyStoreType, keyStore, "GetOrCreateKeyMaterial", []);
    var recoveredFingerprint = (string)fingerprintProperty.GetValue(recoveredKeyMaterial)!;
    var recoveredSignature = (string)Invoke<object>(keyStoreType, keyStore, "Sign", ["hello-again"]);
    Assert(
        !string.IsNullOrWhiteSpace(recoveredFingerprint) && !string.IsNullOrWhiteSpace(recoveredSignature),
        "Key store should recover from corrupted key material by minting a replacement keypair."
    );
    Assert(
        Directory.GetFiles(tempRoot, "key.json.corrupt-*").Length == 1,
        "Key store should preserve the corrupted key payload for inspection before regenerating."
    );

    File.WriteAllText(
        privateKeyPath,
        """
        {
          "algorithm": "rsa-pkcs1-sha256",
          "modulus_b64": "not-base64",
          "exponent_b64": "AQAB",
          "d_b64": "AQAB",
          "p_b64": "AQAB",
          "q_b64": "AQAB",
          "dp_b64": "AQAB",
          "dq_b64": "AQAB",
          "inverse_q_b64": "AQAB",
          "fingerprint": "broken"
        }
        """
    );
    keyStore = Activator.CreateInstance(keyStoreType, privateKeyPath)
        ?? throw new InvalidOperationException("Failed to recreate RunUploadKeyStore.");
    var recoveredFromMalformedJsonKey = Invoke<object>(
        keyStoreType,
        keyStore,
        "GetOrCreateKeyMaterial",
        []
    );
    var recoveredFromMalformedJsonSignature = (string)Invoke<object>(
        keyStoreType,
        keyStore,
        "Sign",
        ["hello-after-valid-json-corruption"]
    );
    Assert(
        !string.IsNullOrWhiteSpace(
            (string)fingerprintProperty.GetValue(recoveredFromMalformedJsonKey)!
        ) && !string.IsNullOrWhiteSpace(recoveredFromMalformedJsonSignature),
        "Key store should recover when persisted key JSON is syntactically valid but contains invalid RSA material."
    );
    Assert(
        Directory.GetFiles(tempRoot, "key.json.corrupt-*").Length == 2,
        "Key store should also preserve valid-JSON corrupted key payloads before regenerating."
    );
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("Run upload sync checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static T Invoke<T>(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    return (T)method.Invoke(instance, args)!;
}

static void InvokeVoid(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(
        name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    method.Invoke(instance, args);
}

static long GetInt64(SqliteConnection connection, string sql, string runId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$runId", runId);
    return (long)(
        command.ExecuteScalar()
        ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
}

static string GetString(SqliteConnection connection, string sql, string runId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$runId", runId);
    return (string)(
        command.ExecuteScalar()
        ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
