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
object? queuedStore = null;

try
{
    const string runId = "run-upload-001";
    var startedAt = new DateTimeOffset(2026, 3, 26, 12, 0, 0, TimeSpan.Zero);

    var sqliteStore = new SqliteRunLogStore(dbPath);
    var uploadStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadSqliteStore");
    var uploadStore =
        Activator.CreateInstance(uploadStoreType, dbPath)
        ?? throw new InvalidOperationException("Failed to create RunUploadSqliteStore.");
    var replicatedStoreType = RequireType(
        "BazaarPlusPlus.Game.RunLogging.Persistence.ReplicatedRunLogStore"
    );
    var replicatedStore =
        Activator.CreateInstance(replicatedStoreType, sqliteStore, uploadStore)
        ?? throw new InvalidOperationException("Failed to create ReplicatedRunLogStore.");
    var queuedStoreType = RequireType(
        "BazaarPlusPlus.Game.RunLogging.Persistence.QueuedRunLogStore"
    );
    queuedStore =
        Activator.CreateInstance(queuedStoreType, replicatedStore)
        ?? throw new InvalidOperationException("Failed to create QueuedRunLogStore.");

    Invoke<RunLogSessionState>(
        queuedStoreType,
        queuedStore,
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
        queuedStoreType,
        queuedStore,
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
        queuedStoreType,
        queuedStore,
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
                Completed = false,
            },
        ]
    );
    InvokeVoid(
        queuedStoreType,
        queuedStore,
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
                FinalPlayerRank = "Legendary",
                FinalPlayerRating = 1436,
            },
        ]
    );

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetInt64(connection, "SELECT COUNT(*) FROM run_events WHERE run_id = $runId;", runId)
                == 1,
            "QueuedRunLogStore should flush pending run events before CompleteRun returns."
        );
        Assert(
            GetInt64(
                connection,
                "SELECT last_seq FROM runs WHERE run_id = $runId;",
                runId
            ) == 1,
            "QueuedRunLogStore should flush the inline run checkpoint state before CompleteRun returns."
        );
        Assert(
            GetInt64(connection, "SELECT dirty FROM run_sync_state WHERE run_id = $runId;", runId)
                == 1,
            "QueuedRunLogStore should mark completed runs as dirty for upload before returning."
        );
    }

    var pendingRunIds =
        (IReadOnlyList<string>)
            Invoke<object>(uploadStoreType, uploadStore, "GetPendingCompletedRunIds", [2]);
    Assert(
        pendingRunIds.Count == 1 && pendingRunIds[0] == runId,
        "RunUploadSqliteStore should return dirty completed runs."
    );

    var snapshot = Invoke<object>(
        uploadStoreType,
        uploadStore,
        "TryBuildRunSummarySnapshot",
        [runId, "install-123", null]
    );
    Assert(
        snapshot != null,
        "RunUploadSqliteStore should build an upload snapshot for a completed run."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunSummaryUploadPayload") != null,
        "RunSummaryUploadPayload should exist as the primary run upload payload type."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunSummaryUploadSnapshot") != null,
        "RunSummaryUploadSnapshot should exist as the primary run upload snapshot type."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunSummaryUploadCycleResult") != null,
        "RunSummaryUploadCycleResult should exist as the primary run upload cycle result type."
    );
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadPayload") == null,
        "RunUploadPayload compatibility wrapper should be removed."
    );
    var payload =
        snapshot!.GetType().GetProperty("Payload")!.GetValue(snapshot)
        ?? throw new InvalidOperationException("Snapshot payload should be populated.");
    var payloadType = payload.GetType();
    Assert(
        (string)payloadType.GetProperty("InstallId")!.GetValue(payload)! == "install-123",
        "Upload payload should include the install id."
    );
    Assert(
        payloadType.GetProperty("Events") == null && payloadType.GetProperty("PvpBattles") == null,
        "Upload payload should not expose legacy events or PVP battle arrays."
    );
    Assert(
        payloadType.GetProperty("Meta") == null && payloadType.GetProperty("Checkpoint") == null,
        "Upload payload should not expose nested meta/checkpoint wrappers."
    );
    Assert(
        (string?)payloadType.GetProperty("Status")!.GetValue(payload) == "completed",
        "Upload payload should include the terminal run status as a top-level field."
    );
    Assert(
        (string?)payloadType.GetProperty("HeroName")!.GetValue(payload) == "Vanessa",
        "Upload payload should include the hero name as a top-level field."
    );
    Assert(
        (int?)payloadType.GetProperty("FinalDay")!.GetValue(payload) == 3,
        "Upload payload should include the final day as a top-level field."
    );
    Assert(
        (string?)payloadType.GetProperty("EndedAtUtc")!.GetValue(payload)
            == startedAt.AddMinutes(10).ToString("o"),
        "Upload payload should include the end timestamp as a top-level field."
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

    var identityStoreType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiIdentityStore");
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadIdentityStore") == null,
        "RunUploadIdentityStore compatibility wrapper should be removed."
    );
    var identityStore =
        Activator.CreateInstance(identityStoreType, installIdPath)
        ?? throw new InvalidOperationException("Failed to create ModApiIdentityStore.");
    var installId1 = (string)
        Invoke<object>(identityStoreType, identityStore, "GetOrCreateInstallId", []);
    var installId2 = (string)
        Invoke<object>(identityStoreType, identityStore, "GetOrCreateInstallId", []);
    Assert(
        !string.IsNullOrWhiteSpace(installId1) && installId1 == installId2,
        "ModApiIdentityStore should persist a stable install id."
    );
    Assert(
        File.Exists(installIdPath) && File.ReadAllText(installIdPath).Trim() == installId1,
        "ModApiIdentityStore should write the install id to disk."
    );

    var clientStateStoreType = RequireType(
        "BazaarPlusPlus.Game.ModApi.ModApiClientStateStore"
    );
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadClientStateStore")
            == null,
        "RunUploadClientStateStore compatibility wrapper should be removed."
    );
    var clientStateStore =
        Activator.CreateInstance(clientStateStoreType, clientStatePath)
        ?? throw new InvalidOperationException("Failed to create ModApiClientStateStore.");
    Assert(
        Invoke<object?>(clientStateStoreType, clientStateStore, "TryGetClientId", []) == null,
        "Client state store should return null before registration."
    );
    InvokeVoid(clientStateStoreType, clientStateStore, "SaveClientId", ["client-abc"]);
    Assert(
        (string)Invoke<object>(clientStateStoreType, clientStateStore, "TryGetClientId", [])
            == "client-abc",
        "Client state store should persist client id."
    );
    Assert(
        Invoke<object?>(clientStateStoreType, clientStateStore, "TryGetBoundPlayerAccountId", [])
            == null,
        "Client state store should return null before a bound player account is saved."
    );
    InvokeVoid(
        clientStateStoreType,
        clientStateStore,
        "SaveBoundPlayerAccountId",
        ["player-account-001"]
    );
    Assert(
        (string)
            Invoke<object>(
                clientStateStoreType,
                clientStateStore,
                "TryGetBoundPlayerAccountId",
                []
            ) == "player-account-001",
        "Client state store should persist the bound player account."
    );
    clientStateStore =
        Activator.CreateInstance(clientStateStoreType, clientStatePath)
        ?? throw new InvalidOperationException("Failed to recreate ModApiClientStateStore.");
    Assert(
        (string)
            Invoke<object>(
                clientStateStoreType,
                clientStateStore,
                "TryGetBoundPlayerAccountId",
                []
            ) == "player-account-001",
        "Client state store should persist the bound player account across restarts."
    );
    InvokeVoid(clientStateStoreType, clientStateStore, "ClearClientId", []);
    Assert(
        Invoke<object?>(clientStateStoreType, clientStateStore, "TryGetClientId", []) == null,
        "Client state store should clear the persisted client id."
    );

    File.WriteAllText(clientStatePath, "{ not-valid-json");
    clientStateStore =
        Activator.CreateInstance(clientStateStoreType, clientStatePath)
        ?? throw new InvalidOperationException("Failed to recreate ModApiClientStateStore.");
    Assert(
        Invoke<object?>(clientStateStoreType, clientStateStore, "TryGetClientId", []) == null,
        "Client state store should recover from corrupted JSON by treating it as empty state."
    );

    var keyStoreType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiKeyStore");
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadKeyStore") == null,
        "RunUploadKeyStore compatibility wrapper should be removed."
    );
    var keyStore =
        Activator.CreateInstance(keyStoreType, privateKeyPath)
        ?? throw new InvalidOperationException("Failed to create ModApiKeyStore.");
    var keyMaterial1 = Invoke<object>(keyStoreType, keyStore, "GetOrCreateKeyMaterial", []);
    var keyMaterial2 = Invoke<object>(keyStoreType, keyStore, "GetOrCreateKeyMaterial", []);
    var fingerprintProperty =
        keyMaterial1.GetType().GetProperty("Fingerprint")
        ?? throw new InvalidOperationException("Key material should expose fingerprint.");
    Assert(
        (string)fingerprintProperty.GetValue(keyMaterial1)!
            == (string)fingerprintProperty.GetValue(keyMaterial2)!,
        "Key store should persist a stable RSA keypair."
    );
    var signature = (string)Invoke<object>(keyStoreType, keyStore, "Sign", ["hello"]);
    Assert(
        !string.IsNullOrWhiteSpace(signature) && File.Exists(privateKeyPath),
        "Key store should sign payloads and persist the local private key."
    );

    File.WriteAllText(privateKeyPath, "{ not-valid-json");
    keyStore =
        Activator.CreateInstance(keyStoreType, privateKeyPath)
        ?? throw new InvalidOperationException("Failed to recreate ModApiKeyStore.");
    var recoveredKeyMaterial = Invoke<object>(keyStoreType, keyStore, "GetOrCreateKeyMaterial", []);
    var recoveredFingerprint = (string)fingerprintProperty.GetValue(recoveredKeyMaterial)!;
    var recoveredSignature = (string)
        Invoke<object>(keyStoreType, keyStore, "Sign", ["hello-again"]);
    Assert(
        !string.IsNullOrWhiteSpace(recoveredFingerprint)
            && !string.IsNullOrWhiteSpace(recoveredSignature),
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
    keyStore =
        Activator.CreateInstance(keyStoreType, privateKeyPath)
        ?? throw new InvalidOperationException("Failed to recreate ModApiKeyStore.");
    var recoveredFromMalformedJsonKey = Invoke<object>(
        keyStoreType,
        keyStore,
        "GetOrCreateKeyMaterial",
        []
    );
    var recoveredFromMalformedJsonSignature = (string)
        Invoke<object>(keyStoreType, keyStore, "Sign", ["hello-after-valid-json-corruption"]);
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

    var startupGateType = RequireType(
        "BazaarPlusPlus.Game.Upload.StartupUploadAttemptGate"
    );
    var gate = Activator.CreateInstance(startupGateType, 5f, 10f)
        ?? throw new InvalidOperationException("Failed to create StartupUploadAttemptGate.");
    var waitDecision = Invoke<object>(startupGateType, gate, "Poll", [4f, false]).ToString();
    Assert(
        waitDecision == "Wait",
        "Startup upload gate should wait until the startup delay elapses."
    );
    var firstStartDecision = Invoke<object>(startupGateType, gate, "Poll", [5f, false]).ToString();
    Assert(
        firstStartDecision == "Start",
        "Startup upload gate should trigger a single upload attempt once the startup delay elapses."
    );
    var secondStartDecision = Invoke<object>(startupGateType, gate, "Poll", [14f, false]).ToString();
    Assert(
        secondStartDecision == "Wait",
        "Startup upload gate should wait until the retry interval elapses."
    );
    var thirdStartDecision = Invoke<object>(startupGateType, gate, "Poll", [15f, false]).ToString();
    Assert(
        thirdStartDecision == "Start",
        "Startup upload gate should schedule repeated uploads after the retry interval elapses."
    );

    gate = Activator.CreateInstance(startupGateType, 5f, 10f)
        ?? throw new InvalidOperationException("Failed to recreate StartupUploadAttemptGate.");
    var skippedDecision = Invoke<object>(startupGateType, gate, "Poll", [5f, true]).ToString();
    Assert(
        skippedDecision == "SkipLiveRun",
        "Startup upload gate should skip the startup upload when a live run is active."
    );
    var afterSkipDecision = Invoke<object>(startupGateType, gate, "Poll", [30f, false]).ToString();
    Assert(
        afterSkipDecision == "Start",
        "Startup upload gate should retry after the live run ends."
    );
}
finally
{
    if (queuedStore is IDisposable disposable)
        disposable.Dispose();
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

static Type? ResolveTypeOrNull(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus");
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
