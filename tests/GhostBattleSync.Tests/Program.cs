#nullable enable
using System.Net;
using System.Net.Http.Headers;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Models;

await DiscoveryUsesV5ShapeAndLimit();
await RetryAfterStartsCooldown();
await ExpiredUrlRefreshesOnceAndBecomesTerminal();
await CorruptBundleBecomesPermanentWithoutRepeatedDownload();
UnknownProjectionAndCountsStayUnknown();

Console.WriteLine("Ghost battle V5 sync tests passed.");

static async Task DiscoveryUsesV5ShapeAndLimit()
{
    Uri? observed = null;
    var handler = new RecordingHandler(request =>
    {
        observed = request.RequestUri;
        return JsonResponse(
            BattlePage(
                "battle-1",
                "01K1ABCDEF0123456789ABCDEF",
                "https://r2.example/bundle",
                DateTimeOffset.UtcNow.AddMinutes(5),
                "account-uploader",
                "account-local"
            )
        );
    });
    using var http = new HttpClient(handler);
    var client = new GhostBattleClient(http, ModApiRoutes.TryCreate("https://api.example")!);
    var result = await client.QueryAgainstMeAsync("account-local", 999, CancellationToken.None);

    Assert(result.Succeeded && result.Battles.Count == 1, "V5 discovery page must parse.");
    Assert(observed?.AbsolutePath == "/ghost-battles", "Discovery route must be /ghost-battles.");
    Assert(
        observed?.Query.Contains("limit=200", StringComparison.Ordinal) == true,
        "Discovery limit must clamp to 200."
    );
    Assert(
        result.Battles[0].PlayerHandItemCount == null,
        "Projection counts must remain unknown before download."
    );
    Assert(
        result.Battles[0].ReplayAvailable,
        "A valid presigned URL must expose replay availability."
    );

    var overLimit = new RecordingHandler(_ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1]),
        };
        response.Content.Headers.ContentLength = 8_388_608;
        return response;
    });
    using var downloadHttp = new HttpClient(overLimit);
    var downloadClient = new GhostBattleClient(
        downloadHttp,
        ModApiRoutes.TryCreate("https://api.example")!
    );
    var tooLarge = await downloadClient.DownloadBundleAsync(
        "https://r2.example/large",
        CancellationToken.None
    );
    Assert(
        !tooLarge.Succeeded && tooLarge.Error == "bundle_too_large",
        "Content-Length must be bounded before reading."
    );
}

static async Task RetryAfterStartsCooldown()
{
    using var fixture = new GhostFixture(request =>
    {
        var response = new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent("{\"error\":\"rate_limited\",\"retryable\":true}"),
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
        return response;
    });
    var first = await fixture.Service.SyncRecentBattlesAsync(CancellationToken.None);
    var second = await fixture.Service.SyncRecentBattlesAsync(CancellationToken.None);
    Assert(
        !first.Succeeded && !second.Succeeded,
        "Rate-limited discovery must fail without throwing."
    );
    Assert(fixture.Handler.Count == 1, "Retry-After cooldown must suppress the second HTTP query.");
}

static async Task ExpiredUrlRefreshesOnceAndBecomesTerminal()
{
    var bundleId = "01K1ABCDEF0123456789ABCDEG";
    var record = CreateImport(
        "battle-expired",
        bundleId,
        "https://r2.example/expired",
        DateTimeOffset.UtcNow.AddMinutes(-1)
    );
    using var fixture = new GhostFixture(request =>
    {
        if (request.RequestUri?.AbsolutePath == "/ghost-battles")
            return JsonResponse(
                BattlePage(
                    record.BattleId,
                    bundleId,
                    "https://r2.example/refreshed",
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    "account-uploader",
                    "account-local"
                )
            );
        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("missing"),
        };
    });
    fixture.Repository.UpsertGhostBattles("account-local", [record]);
    var localId = fixture.Repository.ListRecentGhostBattles(10).Single().BattleId;
    var replayRoot = Path.Combine(fixture.Root, "CombatReplays");

    var first = await fixture.Service.DownloadReplayAsync(
        localId,
        replayRoot,
        CancellationToken.None
    );
    var callsAfterFirst = fixture.Handler.Count;
    var second = await fixture.Service.DownloadReplayAsync(
        localId,
        replayRoot,
        CancellationToken.None
    );
    Assert(!first.Succeeded && !second.Succeeded, "A refreshed 404 must become expired.");
    Assert(
        callsAfterFirst == 2,
        "An expired URL must refresh discovery exactly once before download."
    );
    Assert(
        fixture.Handler.Count == callsAfterFirst,
        "An expired terminal row must not download again."
    );
    Assert(
        fixture.Repository.TryGetGhostBundleReference(localId)?.ReplayState == "expired",
        "Expired must persist."
    );
}

