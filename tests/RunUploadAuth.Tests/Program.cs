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

var errorFormatterType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiErrorFormatter");
var formatHttpFailureMethod = errorFormatterType.GetMethod(
    "FormatHttpFailure",
    BindingFlags.Public | BindingFlags.Static
);
Assert(
    formatHttpFailureMethod != null,
    "ModApiErrorFormatter should expose HTTP error formatting for client-visible errors."
);
Assert(
    string.Equals(
        (string?)formatHttpFailureMethod!.Invoke(null, [403, """{"error":"battle_forbidden"}"""]),
        "http_403:battle_forbidden",
        StringComparison.Ordinal
    ),
    "HTTP formatter should extract the server error code from JSON payloads."
);
Assert(
    string.Equals(
        (string?)
            formatHttpFailureMethod.Invoke(
                null,
                [500, """{"error":"projection_failed","detail":"run-store-failed"}"""]
            ),
        "http_500:projection_failed(run-store-failed)",
        StringComparison.Ordinal
    ),
    "HTTP formatter should include concise detail text when the server provides it."
);
Assert(
    string.Equals(
        (string?)
            formatHttpFailureMethod.Invoke(
                null,
                [400, """{"error":"invalid_battle_manifest","reason":"missing_recorded_at_utc"}"""]
            ),
        "http_400:invalid_battle_manifest(missing_recorded_at_utc)",
        StringComparison.Ordinal
    ),
    "HTTP formatter should surface stable server-side validation reasons in client-visible errors."
);
Assert(
    string.Equals(
        (string?)formatHttpFailureMethod.Invoke(null, [502, "bad gateway"]),
        "http_502:bad gateway",
        StringComparison.Ordinal
    ),
    "HTTP formatter should fall back to truncated plain-text bodies."
);

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

        if (request.Url?.AbsolutePath == "/runs")
        {
            Assert(
                request.Headers["X-BPP-Client-Id"] == "client-test-001",
                "Signed upload should include the registered client id."
            );
            Assert(
                request.Headers["X-BPP-Install-Id"] == registeredInstallId,
                "Signed upload should include the same install id used at registration."
            );
            Assert(
                request.Headers["X-BPP-Nonce"] == null,
                "Signed upload should no longer send a nonce header."
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
            Assert(computedHash == bodyHash, "Signed upload should send the raw body SHA-256.");

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
    var uploadStore =
        Activator.CreateInstance(uploadStoreType, dbPath)
        ?? throw new InvalidOperationException("Failed to create RunUploadSqliteStore.");
    var replicatedStoreType = RequireType(
        "BazaarPlusPlus.Game.RunLogging.Persistence.ReplicatedRunLogStore"
    );
    var replicatedStore =
        Activator.CreateInstance(replicatedStoreType, sqliteStore, uploadStore)
        ?? throw new InvalidOperationException("Failed to create ReplicatedRunLogStore.");

    SeedCompletedRun(replicatedStoreType, replicatedStore, runId, startedAt);
    var identityStoreType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiIdentityStore");
    var identityStore =
        Activator.CreateInstance(identityStoreType, installIdPath)
        ?? throw new InvalidOperationException("Failed to create ModApiIdentityStore.");
    var clientStateStoreType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiClientStateStore");
    var clientStateStore =
        Activator.CreateInstance(clientStateStoreType, clientStatePath)
        ?? throw new InvalidOperationException("Failed to create ModApiClientStateStore.");
    var keyStoreType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiKeyStore");
    var keyStore =
        Activator.CreateInstance(keyStoreType, privateKeyPath)
        ?? throw new InvalidOperationException("Failed to create ModApiKeyStore.");
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadIdentityStore") == null,
        "RunUploadIdentityStore compatibility wrapper should be removed."
    );
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadClientStateStore")
            == null,
        "RunUploadClientStateStore compatibility wrapper should be removed."
    );
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadKeyStore") == null,
        "RunUploadKeyStore compatibility wrapper should be removed."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.ModApi.ModApiRequestSigner") != null,
        "ModApiRequestSigner should exist as the primary signing abstraction."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.ModApi.ModApiRegistrationClient") != null,
        "ModApiRegistrationClient should exist as the primary registration abstraction."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.ModApi.ModApiAuthenticatedSession") != null,
        "ModApiAuthenticatedSession should exist as the primary authenticated session abstraction."
    );
    var routesType = RequireType("BazaarPlusPlus.Game.ModApi.ModApiRoutes");
    var tryCreateRoutes = routesType.GetMethod(
        "TryCreate",
        BindingFlags.Public | BindingFlags.Static
    );
    Assert(tryCreateRoutes != null, "ModApiRoutes should expose a static TryCreate factory.");
    var routes =
        tryCreateRoutes!.Invoke(null, [$"{prefix}"])
        ?? throw new InvalidOperationException("Failed to create ModApiRoutes.");
    Assert(
        RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunSummaryUploadPayload") != null,
        "RunSummaryUploadPayload should exist as the primary run upload payload type."
    );
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadPayload") == null,
        "RunUploadPayload compatibility type should be removed."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunSummaryUploadSnapshot") != null,
        "RunSummaryUploadSnapshot should exist as the primary run upload snapshot type."
    );
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadSnapshot") == null,
        "RunUploadSnapshot compatibility type should be removed."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunSummaryUploadCycleResult") != null,
        "RunSummaryUploadCycleResult should exist as the primary run upload cycle result type."
    );
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadCycleResult") == null,
        "RunUploadCycleResult compatibility type should be removed."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunSummaryUploadApiClient") != null,
        "RunSummaryUploadApiClient should exist as the primary run upload API client."
    );
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadApiClient") == null,
        "RunUploadApiClient compatibility type should be removed."
    );
    Assert(
        RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunSummaryUploadService") != null,
        "RunSummaryUploadService should exist as the primary run upload service."
    );
    Assert(
        ResolveTypeOrNull("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadService") == null,
        "RunUploadService compatibility type should be removed."
    );
    var serviceType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunSummaryUploadService");
    var service =
        Activator.CreateInstance(
            serviceType,
            uploadStore,
            identityStore,
            clientStateStore,
            keyStore,
            routes,
            3,
            TimeSpan.FromSeconds(10)
        ) ?? throw new InvalidOperationException("Failed to create RunSummaryUploadService.");

    var uploadTask = (Task)
        Invoke<object>(
            serviceType,
            service,
            "UploadPendingRunSummariesAsync",
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
        clientState["client_id"]?.Value<string>() == "client-test-001",
        "Client registration should persist a single client_id locally."
    );

    File.WriteAllText(
        clientStatePath,
        """
        {
          "client_id": "client-stale-001"
        }
        """
    );
    clientStateStore =
        Activator.CreateInstance(clientStateStoreType, clientStatePath)
        ?? throw new InvalidOperationException("Failed to recreate ModApiClientStateStore.");
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE run_sync_state
            SET dirty = 1,
                retry_count = 0,
                last_error = NULL,
                uploaded_seq = NULL,
                uploaded_status = NULL,
                last_uploaded_at_utc = NULL
            WHERE run_id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.ExecuteNonQuery();
    }

    var recoveryListener = new HttpListener();
    var recoveryPort = GetFreePort();
    var recoveryPrefix = $"http://127.0.0.1:{recoveryPort}/";
    recoveryListener.Prefixes.Add(recoveryPrefix);
    recoveryListener.Start();

    var reRegisterCount = 0;
    var uploadAttemptCount = 0;
    var recoveryRequestsHandled = Task.Run(async () =>
    {
        string? publicModulus = null;
        string? publicExponent = null;
        var registeredInstallId = string.Empty;

        try
        {
            for (var i = 0; i < 3; i++)
            {
                var context = await recoveryListener.GetContextAsync();
                var request = context.Request;
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var body = await reader.ReadToEndAsync();

                if (request.Url?.AbsolutePath == "/clients/register")
                {
                    reRegisterCount++;
                    var payload = JObject.Parse(body);
                    registeredInstallId = payload["install_id"]?.Value<string>() ?? string.Empty;
                    var publicKey = (JObject?)payload["public_key"];
                    publicModulus = publicKey?["modulus_b64"]?.Value<string>();
                    publicExponent = publicKey?["exponent_b64"]?.Value<string>();

                    var responseBytes = Encoding.UTF8.GetBytes(
                        """{"client_id":"client-test-002"}"""
                    );
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
                    context.Response.Close();
                    continue;
                }

                if (request.Url?.AbsolutePath == "/runs")
                {
                    uploadAttemptCount++;
                    if (uploadAttemptCount == 1)
                    {
                        Assert(
                            request.Headers["X-BPP-Client-Id"] == "client-stale-001",
                            "First retry upload should use the stale cached client id."
                        );
                        var responseBytes = Encoding.UTF8.GetBytes(
                            """{"error":"client not found"}"""
                        );
                        context.Response.StatusCode = 404;
                        context.Response.ContentType = "application/json";
                        context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
                        context.Response.Close();
                        continue;
                    }

                    Assert(
                        request.Headers["X-BPP-Client-Id"] == "client-test-002",
                        "Recovered upload should use the re-registered client id."
                    );
                    Assert(
                        request.Headers["X-BPP-Install-Id"] == registeredInstallId,
                        "Recovered upload should keep the persisted install id."
                    );
                    Assert(
                        request.Headers["X-BPP-Nonce"] == null,
                        "Recovered upload should no longer send a nonce header."
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
                        "Recovered upload should preserve body hashing."
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
                                    ?? throw new InvalidOperationException(
                                        "Missing public modulus."
                                    )
                            ),
                            Exponent = Convert.FromBase64String(
                                publicExponent
                                    ?? throw new InvalidOperationException(
                                        "Missing public exponent."
                                    )
                            ),
                        }
                    );
                    var valid = rsa.VerifyData(
                        Encoding.UTF8.GetBytes(canonical),
                        Convert.FromBase64String(signature),
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1
                    );
                    Assert(
                        valid,
                        "Recovered upload should verify against the current registered public key."
                    );

                    context.Response.StatusCode = 200;
                    context.Response.Close();
                    continue;
                }

                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }
        catch (HttpListenerException) { }
        catch (ObjectDisposedException) { }
    });

    try
    {
        routes =
            tryCreateRoutes!.Invoke(null, [$"{recoveryPrefix}"])
            ?? throw new InvalidOperationException("Failed to recreate ModApiRoutes.");
        service =
            Activator.CreateInstance(
                serviceType,
                uploadStore,
                identityStore,
                clientStateStore,
                keyStore,
                routes,
                3,
                TimeSpan.FromSeconds(10)
            ) ?? throw new InvalidOperationException("Failed to recreate RunSummaryUploadService.");

        uploadTask = (Task)
            Invoke<object>(
                serviceType,
                service,
                "UploadPendingRunSummariesAsync",
                [CancellationToken.None]
            );
        await uploadTask.ConfigureAwait(false);
    }
    finally
    {
        recoveryListener.Stop();
        recoveryListener.Close();
        await recoveryRequestsHandled.ConfigureAwait(false);
    }

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetInt64(connection, "SELECT dirty FROM run_sync_state WHERE run_id = $runId;", runId)
                == 0,
            "A stale client id should be recovered by re-registration and successful retry."
        );
    }

    clientState = JObject.Parse(File.ReadAllText(clientStatePath));
    Assert(
        clientState["client_id"]?.Value<string>() == "client-test-002",
        "Re-registration should replace the stale client id."
    );
    Assert(reRegisterCount == 1, "A stale client id should trigger exactly one re-registration.");
    Assert(uploadAttemptCount == 2, "Recovery should retry the upload after re-registration.");
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
    string bodyHash
)
{
    return string.Join(
        "\n",
        new[] { method.ToUpperInvariant(), absolutePath, clientId, installId, timestamp, bodyHash }
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

static void SeedCompletedRun(
    Type replicatedStoreType,
    object replicatedStore,
    string runId,
    DateTimeOffset startedAt
)
{
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

static Type? ResolveTypeOrNull(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus");
}
