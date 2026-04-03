#nullable enable
using System.Reflection;
using Microsoft.Data.Sqlite;

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-combat-replay-upload-sync-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);

var dbPath = Path.Combine(tempRoot, "run-logs.db");
var replayRoot = Path.Combine(tempRoot, "CombatReplays");

try
{
    var runStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Persistence.SqliteRunLogStore");
    var runStore =
        Activator.CreateInstance(runStoreType, dbPath)
        ?? throw new InvalidOperationException("Failed to create SqliteRunLogStore.");
    var createRunRequestType = RequireType(
        "BazaarPlusPlus.Game.RunLogging.Models.RunLogCreateRequest"
    );
    var createRunRequest = Activator.CreateInstance(createRunRequestType)!;
    createRunRequestType.GetProperty("RunId")!.SetValue(createRunRequest, "server-run-001");
    createRunRequestType
        .GetProperty("StartedAtUtc")!
        .SetValue(createRunRequest, new DateTimeOffset(2026, 3, 28, 0, 0, 0, TimeSpan.Zero));
    createRunRequestType.GetProperty("Hero")!.SetValue(createRunRequest, "Vanessa");
    createRunRequestType.GetProperty("GameMode")!.SetValue(createRunRequest, "Ranked");
    createRunRequestType.GetProperty("Status")!.SetValue(createRunRequest, "active");
    InvokeVoid(runStoreType, runStore, "CreateRun", [createRunRequest]);

    var storeType = RequireType("BazaarPlusPlus.Game.CombatReplay.Upload.BattleUploadSqliteStore");
    var serviceType = RequireType(
        "BazaarPlusPlus.Game.CombatReplay.Upload.BattleArtifactUploadService"
    );
    Assert(
        storeType != null && serviceType != null,
        "Battle artifact upload store and service should exist."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.CombatReplay.Upload.BattleArtifactUploadPayload") != null,
        "BattleArtifactUploadPayload should exist as the primary battle upload payload type."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.CombatReplay.Upload.BattleArtifactUploadSnapshot") != null,
        "BattleArtifactUploadSnapshot should exist as the primary battle upload snapshot type."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.CombatReplay.Upload.BattleArtifactUploadCycleResult")
            != null,
        "BattleArtifactUploadCycleResult should exist as the primary battle upload cycle result type."
    );
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.CombatReplay.Upload.BattleUploadService") == null,
        "BattleUploadService compatibility wrapper should be removed."
    );

    var payloadStoreType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayPayloadStore");
    var payloadStore =
        Activator.CreateInstance(payloadStoreType, replayRoot)
        ?? throw new InvalidOperationException("Failed to create CombatReplayPayloadStore.");
    var payloadType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayload");
    var payload = Activator.CreateInstance(payloadType)!;
    payloadType.GetProperty("BattleId")!.SetValue(payload, "battle-upload-001");
    payloadType.GetProperty("Version")!.SetValue(payload, 1);
    payloadType.GetProperty("SpawnMessageBase64")!.SetValue(payload, "spawn");
    payloadType.GetProperty("CombatMessageBase64")!.SetValue(payload, "combat");
    payloadType.GetProperty("DespawnMessageBase64")!.SetValue(payload, "despawn");
    InvokeVoid(payloadStoreType, payloadStore, "Save", [payload]);

    var catalogType = RequireType("BazaarPlusPlus.Game.PvpBattles.Persistence.PvpBattleCatalog");
    var catalog =
        Activator.CreateInstance(catalogType, dbPath)
        ?? throw new InvalidOperationException("Failed to create PvpBattleCatalog.");
    var manifestType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpBattleManifest");
    var manifest = Activator.CreateInstance(manifestType)!;
    manifestType.GetProperty("BattleId")!.SetValue(manifest, "battle-upload-001");
    manifestType.GetProperty("RunId")!.SetValue(manifest, "server-run-001");
    manifestType
        .GetProperty("RecordedAtUtc")!
        .SetValue(manifest, new DateTimeOffset(2026, 3, 28, 1, 0, 0, TimeSpan.Zero));
    manifestType.GetProperty("CombatKind")!.SetValue(manifest, "PVPCombat");
    InvokeVoid(catalogType, catalog, "Save", [manifest]);

    var store =
        Activator.CreateInstance(storeType, dbPath, replayRoot)
        ?? throw new InvalidOperationException("Failed to create BattleUploadSqliteStore.");
    InvokeVoid(storeType, store, "MarkReplayDirty", ["battle-upload-001"]);

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetInt64(
                connection,
                "SELECT replay_dirty FROM battles WHERE battle_id = $battleId;",
                "battle-upload-001"
            ) == 1,
            "MarkReplayDirty should set battles.replay_dirty."
        );
    }

    var pendingBattleIds =
        (IReadOnlyList<string>)Invoke<object>(storeType, store, "GetPendingBattleIds", [2]);
    Assert(
        pendingBattleIds.Count == 1 && pendingBattleIds[0] == "battle-upload-001",
        "BattleUploadSqliteStore should return dirty replay ids."
    );

    var snapshot = Invoke<object>(
        storeType,
        store,
        "TryBuildBattleArtifactSnapshot",
        ["battle-upload-001", "install-123", null]
    );
    Assert(snapshot != null, "BattleUploadSqliteStore should build an upload snapshot.");

    var snapshotType = snapshot!.GetType();
    var payloadEnvelope =
        snapshotType.GetProperty("Payload")!.GetValue(snapshot)
        ?? throw new InvalidOperationException("Replay upload snapshot should expose Payload.");
    var payloadEnvelopeType = payloadEnvelope.GetType();
    Assert(
        (string)payloadEnvelopeType.GetProperty("InstallId")!.GetValue(payloadEnvelope)!
            == "install-123",
        "Replay upload payload should include the install id."
    );
    Assert(
        (string)payloadEnvelopeType.GetProperty("BattleId")!.GetValue(payloadEnvelope)!
            == "battle-upload-001",
        "Replay upload payload should include the battle id."
    );
    Assert(
        (string?)payloadEnvelopeType.GetProperty("RunId")!.GetValue(payloadEnvelope)
            == "server-run-001",
        "Replay upload payload should include the linked run id."
    );
    Assert(
        payloadEnvelopeType.GetProperty("ReplayPayload") != null,
        "Replay upload payload should embed the replay payload body."
    );

    var payloadSha256 = (string)snapshotType.GetProperty("PayloadSha256")!.GetValue(snapshot)!;
    Assert(
        !string.IsNullOrWhiteSpace(payloadSha256),
        "Replay upload snapshot should compute a payload SHA-256."
    );

    InvokeVoid(
        storeType,
        store,
        "MarkReplayUploaded",
        ["battle-upload-001", new DateTimeOffset(2026, 3, 28, 2, 0, 0, TimeSpan.Zero)]
    );

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetInt64(
                connection,
                "SELECT replay_dirty FROM battles WHERE battle_id = $battleId;",
                "battle-upload-001"
            ) == 0,
            "MarkReplayUploaded should clear battles.replay_dirty."
        );
        Assert(
            !ColumnExists(connection, "battles", "payload_sha256")
                && !ColumnExists(connection, "battles", "object_key"),
            "Battle replay sync should not persist upload object metadata."
        );
    }

    payloadStore =
        Activator.CreateInstance(payloadStoreType, replayRoot)
        ?? throw new InvalidOperationException("Failed to recreate CombatReplayPayloadStore.");
    InvokeVoid(payloadStoreType, payloadStore, "Delete", ["battle-upload-001"]);
    InvokeVoid(storeType, store, "MarkReplayDirty", ["battle-upload-001"]);

    var clientStatePath = Path.Combine(tempRoot, "client.json");
    File.WriteAllText(
        clientStatePath,
        """
        {
          "client_id": "client-existing-001"
        }
        """
    );
    var identityStoreType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiIdentityStore");
    var identityStore =
        Activator.CreateInstance(identityStoreType, Path.Combine(tempRoot, "install-id.txt"))
        ?? throw new InvalidOperationException("Failed to create ModApiIdentityStore.");
    var clientStateStoreType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiClientStateStore");
    var clientStateStore =
        Activator.CreateInstance(clientStateStoreType, clientStatePath)
        ?? throw new InvalidOperationException("Failed to create ModApiClientStateStore.");
    InvokeVoid(clientStateStoreType, clientStateStore, "SaveClientId", ["run-client-001"]);
    var keyStoreType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiKeyStore");
    var keyStore =
        Activator.CreateInstance(keyStoreType, Path.Combine(tempRoot, "key.json"))
        ?? throw new InvalidOperationException("Failed to create ModApiKeyStore.");
    var routesType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiRoutes");
    var tryCreateRoutes = routesType.GetMethod(
        "TryCreate",
        BindingFlags.Public | BindingFlags.Static
    );
    Assert(tryCreateRoutes != null, "ModApiRoutes should expose a static TryCreate factory.");
    var routes =
        tryCreateRoutes!.Invoke(null, ["https://cloudflare.example"])
        ?? throw new InvalidOperationException("Failed to create ModApiRoutes.");
    var service =
        Activator.CreateInstance(
            serviceType,
            store,
            identityStore,
            clientStateStore,
            keyStore,
            routes,
            1,
            TimeSpan.FromSeconds(10)
        ) ?? throw new InvalidOperationException("Failed to create BattleArtifactUploadService.");
    var uploadTask = (Task)
        Invoke<object>(
            serviceType,
            service,
            "UploadPendingBattleArtifactsAsync",
            [CancellationToken.None]
        );
    uploadTask.GetAwaiter().GetResult();

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetInt64(
                connection,
                "SELECT replay_dirty FROM battles WHERE battle_id = $battleId;",
                "battle-upload-001"
            ) == 0,
            "Missing local replay payloads should stop retrying instead of staying dirty forever."
        );
        Assert(
            GetString(
                connection,
                "SELECT replay_last_error FROM battles WHERE battle_id = $battleId;",
                "battle-upload-001"
            ) == "replay_snapshot_not_found",
            "Missing local replay payloads should still persist a terminal error reason."
        );
    }

    var persistedClientState = File.ReadAllText(clientStatePath);
    Assert(
        persistedClientState.Contains(
            "\"client_id\": \"run-client-001\"",
            StringComparison.Ordinal
        ),
        "Replay upload verification should keep the pre-existing client id untouched when replay registration never starts."
    );
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("Combat replay upload sync checks passed.");

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

static long GetInt64(SqliteConnection connection, string sql, string battleId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$battleId", battleId);
    return (long)(
        command.ExecuteScalar()
        ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
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
                StringComparison.OrdinalIgnoreCase
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

static Type? ResolveTypeOrNull(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus");
}
