using System.Net;
using System.Text;
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

var root = Path.Combine(Path.GetTempPath(), "bpp-v5-pipeline-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var paths = new TestPaths(root);
    var store = new RunLogStore(paths);
    var started = DateTimeOffset.UtcNow.AddMinutes(-3);
    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = "run-seal-001",
            StartedAtUtc = started,
            Hero = "Vanessa",
            GameMode = "Ranked",
            PlayerAccountId = "account-001",
            BundleScreenshotRequested = false,
            ModVersion = "5.0.0",
        }
    );
    store.CompleteRun(
        "run-seal-001",
        new RunLogCompletion { EndedAtUtc = started.AddMinutes(1), Status = "completed" }
    );

    var services = new TestServices(paths);
    using var coordinator = new BundleSealCoordinator(services);
    await coordinator.ReconcileAsync(CancellationToken.None);

    var database = PathConstants.RunLogDatabase(root);
    using (var connection = new SqliteConnection($"Data Source={database}"))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT bundle_id, file_name, status FROM bundle_outbox WHERE run_id='run-seal-001';";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            reader.Close();
            using var job = connection.CreateCommand();
            job.CommandText =
                "SELECT state, last_error_code, last_error_detail FROM bundle_seal_jobs WHERE run_id='run-seal-001';";
            using var jobReader = job.ExecuteReader();
            var detail = jobReader.Read()
                ? $"{jobReader.GetString(0)}/{(jobReader.IsDBNull(1) ? "" : jobReader.GetString(1))}/{(jobReader.IsDBNull(2) ? "" : jobReader.GetString(2))}"
                : "job missing";
            throw new InvalidOperationException("completed run should seal into outbox: " + detail);
        }
        var bundleId = reader.GetString(0);
        var file = Path.Combine(PathConstants.BundleOutbox(root), reader.GetString(1));
        Assert(reader.GetString(2) == "pending", "new outbox row should be pending");
        Assert(File.Exists(file), "sealed bundle file should exist");
        var opened = BundleV5Codec.Open(File.ReadAllBytes(file));
        Assert(opened.Manifest.BundleId == bundleId, "file identity should match outbox");
        Assert(opened.Manifest.Screenshot == null, "screenshot-disabled run should be Run-only");
        var payload = RunPayloadV5Codec.Decode(opened.RunPayload);
        Assert(payload.RunId == "run-seal-001", "payload should preserve run identity");
        Assert(payload.PlayerAccountId == "account-001", "payload should preserve frozen account");
    }

    using (var repository = new SqliteConnection($"Data Source={database}"))
    {
        repository.Open();
        using var delete = repository.CreateCommand();
        delete.CommandText = "DELETE FROM runs WHERE run_id='run-seal-001';";
        delete.ExecuteNonQuery();
        using var count = repository.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM bundle_outbox WHERE run_id='run-seal-001';";
        Assert(
            Convert.ToInt32(count.ExecuteScalar()) == 1,
            "outbox must not FK-block Run deletion"
        );
    }

    await VerifyUploadClientAsync();
    Console.WriteLine("Bundle pipeline tests passed.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static async Task VerifyUploadClientAsync()
{
    var payload = RunPayloadV5Codec.Encode(
        new RunPayloadV5 { RunId = "run-http", PlayerAccountId = "account-http" }
    );
    var built = BundleV5Codec.Build(
        new BundleBuildInputV5
        {
            BundleId = "01J00000000000000000000111",
            CreatedAtMs = 1_785_628_800_000,
            RunId = "run-http",
            PlayerAccountId = "account-http",
            RunPayload = payload,
        }
    );
    var responses = new Queue<HttpResponseMessage>(
        new[]
        {
            Json(
                HttpStatusCode.Created,
                "{\"bundle_id\":\"01J00000000000000000000111\",\"run_id\":\"run-http\",\"outcome\":\"stored\",\"bazaardb_delivery\":\"not_applicable\"}"
            ),
            Json(
                HttpStatusCode.OK,
                "{\"bundle_id\":\"01J00000000000000000000111\",\"run_id\":\"run-http\",\"outcome\":\"duplicate\",\"bazaardb_delivery\":\"not_applicable\"}"
            ),
            Json(
                HttpStatusCode.Conflict,
                "{\"error\":{\"code\":\"bundle_id_conflict\",\"message\":\"different\",\"retryable\":false,\"request_id\":\"r\"}}"
            ),
            Json(
                HttpStatusCode.ServiceUnavailable,
                "{\"error\":{\"code\":\"storage_unavailable\",\"message\":\"later\",\"retryable\":true,\"request_id\":\"r\"}}"
            ),
        }
    );
    var handler = new CaptureHandler(responses);
    using var http = new HttpClient(handler);
    var routes = ModApiRoutes.TryCreate("https://example.test")!;
    var client = new BundleUploadClient(http, routes);
    var dispositions = new List<BundleUploadDisposition>();
    for (var index = 0; index < 4; index++)
    {
        using var stream = new MemoryStream(built.Bytes, writable: false);
        var response = await client.UploadAsync(
            stream,
            built.Bytes.Length,
            built.ContentDigest,
            built.Manifest.BundleId,
            built.Manifest.Run.RunId,
            CancellationToken.None
        );
        dispositions.Add(response.Disposition);
    }
    Assert(
        dispositions.SequenceEqual(
            new[]
            {
                BundleUploadDisposition.Uploaded,
                BundleUploadDisposition.Uploaded,
                BundleUploadDisposition.Permanent,
                BundleUploadDisposition.Transient,
            }
        ),
        "upload response matrix should classify exactly"
    );
    Assert(
        handler.Bodies.All(body => body.SequenceEqual(built.Bytes)),
        "retries must be byte-identical"
    );
    Assert(
        handler.ContentDigests.All(value => value == built.ContentDigest),
        "Content-Digest must be exact"
    );
}

static HttpResponseMessage Json(HttpStatusCode status, string body) =>
    new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class CaptureHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
{
    internal List<byte[]> Bodies { get; } = new();
    internal List<string?> ContentDigests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Bodies.Add(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
        ContentDigests.Add(request.Headers.GetValues("Content-Digest").Single());
        if (request.Content.Headers.ContentLength != Bodies[^1].Length)
            throw new InvalidOperationException("Content-Length required");
        if (request.Content.Headers.ContentType!.MediaType != BundleLimitsV5.BundleContentType)
            throw new InvalidOperationException("Bundle Content-Type required");
        return responses.Dequeue();
    }
}

internal sealed class TestPaths(string root) : IPathProvider
{
    public string? DataRootDirectoryPath => root;
    public string? PluginsDirectoryPath => root;
}

internal sealed class TestServices(IPathProvider paths) : IBppServices
{
    public IBppEventBus EventBus { get; } = new InMemoryBppEventBus();
    public IBppConfig Config => null!;
    public IPathProvider Paths => paths;
    public IRunContext RunContext => null!;
    public IGameStateProbe GameStateProbe => null!;
    public IEncounterStateProbe EncounterState => null!;
    public IRunSnapshotProbe RunSnapshot => null!;
    public IGameBuildInfo GameBuild => new TestBuild();
    public ManualLogSource Logger => null!;
}

internal sealed class TestBuild : IGameBuildInfo
{
    public string RawVersion => "test";
    public GameBuildChannel Channel => GameBuildChannel.Online;
}
