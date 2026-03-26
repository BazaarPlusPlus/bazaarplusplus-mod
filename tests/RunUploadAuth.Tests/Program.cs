#nullable enable
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-run-upload-auth-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);

var dbPath = Path.Combine(tempRoot, "run-logs.db");
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

            var responseBytes = Encoding.UTF8.GetBytes("""{"client_id":"client-test-001"}""");
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
            context.Response.Close();
            continue;
        }

        if (request.Url?.AbsolutePath == "/runs/upload")
        {
            Assert(
                request.Headers["X-BPP-Client-Id"] == "client-test-001",
                "Signed upload should include the registered client id."
            );
            Assert(
                request.Headers["X-BPP-Install-Id"] == registeredInstallId,
                "Signed upload should include the same install id used at registration."
            );

            var timestamp = request.Headers["X-BPP-Timestamp"] ?? throw new InvalidOperationException("Missing timestamp header.");
            var nonce = request.Headers["X-BPP-Nonce"] ?? throw new InvalidOperationException("Missing nonce header.");
            var bodyHash = request.Headers["X-BPP-Content-SHA256"] ?? throw new InvalidOperationException("Missing body hash header.");
            var signature = request.Headers["X-BPP-Signature"] ?? throw new InvalidOperationException("Missing signature header.");

            using var sha256 = SHA256.Create();
            var computedHash = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(body)));
            Assert(computedHash == bodyHash, "Signed upload should send the raw body SHA-256.");

            var canonical = BuildCanonical(
                "POST",
                request.Url.AbsolutePath,
                request.Headers["X-BPP-Client-Id"]!,
                request.Headers["X-BPP-Install-Id"]!,
                timestamp,
                nonce,
                bodyHash
            );

            using var rsa = RSA.Create();
            rsa.ImportParameters(
                new RSAParameters
                {
                    Modulus = Convert.FromBase64String(publicModulus ?? throw new InvalidOperationException("Missing public modulus.")),
                    Exponent = Convert.FromBase64String(publicExponent ?? throw new InvalidOperationException("Missing public exponent.")),
                }
            );
            var valid = rsa.VerifyData(
                Encoding.UTF8.GetBytes(canonical),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            Assert(valid, "Signed upload should verify against the registered public key.");

            var payload = JObject.Parse(body);
            Assert(
                payload["client_id"]?.Value<string>() == "client-test-001",
                "Upload body should also contain client_id."
            );

            context.Response.StatusCode = 200;
            context.Response.Close();
            continue;
        }

        context.Response.StatusCode = 404;
        context.Response.Close();
    }
});

try
{
    const string runId = "run-auth-001";
    var startedAt = new DateTimeOffset(2026, 3, 27, 1, 0, 0, TimeSpan.Zero);

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

    var identityStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadIdentityStore");
    var identityStore = Activator.CreateInstance(identityStoreType, installIdPath)
        ?? throw new InvalidOperationException("Failed to create RunUploadIdentityStore.");
    var clientStateStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadClientStateStore");
    var clientStateStore = Activator.CreateInstance(clientStateStoreType, clientStatePath)
        ?? throw new InvalidOperationException("Failed to create RunUploadClientStateStore.");
    var keyStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadKeyStore");
    var keyStore = Activator.CreateInstance(keyStoreType, privateKeyPath)
        ?? throw new InvalidOperationException("Failed to create RunUploadKeyStore.");
    var routeKindType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadRouteKind");
    var modeType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadMode");
    var routeStateStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadRouteStateStore");
    var routeStateStore = Activator.CreateInstance(
        routeStateStoreType,
        Path.Combine(tempRoot, "route.json")
    ) ?? throw new InvalidOperationException("Failed to create RunUploadRouteStateStore.");
    var routeSelectorType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadRouteSelector");
    var globalRouteKind = Enum.Parse(routeKindType, "Global");
    var globalMode = Enum.Parse(modeType, "Global");
    var routeSelector = Activator.CreateInstance(
        routeSelectorType,
        globalMode,
        routeStateStore,
        2,
        TimeSpan.FromMinutes(60)
    ) ?? throw new InvalidOperationException("Failed to create RunUploadRouteSelector.");
    var endpointSetType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadEndpointSet");
    var globalEndpoint = Activator.CreateInstance(endpointSetType)!;
    endpointSetType.GetProperty("RouteKind")!.SetValue(globalEndpoint, globalRouteKind);
    endpointSetType.GetProperty("RegistrationEndpoint")!.SetValue(globalEndpoint, $"{prefix}clients/register");
    endpointSetType.GetProperty("UploadEndpoint")!.SetValue(globalEndpoint, $"{prefix}runs/upload");
    var serviceType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadService");
    var service = Activator.CreateInstance(
        serviceType,
        uploadStore,
        identityStore,
        clientStateStore,
        keyStore,
        routeSelector,
        globalEndpoint,
        null,
        3,
        TimeSpan.FromSeconds(10)
    ) ?? throw new InvalidOperationException("Failed to create RunUploadService.");

    var uploadTask = (Task)Invoke<object>(
        serviceType,
        service,
        "UploadPendingRunsAsync",
        [CancellationToken.None]
    );
    await uploadTask.ConfigureAwait(false);

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetInt64(connection, "SELECT dirty FROM run_sync_state WHERE run_id = $runId;", runId)
                == 0,
            "Successful signed upload should clear the dirty flag."
        );
    }

    var clientState = JObject.Parse(File.ReadAllText(clientStatePath));
    Assert(
        clientState["client_ids"]?["Global"]?.Value<string>() == "client-test-001",
        "Client registration should persist route-scoped client_id locally."
    );
}
finally
{
    listener.Stop();
    listener.Close();
    await requestsHandled.ConfigureAwait(false);
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("Run upload auth checks passed.");

static string BuildCanonical(
    string method,
    string absolutePath,
    string clientId,
    string installId,
    string timestamp,
    string nonce,
    string bodyHash
)
{
    return string.Join(
        "\n",
        new[] { method.ToUpperInvariant(), absolutePath, clientId, installId, timestamp, nonce, bodyHash }
    );
}

static int GetFreePort()
{
    var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

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

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
