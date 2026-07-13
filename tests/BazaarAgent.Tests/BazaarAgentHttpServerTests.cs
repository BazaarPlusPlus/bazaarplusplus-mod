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
    private static HttpClient Http() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    // ---------------------------------------------------------------------------
    // Helper: build a minimal valid context with a Wait action available
    // ---------------------------------------------------------------------------

    private static BazaarAgentContext WaitContext(ulong tickId = 1) =>
        new()
        {
            TickId = tickId,
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

    [Fact]
    public async Task Handler_failure_emits_one_correlated_normalized_terminal_event()
    {
        var exception = new InvalidOperationException("snapshot failed");
        using var f = new ServerFixture(snapshotGetter: () => throw exception);

        using var http = Http();
        var res = await http.GetAsync(
            $"http://127.0.0.1:{f.Port}/v1/context?token=must-not-appear"
        );

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.True(f.Logger.WaitForCount(1, TimeSpan.FromSeconds(5)));
        var logEvent = Assert.Single(f.Logger.Events);
        Assert.Equal(BazaarAgentLogSeverity.Error, logEvent.Definition.Severity);
        Assert.Equal("agent.http_request.failed", logEvent.Definition.EventId);
        Assert.Same(exception, logEvent.Exception);
        Assert.Collection(
            logEvent.Values,
            value =>
            {
                var requestId = Assert.IsType<string>(value.Value);
                Assert.Equal(26, requestId.Length);
            },
            value => Assert.Equal("Context", value.Value?.ToString()),
            value => Assert.Equal("Get", value.Value?.ToString()),
            value => Assert.Equal("HttpHandlerException", value.Value?.ToString())
        );
        Assert.DoesNotContain(
            f.Logger.Events.SelectMany(entry => entry.Values),
            value =>
                value.Value?.ToString()?.Contains("must-not-appear", StringComparison.Ordinal)
                == true
        );
    }

    [Fact]
    public async Task Request_id_failure_is_guarded_answers_and_closes_the_request()
    {
        var exception = new InvalidOperationException("rng failed");
        using var f = new ServerFixture(requestIdFactory: () => throw exception);

        using var http = Http();
        var res = await http.GetAsync($"http://127.0.0.1:{f.Port}/v1/context");

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.True(f.Logger.WaitForCount(1, TimeSpan.FromSeconds(5)));
        var logEvent = Assert.Single(f.Logger.Events);
        Assert.Equal("agent.http_request.failed", logEvent.Definition.EventId);
        Assert.Same(exception, logEvent.Exception);
        Assert.Matches("^f[0-9a-f]{7}$", Assert.IsType<string>(logEvent.Values[0].Value));
        Assert.Equal(BazaarAgentHttpLogRoute.Unknown, logEvent.Values[1].Value);
        Assert.Equal(BazaarAgentHttpLogMethod.Other, logEvent.Values[2].Value);
        Assert.Equal(BazaarAgentLogReasonCode.HttpHandlerException, logEvent.Values[3].Value);
    }

    [Fact]
    public async Task Blank_generated_request_ids_keep_distinct_visible_fallback_correlations()
    {
        using var f = new ServerFixture(requestIdFactory: () => " ");
        using var http = Http();

        var first = await http.GetAsync($"http://127.0.0.1:{f.Port}/v1/context");
        var second = await http.GetAsync($"http://127.0.0.1:{f.Port}/v1/context");

        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, second.StatusCode);
        Assert.True(f.Logger.WaitForCount(2, TimeSpan.FromSeconds(5)));
        var events = f.Logger.Events;
        Assert.Equal(2, events.Count);
        var firstId = Assert.IsType<string>(events[0].Values[0].Value);
        var secondId = Assert.IsType<string>(events[1].Values[0].Value);
        Assert.Matches("^f[0-9a-f]{7}$", firstId);
        Assert.Matches("^f[0-9a-f]{7}$", secondId);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void Stop_cleans_listener_after_accept_loop_has_already_marked_it_not_running()
    {
        using var f = new ServerFixture();
        var started = typeof(BazaarAgentHttpServer).GetField(
            "_started",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        var listenerField = typeof(BazaarAgentHttpServer).GetField(
            "_listener",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        Assert.NotNull(started);
        Assert.NotNull(listenerField);
        var listener = Assert.IsType<HttpListener>(listenerField!.GetValue(f.Server));
        started!.SetValue(f.Server, 0);

        var report = f.Server.Stop();

        Assert.Equal(0, report.FailedPhaseCount);
        Assert.False(listener.IsListening);
        Assert.Null(listenerField.GetValue(f.Server));
    }

    [Fact]
    public void Failed_start_cleanup_does_not_report_a_second_stop_failure()
    {
        using var portOwner = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback,
            0
        );
        portOwner.Start();
        var port = ((System.Net.IPEndPoint)portOwner.LocalEndpoint).Port;
        using var queue = new BazaarAgentCommandQueue<BazaarAgentAction>(5000);
        using var replayQueue = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(5000);
        using var server = new BazaarAgentHttpServer(
            port,
            () => null,
            queue,
            replayQueue,
            new CapturingBazaarAgentLogger()
        );

        Assert.ThrowsAny<Exception>(() => server.Start());
        var report = server.Stop();

        Assert.Equal(0, report.FailedPhaseCount);
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Handler_and_fallback_write_failures_emit_one_write_owned_terminal()
    {
        var handlerException = new InvalidOperationException("snapshot failed");
        var writeException = new IOException("output closed");
        using var f = new ServerFixture(
            snapshotGetter: () => throw handlerException,
            errorEnvelopeWriter: (ctx, status, _, _) =>
            {
                ctx.Response.StatusCode = status;
                return writeException;
            }
        );

        using var http = Http();
        var res = await http.GetAsync($"http://127.0.0.1:{f.Port}/v1/context");

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.True(f.Logger.WaitForCount(1, TimeSpan.FromSeconds(5)));
        var logEvent = Assert.Single(f.Logger.Events);
        Assert.Same(writeException, logEvent.Exception);
        Assert.Equal(
            BazaarAgentLogReasonCode.HttpErrorResponseWriteException,
            logEvent.Values[3].Value
        );
    }

    [Fact]
    public async Task Body_read_and_fallback_write_failures_emit_one_write_owned_terminal()
    {
        var readException = new IOException("request reset");
        var writeException = new IOException("response reset");
        using var f = new ServerFixture(
            requestBodyReaderOverride: (_, _, _, _) => Task.FromException<byte[]?>(readException),
            errorEnvelopeWriter: (ctx, status, _, _) =>
            {
                ctx.Response.StatusCode = status;
                return writeException;
            }
        );

        using var http = Http();
        using var content = new ByteArrayContent(new byte[] { 1 });
        var res = await http.PostAsync($"http://127.0.0.1:{f.Port}/v1/actions", content);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True(f.Logger.WaitForCount(1, TimeSpan.FromSeconds(5)));
        var logEvent = Assert.Single(f.Logger.Events);
        Assert.Same(writeException, logEvent.Exception);
        Assert.Equal(
            BazaarAgentLogReasonCode.HttpErrorResponseWriteException,
            logEvent.Values[3].Value
        );
    }

    [Fact]
    public void Unexpected_accept_loop_death_emits_one_terminal_and_does_not_self_heal()
    {
        using var f = new ServerFixture();
        var listenerField = typeof(BazaarAgentHttpServer).GetField(
            "_listener",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        Assert.NotNull(listenerField);
        var listener = Assert.IsType<HttpListener>(listenerField!.GetValue(f.Server));

        listener.Abort();

        Assert.True(f.Logger.WaitForCount(1, TimeSpan.FromSeconds(5)));
        var logEvent = Assert.Single(f.Logger.Events);
        Assert.Equal("agent.listener.failed", logEvent.Definition.EventId);
        Assert.Equal(BazaarAgentLogSeverity.Error, logEvent.Definition.Severity);
        Assert.False(f.Server.IsRunning);
        System.Threading.Thread.Sleep(100);
        Assert.Single(f.Logger.Events);
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
        };
        f.SetSnapshot(new BazaarAgentContextSnapshot(ctx));

        using var http = Http();
        var res = await http.GetAsync($"http://127.0.0.1:{f.Port}/v1/context");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("\"42\"", res.Headers.ETag?.ToString());
        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"schemaVersion\":\"2.2.0\"", body);
        Assert.Contains("\"tickId\":42", body);
        Assert.Contains("\"stateName\":\"Choice\"", body);
        Assert.DoesNotContain("\"isEnabled\"", body);
    }

    // ---------------------------------------------------------------------------
    // Case 3: GET 304 on If-None-Match match
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetContext_Returns304_WhenIfNoneMatchMatchesETag()
    {
        using var f = new ServerFixture();
        var ctx = new BazaarAgentContext { TickId = 7 };
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
    // Case 7b: GET serializes replayPhase as camelCase wire values
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetContext_SerializesReplayPhaseCamelCase()
    {
        using var f = new ServerFixture();
        var ctx = new BazaarAgentContext
        {
            TickId = 5,
            ReplayPhase = BazaarAgentReplayPhase.FinishedAwaitingContinue,
            ReplayBattleId = "battle-123",
        };
        f.SetSnapshot(new BazaarAgentContextSnapshot(ctx));

        using var http = Http();
        var res = await http.GetAsync($"http://127.0.0.1:{f.Port}/v1/context");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"replayPhase\":\"finishedAwaitingContinue\"", body);
        Assert.Contains("\"replayBattleId\":\"battle-123\"", body);
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