static async Task CorruptBundleBecomesPermanentWithoutRepeatedDownload()
{
    using var fixture = new GhostFixture(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent([0, 1, 2, 3]),
    });
    fixture.Repository.UpsertGhostBattles(
        "account-local",
        [
            CreateImport(
                "battle-corrupt",
                "01K1ABCDEF0123456789ABCDEH",
                "https://r2.example/corrupt",
                DateTimeOffset.UtcNow.AddMinutes(5)
            ),
        ]
    );
    var localId = fixture.Repository.ListRecentGhostBattles(10).Single().BattleId;
    var replayRoot = Path.Combine(fixture.Root, "CombatReplays");

    var first = await fixture.Service.DownloadReplayAsync(
        localId,
        replayRoot,
        CancellationToken.None
    );
    var callsAfterFirst = fixture.Handler.Count;
    var second = await fixture.Service.DownloadReplayAsync(
        localId,
        replayRoot,
        CancellationToken.None
    );
    Assert(!first.Succeeded && !second.Succeeded, "Corrupt bundles must fail permanently.");
    Assert(
        fixture.Handler.Count == callsAfterFirst,
        "unavailable_payload must not download twice."
    );
    Assert(
        fixture.Repository.TryGetGhostBundleReference(localId)?.ReplayState
            == "unavailable_payload",
        "Corruption state must persist."
    );
}

static void UnknownProjectionAndCountsStayUnknown()
{
    Assert(
        GhostBattleLocalProjector.ProjectResultToLocal("unknown") == null,
        "Unknown server result must map to the local unknown result."
    );
    var record = CreateImport(
        "battle-unknown",
        "01K1ABCDEF0123456789ABCDEJ",
        "https://r2.example/unknown",
        DateTimeOffset.UtcNow.AddMinutes(5)
    );
    using var fixture = new GhostFixture(_ => JsonResponse("{\"battles\":[]}"));
    fixture.Repository.UpsertGhostBattles("account-local", [record]);
    var local = fixture.Repository.ListRecentGhostBattles(10).Single();
    Assert(
        !local.SnapshotCounts.Known,
        "Hand and skill counts must be unknown before replay download."
    );
    Assert(
        HistoryPanelFormatter
            .FormatSnapshotSummary(local.SnapshotCounts)
            .Contains("unknown", StringComparison.OrdinalIgnoreCase),
        "Unknown counts must not render as zero."
    );
}

static GhostBattleImportRecord CreateImport(
    string battleId,
    string bundleId,
    string url,
    DateTimeOffset expiry
) =>
    new()
    {
        BattleId = battleId,
        BundleId = bundleId,
        DownloadUrl = url,
        DownloadExpiresAtUtc = expiry,
        RecordedAtUtc = DateTimeOffset.UtcNow,
        Day = 1,
        Hour = 2,
        PlayerAccountId = "account-uploader",
        PlayerName = "Uploader",
        OpponentAccountId = "account-local",
        OpponentName = "Local",
        CombatKind = "PVPCombat",
        Result = "unknown",
    };

static string BattlePage(
    string battleId,
    string bundleId,
    string url,
    DateTimeOffset expiry,
    string uploader,
    string opponent
) =>
    System.Text.Json.JsonSerializer.Serialize(
        new Dictionary<string, object?>
        {
            ["battles"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["battle_id"] = battleId,
                    ["bundle_id"] = bundleId,
                    ["recorded_at_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["download_url"] = url,
                    ["download_expires_at_ms"] = expiry.ToUnixTimeMilliseconds(),
                    ["day"] = 1,
                    ["hour"] = 2,
                    ["combat_kind"] = "PVPCombat",
                    ["result"] = "unknown",
                    ["is_final_battle"] = false,
                    ["player"] = new Dictionary<string, object?>
                    {
                        ["account_id"] = uploader,
                        ["display_name"] = "Uploader",
                    },
                    ["opponent"] = new Dictionary<string, object?>
                    {
                        ["account_id"] = opponent,
                        ["display_name"] = "Local",
                    },
                },
            },
        }
    );

static HttpResponseMessage JsonResponse(string json) =>
    new(HttpStatusCode.OK) { Content = new StringContent(json) };

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = respond;

    public int Count { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Count++;
        return Task.FromResult(_respond(request));
    }
}

internal sealed class GhostFixture : IDisposable
{
    private readonly HttpClient _http;

    public GhostFixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        Root = Path.Combine(Path.GetTempPath(), $"bpp-ghost-v5-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        Repository = new HistoryPanelRepository(Path.Combine(Root, "runs.sqlite3"));
        Handler = new RecordingHandler(respond);
        _http = new HttpClient(Handler);
        var online = new ModOnlineClient(_http, ModApiRoutes.TryCreate("https://api.example")!);
        Service = new GhostBattleSyncService(Repository, online, () => "account-local");
    }

    public string Root { get; }
    public HistoryPanelRepository Repository { get; }
    public RecordingHandler Handler { get; }
    public GhostBattleSyncService Service { get; }

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
