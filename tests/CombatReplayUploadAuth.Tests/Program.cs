#nullable enable
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-combat-replay-upload-auth-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);

var dbPath = Path.Combine(tempRoot, "run-logs.db");
var replayRoot = Path.Combine(tempRoot, "CombatReplays");
var installIdPath = Path.Combine(tempRoot, "install-id.txt");
var clientStatePath = Path.Combine(tempRoot, "client.json");
var privateKeyPath = Path.Combine(tempRoot, "key.json");
var listener = new HttpListener();
var port = GetFreePort();
var prefix = $"http://127.0.0.1:{port}/";
listener.Prefixes.Add(prefix);
listener.Start();

var requestsHandled = Task.Run(async () =>
{
    string? publicModulus = null;
    string? publicExponent = null;
    var registeredInstallId = string.Empty;

    for (var i = 0; i < 2; i++)
    {
        var context = await listener.GetContextAsync();
        var request = context.Request;
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();

        if (request.Url?.AbsolutePath == "/clients/register")
        {
            var payload = JObject.Parse(body);
            registeredInstallId = payload["install_id"]?.Value<string>() ?? string.Empty;
            var publicKey = (JObject?)payload["public_key"];
            publicModulus = publicKey?["modulus_b64"]?.Value<string>();
            publicExponent = publicKey?["exponent_b64"]?.Value<string>();

            var responseBytes = Encoding.UTF8.GetBytes("""{"client_id":"client-replay-001"}""");
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
            context.Response.Close();
            continue;
        }

        if (request.Url?.AbsolutePath == "/battles/upload")
        {
            Assert(
                request.Headers["X-BPP-Client-Id"] == "client-replay-001",
                "Signed battle upload should include the registered client id."
            );
            Assert(
                request.Headers["X-BPP-Install-Id"] == registeredInstallId,
                "Signed battle upload should include the registered install id."
            );
            Assert(
                request.Headers["X-BPP-Battle-Id"] == "battle-auth-001",
                "Signed battle upload should include the battle id."
            );
            Assert(
                request.Headers["X-BPP-Nonce"] == null,
                "Signed battle upload should no longer send a nonce header."
            );

            var timestamp =
                request.Headers["X-BPP-Timestamp"]
                ?? throw new InvalidOperationException("Missing timestamp header.");
            var bodyHash =
                request.Headers["X-BPP-Content-SHA256"]
                ?? throw new InvalidOperationException("Missing body hash header.");
            var signature =
                request.Headers["X-BPP-Signature"]
                ?? throw new InvalidOperationException("Missing signature header.");

            using var sha256 = SHA256.Create();
            var computedHash = Convert.ToBase64String(
                sha256.ComputeHash(Encoding.UTF8.GetBytes(body))
            );
            Assert(
                computedHash == bodyHash,
                "Signed replay upload should send the raw body SHA-256."
            );

            var canonical = BuildCanonical(
                "POST",
                request.Url.AbsolutePath,
                request.Headers["X-BPP-Client-Id"]!,
                request.Headers["X-BPP-Install-Id"]!,
                timestamp,
                bodyHash
            );

            using var rsa = RSA.Create();
            rsa.ImportParameters(
                new RSAParameters
                {
                    Modulus = Convert.FromBase64String(
                        publicModulus
                            ?? throw new InvalidOperationException("Missing public modulus.")
                    ),
                    Exponent = Convert.FromBase64String(
                        publicExponent
                            ?? throw new InvalidOperationException("Missing public exponent.")
                    ),
                }
            );
            var valid = rsa.VerifyData(
                Encoding.UTF8.GetBytes(canonical),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            Assert(valid, "Signed replay upload should verify against the registered public key.");

            var payload = JObject.Parse(body);
            Assert(
                payload["battle_id"]?.Value<string>() == "battle-auth-001",
                "Replay upload body should contain battle_id."
            );
            Assert(
                payload["client_id"]?.Value<string>() == "client-replay-001",
                "Replay upload body should contain client_id."
            );
            Assert(
                payload["replay_payload"]?["battle_id"]?.Value<string>() == "battle-auth-001",
                "Replay upload body should embed the replay payload."
            );
            Assert(
                !string.IsNullOrWhiteSpace(
                    payload["battle_manifest"]?["recorded_at_utc"]?.Value<string>()
                ),
                "Replay upload body should serialize battle manifest timestamps as recorded_at_utc for the server contract."
            );

            var responseBytes = Encoding.UTF8.GetBytes(
                """{"object_key":"combat-replays/global/client-replay-001/battle-auth-001.payload.json"}"""
            );
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
            context.Response.Close();
            continue;
        }

        context.Response.StatusCode = 404;
        context.Response.Close();
    }
});

try
{
    var runStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Persistence.SqliteRunLogStore");
    var runStore =
        Activator.CreateInstance(runStoreType, dbPath)
        ?? throw new InvalidOperationException("Failed to create SqliteRunLogStore.");
    var createRunRequestType = RequireType("BazaarPlusPlus.Game.RunLogging.Models.RunLogCreateRequest");
    var createRunRequest = Activator.CreateInstance(createRunRequestType)!;
    createRunRequestType.GetProperty("RunId")!.SetValue(createRunRequest, "server-run-auth-001");
    createRunRequestType
        .GetProperty("StartedAtUtc")!
        .SetValue(createRunRequest, new DateTimeOffset(2026, 3, 28, 2, 30, 0, TimeSpan.Zero));
    createRunRequestType.GetProperty("Hero")!.SetValue(createRunRequest, "Vanessa");
    createRunRequestType.GetProperty("GameMode")!.SetValue(createRunRequest, "Ranked");
    createRunRequestType.GetProperty("Status")!.SetValue(createRunRequest, "active");
    InvokeVoid(runStoreType, runStore, "CreateRun", [createRunRequest]);

    var payloadStoreType = RequireType("BazaarPlusPlus.Game.CombatReplay.CombatReplayPayloadStore");
    var payloadStore =
        Activator.CreateInstance(payloadStoreType, replayRoot)
        ?? throw new InvalidOperationException("Failed to create CombatReplayPayloadStore.");
    var payloadType = RequireType("BazaarPlusPlus.Game.PvpBattles.PvpReplayPayload");
    var payload = Activator.CreateInstance(payloadType)!;
    payloadType.GetProperty("BattleId")!.SetValue(payload, "battle-auth-001");
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
    manifestType.GetProperty("BattleId")!.SetValue(manifest, "battle-auth-001");
    manifestType.GetProperty("RunId")!.SetValue(manifest, "server-run-auth-001");
    manifestType
        .GetProperty("RecordedAtUtc")!
        .SetValue(manifest, new DateTimeOffset(2026, 3, 28, 3, 0, 0, TimeSpan.Zero));
    manifestType.GetProperty("CombatKind")!.SetValue(manifest, "PVPCombat");
    InvokeVoid(catalogType, catalog, "Save", [manifest]);

    var storeType = RequireType("BazaarPlusPlus.Game.CombatReplay.Upload.BattleUploadSqliteStore");
    var store =
        Activator.CreateInstance(storeType, dbPath, replayRoot)
        ?? throw new InvalidOperationException("Failed to create BattleUploadSqliteStore.");
    InvokeVoid(storeType, store, "MarkReplayDirty", ["battle-auth-001"]);

    var identityStoreType = RequireType(
        "BazaarPlusPlus.Game.RunLogging.Upload.RunUploadIdentityStore"
    );
    var identityStore =
        Activator.CreateInstance(identityStoreType, installIdPath)
        ?? throw new InvalidOperationException("Failed to create RunUploadIdentityStore.");
    var clientStateStoreType = RequireType(
        "BazaarPlusPlus.Game.RunLogging.Upload.RunUploadClientStateStore"
    );
    var clientStateStore =
        Activator.CreateInstance(clientStateStoreType, clientStatePath)
        ?? throw new InvalidOperationException("Failed to create RunUploadClientStateStore.");
    var keyStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadKeyStore");
    var keyStore =
        Activator.CreateInstance(keyStoreType, privateKeyPath)
        ?? throw new InvalidOperationException("Failed to create RunUploadKeyStore.");
    var serviceType = RequireType("BazaarPlusPlus.Game.CombatReplay.Upload.BattleUploadService");
    var service =
        Activator.CreateInstance(
            serviceType,
            store,
            identityStore,
            clientStateStore,
            keyStore,
            $"{prefix}clients/register",
            $"{prefix}battles/upload",
            3,
            TimeSpan.FromSeconds(10)
        ) ?? throw new InvalidOperationException("Failed to create BattleUploadService.");

    var uploadTask = (Task)
        Invoke<object>(serviceType, service, "UploadPendingBattlesAsync", [CancellationToken.None]);
    await uploadTask.ConfigureAwait(false);

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetInt64(
                connection,
                "SELECT replay_dirty FROM battles WHERE battle_id = $battleId;",
                "battle-auth-001"
            ) == 0,
            "Successful replay upload should clear the dirty flag."
        );
        Assert(
            !ColumnExists(connection, "battles", "payload_sha256")
                && !ColumnExists(connection, "battles", "object_key"),
            "Successful battle upload should not persist upload object metadata locally."
        );
    }

    var clientState = JObject.Parse(File.ReadAllText(clientStatePath));
    Assert(
        clientState["client_ids"]?["ReplayCloudflare"]?.Value<string>() == "client-replay-001",
        "Replay registration should persist a replay-scoped client id without using the run route keys."
    );
}
finally
{
    listener.Stop();
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

await requestsHandled.ConfigureAwait(false);
Console.WriteLine("Combat replay upload auth checks passed.");

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

static string BuildCanonical(
    string method,
    string absolutePath,
    string clientId,
    string installId,
    string timestamp,
    string bodyHash
)
{
    return string.Join(
        "\n",
        new[]
        {
            method.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(absolutePath) ? "/" : absolutePath.Trim(),
            clientId.Trim(),
            installId.Trim(),
            timestamp.Trim(),
            bodyHash.Trim(),
        }
    );
}

static int GetFreePort()
{
    var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
