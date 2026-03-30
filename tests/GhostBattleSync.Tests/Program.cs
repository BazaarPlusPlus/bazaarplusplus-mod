#nullable enable
using System.Net;
using System.Reflection;
using Newtonsoft.Json.Linq;

var syncServiceType = RequireType("BazaarPlusPlus.Game.HistoryPanel.GhostBattleSyncService");
var shouldAdvanceCheckpoint = syncServiceType.GetMethod(
    "ShouldAdvanceCheckpoint",
    BindingFlags.NonPublic | BindingFlags.Static
);
var shouldTreatGhostErrorAsBindingFailure = syncServiceType.GetMethod(
    "ShouldTreatGhostErrorAsBindingFailure",
    BindingFlags.NonPublic | BindingFlags.Static
);
Assert(
    shouldAdvanceCheckpoint != null,
    "GhostBattleSyncService should expose checkpoint advancement logic."
);
Assert(
    shouldTreatGhostErrorAsBindingFailure != null,
    "GhostBattleSyncService should expose binding-related ghost error classification logic."
);

Assert(
    !(bool)shouldAdvanceCheckpoint!.Invoke(null, [200, 200, 3])!,
    "Ghost sync should not advance the checkpoint when the returned batch hits the limit."
);

Assert(
    (bool)shouldAdvanceCheckpoint!.Invoke(null, [12, 200, 14])!,
    "Ghost sync should advance the checkpoint after a non-truncated fetch even when the requested window was clamped."
);

Assert(
    (bool)shouldAdvanceCheckpoint!.Invoke(null, [12, 200, 3])!,
    "Ghost sync should advance the checkpoint after a non-truncated incremental fetch."
);

Assert(
    (bool)
        shouldTreatGhostErrorAsBindingFailure!.Invoke(
            null,
            ["http_403:{\"error\":\"battle_forbidden\"}"]
        )!,
    "Ghost replay/link failures that report battle_forbidden should be attributed to binding when bind just failed."
);

Assert(
    (bool)shouldTreatGhostErrorAsBindingFailure.Invoke(null, ["http_403:battle_forbidden"])!,
    "Ghost replay/link failures should still recognize battle_forbidden after client-side HTTP error formatting."
);

Assert(
    !(bool)
        shouldTreatGhostErrorAsBindingFailure!.Invoke(
            null,
            ["http_404:{\"error\":\"battle_not_found\"}"]
        )!,
    "Non-binding ghost request failures should keep their original error."
);

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-ghost-battle-sync-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);

var listener = new HttpListener();
var port = GetFreePort();
var prefix = $"http://127.0.0.1:{port}/";
listener.Prefixes.Add(prefix);
listener.Start();

var bindRequestCount = 0;
var requestsHandled = Task.Run(async () =>
{
    try
    {
        var context = await listener.GetContextAsync();
        if (context.Request.Url?.AbsolutePath == "/clients/bind")
        {
            bindRequestCount++;
            var responseBytes = System.Text.Encoding.UTF8.GetBytes(
                """{"status":"bound","player_account_id":"player-account-001"}"""
            );
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
            context.Response.Close();
            return;
        }

        context.Response.StatusCode = 404;
        context.Response.Close();
    }
    catch (HttpListenerException) { }
    catch (ObjectDisposedException) { }
});

try
{
    var repositoryType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelRepository");
    var identityStoreType = RequireType(
        "BazaarPlusPlus.Game.RunLogging.Upload.RunUploadIdentityStore"
    );
    var clientStateStoreType = RequireType(
        "BazaarPlusPlus.Game.RunLogging.Upload.RunUploadClientStateStore"
    );
    var keyStoreType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadKeyStore");
    var endpointSetType = RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunUploadEndpointSet");
    var bindingResultType = RequireType(
        "BazaarPlusPlus.Game.RunLogging.Upload.RunUploadBindingResult"
    );

    var repository =
        Activator.CreateInstance(repositoryType, Path.Combine(tempRoot, "history.db"))
        ?? throw new InvalidOperationException("Failed to create HistoryPanelRepository.");
    var identityStore =
        Activator.CreateInstance(identityStoreType, Path.Combine(tempRoot, "install-id.txt"))
        ?? throw new InvalidOperationException("Failed to create RunUploadIdentityStore.");
    var clientStateStore =
        Activator.CreateInstance(clientStateStoreType, Path.Combine(tempRoot, "client-state.json"))
        ?? throw new InvalidOperationException("Failed to create RunUploadClientStateStore.");
    var keyStore =
        Activator.CreateInstance(keyStoreType, Path.Combine(tempRoot, "key.json"))
        ?? throw new InvalidOperationException("Failed to create RunUploadKeyStore.");
    var endpoint = Activator.CreateInstance(endpointSetType)!;
    endpointSetType
        .GetProperty("RegistrationEndpoint")!
        .SetValue(endpoint, $"{prefix}clients/register");
    endpointSetType.GetProperty("UploadEndpoint")!.SetValue(endpoint, $"{prefix}runs/upload");

    InvokeVoid(
        clientStateStoreType,
        clientStateStore,
        "SaveScopedBoundPlayerAccountId",
        ["Runs", "runs-client-001", "player-account-001"]
    );

    var syncService =
        Activator.CreateInstance(
            syncServiceType,
            repository,
            identityStore,
            clientStateStore,
            keyStore,
            endpoint,
            TimeSpan.FromSeconds(10)
        ) ?? throw new InvalidOperationException("Failed to create GhostBattleSyncService.");

    var ensurePlayerBindingAsync = syncServiceType.GetMethod(
        "EnsurePlayerBindingAsync",
        BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(
        ensurePlayerBindingAsync != null,
        "GhostBattleSyncService should expose binding refresh logic."
    );

    var task = (Task)
        ensurePlayerBindingAsync!.Invoke(
            syncService,
            ["runs-client-001", "install-001", "player-account-001", CancellationToken.None]
        )!;
    await task.ConfigureAwait(false);

    var resultProperty = task.GetType().GetProperty("Result");
    var bindingResult =
        resultProperty?.GetValue(task)
        ?? throw new InvalidOperationException("Binding task should produce a result.");
    var succeeded = (bool)(
        bindingResultType.GetProperty("Succeeded")!.GetValue(bindingResult) ?? false
    );
    Assert(succeeded, "Ghost battle binding refresh should succeed when the bind route succeeds.");
    Assert(
        bindRequestCount == 1,
        "Ghost battle binding refresh should hit /clients/bind even when the local cache already matches."
    );
}
finally
{
    listener.Stop();
    listener.Close();
    await requestsHandled.ConfigureAwait(false);
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("Ghost battle sync checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
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

static int GetFreePort()
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
    finally
    {
        listener.Stop();
    }
}
