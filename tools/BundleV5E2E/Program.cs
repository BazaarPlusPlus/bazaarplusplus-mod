using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BepInEx.Logging;
using Microsoft.Data.Sqlite;

var baseUrl = ReadOption(args, "--base-url")
    ?? throw new ArgumentException("--base-url is required");
var routes = ModApiRoutes.TryCreate(baseUrl)
    ?? throw new ArgumentException("--base-url must be an absolute HTTP(S) URL");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(125) };
var client = new BundleUploadClient(http, routes);

var golden = Convert.FromBase64String(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "run-only.bundle.b64")).Trim()
);
var openedGolden = BundleV5Codec.Open(golden);
var first = await Upload(client, golden, openedGolden);
Require(first.Disposition == BundleUploadDisposition.Uploaded && first.Outcome == "stored", "golden Bundle must return 201 stored");
var duplicate = await Upload(client, golden, openedGolden);
Require(duplicate.Disposition == BundleUploadDisposition.Uploaded && duplicate.Outcome == "duplicate", "golden retransmit must return 200 duplicate");

var conflictingPayload = RunPayloadV5Codec.Encode(
    new RunPayloadV5
    {
        RunId = openedGolden.Manifest.Run.RunId,
        PlayerAccountId = openedGolden.Manifest.Run.PlayerAccountId,
        Run = new RunFactsV5 { Hero = "changed" },
    }
);
var conflicting = BundleV5Codec.Build(
    new BundleBuildInputV5
    {
        BundleId = openedGolden.Manifest.BundleId,
        CreatedAtMs = openedGolden.Manifest.CreatedAtMs,
        RunId = openedGolden.Manifest.Run.RunId,
        PlayerAccountId = openedGolden.Manifest.Run.PlayerAccountId,
        RunPayload = conflictingPayload,
    }
);
var conflict = await Upload(client, conflicting.Bytes, BundleV5Codec.Open(conflicting.Bytes));
Require(conflict.Disposition == BundleUploadDisposition.Permanent && conflict.Code == "bundle_id_conflict", "same Bundle ID with valid different bytes must conflict");

var root = Path.Combine(Path.GetTempPath(), "bpp-v5-e2e-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var paths = new E2EPaths(root);
    var store = new RunLogStore(paths);
    var started = DateTimeOffset.UtcNow.AddMinutes(-3);
    var runId = "e2e-run-" + Guid.NewGuid().ToString("N");
    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = runId,
            StartedAtUtc = started,
            Hero = "Vanessa",
            GameMode = "Ranked",
            PlayerAccountId = "e2e-account",
            ModVersion = "e2e",
        }
    );
    store.CompleteRun(runId, new RunLogCompletion { Status = "completed", EndedAtUtc = started.AddMinutes(1) });
    using var coordinator = new BundleSealCoordinator(new E2EServices(paths));
    await coordinator.ReconcileAsync(CancellationToken.None);
    using var connection = new SqliteConnection($"Data Source={PathConstants.RunLogDatabase(root)}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT file_name FROM bundle_outbox WHERE run_id=$runId AND status='pending';";
    command.Parameters.AddWithValue("$runId", runId);
    var fileName = command.ExecuteScalar() as string
        ?? throw new InvalidOperationException("production sealer did not publish an outbox file");
    var sealedBytes = File.ReadAllBytes(Path.Combine(PathConstants.BundleOutbox(root), fileName));
    var sealedBundle = BundleV5Codec.Open(sealedBytes);
    var sealedResponse = await Upload(client, sealedBytes, sealedBundle);
    Require(sealedResponse.Disposition == BundleUploadDisposition.Uploaded && sealedResponse.Outcome == "stored", "real sealer Bundle must return 201 stored");
}
finally
{
    Directory.Delete(root, recursive: true);
}

Console.WriteLine("Bundle V5 local ingest E2E passed: stored, duplicate, conflict, production sealer stored.");

static async Task<BundleUploadResponse> Upload(
    BundleUploadClient client,
    byte[] bytes,
    OpenedBundleV5 opened
)
{
    using var stream = new MemoryStream(bytes, writable: false);
    return await client.UploadAsync(
        stream,
        bytes.Length,
        opened.ContentDigest,
        opened.Manifest.BundleId,
        opened.Manifest.Run.RunId,
        CancellationToken.None
    );
}

static string? ReadOption(string[] args, string name)
{
    for (var index = 0; index + 1 < args.Length; index++)
        if (args[index] == name)
            return args[index + 1];
    return null;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class E2EPaths(string root) : IPathProvider
{
    public string? DataRootDirectoryPath => root;
    public string? PluginsDirectoryPath => root;
}

internal sealed class E2EServices(IPathProvider paths) : IBppServices
{
    public IBppEventBus EventBus { get; } = new InMemoryBppEventBus();
    public IBppConfig Config => null!;
    public IPathProvider Paths => paths;
    public IRunContext RunContext => null!;
    public IGameStateProbe GameStateProbe => null!;
    public IEncounterStateProbe EncounterState => null!;
    public IRunSnapshotProbe RunSnapshot => null!;
    public IGameBuildInfo GameBuild => new E2EBuild();
    public ManualLogSource Logger => null!;
}

internal sealed class E2EBuild : IGameBuildInfo
{
    public string RawVersion => "e2e";
    public GameBuildChannel Channel => GameBuildChannel.Online;
}
