#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public class BazaarAgentHttpServerTests
{
    // ---------------------------------------------------------------------------
    // Fixture
    // ---------------------------------------------------------------------------

    private sealed class ServerFixture : IDisposable
    {
        public int Port { get; }
        public BazaarAgentHttpServer Server { get; }
        public BazaarAgentActionQueue Queue { get; }
        private BazaarAgentContextSnapshot? _snapshot;
        public BazaarAgentContextSnapshot? CurrentSnapshot => _snapshot;

        public void SetSnapshot(BazaarAgentContextSnapshot? s) => _snapshot = s;

        public ServerFixture(int timeoutMs = 5000)
        {
            Port = PickFreePort();
            Queue = new BazaarAgentActionQueue(timeoutMs);
            Server = new BazaarAgentHttpServer(
                Port,
                () => CurrentSnapshot,
                Queue,
                new TestLogger()
            );
            Server.Start();
        }

        public void Dispose()
        {
            try
            {
                Server.Dispose();
            }
            catch { }
            try
            {
                Queue.Dispose();
            }
            catch { }
        }

        private static int PickFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed class TestLogger : IBazaarAgentLogger
    {
        public void Info(string message) { }

        public void Warning(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }

    private static HttpClient Http() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    // ---------------------------------------------------------------------------
    // Helper: build a minimal valid context with a Wait action available
    // ---------------------------------------------------------------------------

    private static BazaarAgentContext WaitContext(ulong tickId = 1) =>
        new()
        {
            TickId = tickId,
            IsEnabled = true,
            StateName = BazaarAgentRunStateName.Choice,
            PlayerGold = 10,
            AvailableActions = new[]
            {
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.Wait,
                    Group = BazaarAgentActionGroup.Wait,
                    DisplayKey = "Wait",
                },
            },
        };

    // ---------------------------------------------------------------------------
    // Case 1: GET 503 when snapshot is null
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetContext_Returns503_WhenSnapshotIsNull()
    {
        using var f = new ServerFixture();
        // no SetSnapshot call

        using var http = Http();
        var res = await http.GetAsync($"http://127.0.0.1:{f.Port}/v1/context");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"error\"", body);
        Assert.Contains("\"unavailable\"", body);
    }

    // ---------------------------------------------------------------------------
    // Case 2: GET 200 with body + ETag
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetContext_Returns200WithBodyAndETag()
    {
        using var f = new ServerFixture();
        var ctx = new BazaarAgentContext
        {
            TickId = 42,
            StateName = BazaarAgentRunStateName.Choice,
            PlayerGold = 10,
            IsEnabled = true,
        };
        f.SetSnapshot(new BazaarAgentContextSnapshot(ctx));

        using var http = Http();
        var res = await http.GetAsync($"http://127.0.0.1:{f.Port}/v1/context");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("\"42\"", res.Headers.ETag?.ToString());
        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"tickId\":42", body);
        Assert.Contains("\"stateName\":\"Choice\"", body);
    }

    // ---------------------------------------------------------------------------
    // Case 3: GET 304 on If-None-Match match
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetContext_Returns304_WhenIfNoneMatchMatchesETag()
    {
        using var f = new ServerFixture();
        var ctx = new BazaarAgentContext { TickId = 7, IsEnabled = true };
        f.SetSnapshot(new BazaarAgentContextSnapshot(ctx));

        using var http = Http();

        // First request: should be 200 with ETag "\"7\""
        var res1 = await http.GetAsync($"http://127.0.0.1:{f.Port}/v1/context");
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var etag = res1.Headers.ETag;
        Assert.NotNull(etag);

        // Second request with matching If-None-Match: should be 304
        var req2 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{f.Port}/v1/context");
        req2.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag!.Tag, isWeak: false));
        var res2 = await http.SendAsync(req2);
        Assert.Equal(HttpStatusCode.NotModified, res2.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // Case 4: POST 200 round-trip through queue
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PostActions_RoundTripsThroughQueue()
    {
        using var f = new ServerFixture();
        f.SetSnapshot(new BazaarAgentContextSnapshot(WaitContext(tickId: 1)));

        // Background dispatcher: poll queue then respond
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var pending = f.Queue.TryDequeue();
                if (pending is not null)
                {
                    pending.SetResponse(
                        new BazaarAgentServerResponse(200, "{\"ok\":true,\"decisionId\":\"01\"}")
                    );
                    return;
                }
                await Task.Delay(10);
            }
        });

        using var http = Http();
        var res = await http.PostAsync(
            $"http://127.0.0.1:{f.Port}/v1/actions",
            new StringContent("{\"actionKind\":\"Wait\"}", Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\":true", body);
        Assert.Contains("\"decisionId\":\"01\"", body);
    }

    // ---------------------------------------------------------------------------
    // Case 5: POST 400 malformed JSON
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PostActions_Returns400_OnMalformedJson()
    {
        using var f = new ServerFixture();
        f.SetSnapshot(new BazaarAgentContextSnapshot(WaitContext()));

        using var http = Http();
        var res = await http.PostAsync(
            $"http://127.0.0.1:{f.Port}/v1/actions",
            new StringContent("not-json", Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"error\"", body);
        Assert.Contains("\"invalid\"", body);
        Assert.Contains("\"malformed json\"", body);
    }

    // ---------------------------------------------------------------------------
    // Case 6: POST 413 Content-Length too large
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PostActions_Returns413_WhenBodyTooLarge()
    {
        using var f = new ServerFixture();
        f.SetSnapshot(new BazaarAgentContextSnapshot(WaitContext()));

        using var http = Http();
        var content = new ByteArrayContent(new byte[100_000]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var res = await http.PostAsync($"http://127.0.0.1:{f.Port}/v1/actions", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"error\"", body);
        Assert.Contains("\"invalid\"", body);
        Assert.Contains("body too large", body);
    }

    // ---------------------------------------------------------------------------
    // Case 7: POST 503 on queue timeout (short timeout fixture)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PostActions_Returns503_WhenQueueTimesOut()
    {
        using var f = new ServerFixture(timeoutMs: 200);
        f.SetSnapshot(new BazaarAgentContextSnapshot(WaitContext()));
        // Do NOT dequeue — let the timer fire and return 503

        using var http = Http();
        var res = await http.PostAsync(
            $"http://127.0.0.1:{f.Port}/v1/actions",
            new StringContent("{\"actionKind\":\"Wait\"}", Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // Case 8: POST 404 on unknown route
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PostUnknownRoute_Returns404()
    {
        using var f = new ServerFixture();

        using var http = Http();
        var res = await http.PostAsync(
            $"http://127.0.0.1:{f.Port}/v1/missing",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"error\"", body);
        Assert.Contains("\"not-found\"", body);
    }
}
