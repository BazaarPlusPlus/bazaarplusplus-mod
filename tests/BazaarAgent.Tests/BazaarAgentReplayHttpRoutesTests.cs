#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public class BazaarAgentReplayHttpRoutesTests
{
    private static HttpClient Http() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Pumps the replay queue on a background task like the controller tick would,
    /// completing the first dequeued command with <paramref name="respond"/>.</summary>
    private static void PumpReplayQueueOnce(
        ServerFixture f,
        Func<BazaarAgentPendingCommand<BazaarAgentReplayCommand>, BazaarAgentServerResponse> respond
    )
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var pending = f.ReplayQueue.TryDequeue();
                if (pending is not null)
                {
                    pending.SetResponse(respond(pending));
                    return;
                }
                await Task.Delay(10);
            }
        });
    }

    [Fact]
    public async Task PostReplayRecord_RoundTripsRawBytesAndBattleId()
    {
        using var f = new ServerFixture();
        var payload = new byte[] { 0x1F, 0x8B, 0x01, 0x02, 0x03 };

        BazaarAgentPendingCommand<BazaarAgentReplayCommand>? seen = null;
        PumpReplayQueueOnce(
            f,
            pending =>
            {
                seen = pending;
                return BazaarAgentReplayControlProcessor.MapOutcome(
                    pending.Command.Kind,
                    new BazaarAgentReplayControlOutcome(
                        BazaarAgentReplayControlStatus.Accepted,
                        null,
                        pending.Command.BattleId
                    )
                );
            }
        );

        using var http = Http();
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/x-bpp-ghostbattle+msgpack+gzip"
        );
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{f.Port}/v1/replay/record"
        )
        {
            Content = content,
        };
        req.Headers.Add("X-Bpp-Battle-Id", "battle-42");

        var res = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"accepted\":true", body);
        Assert.Contains("\"battleId\":\"battle-42\"", body);
        Assert.Contains("\"status\":\"recording-started\"", body);

        Assert.NotNull(seen);
        Assert.Equal(BazaarAgentReplayControlKind.Start, seen!.Command.Kind);
        Assert.Equal(payload, seen.Command.Payload);
        Assert.Equal("battle-42", seen.Command.BattleId);
    }

    [Fact]
    public async Task PostReplayRecord_AcceptsBattleIdFromQueryString()
    {
        using var f = new ServerFixture();

        BazaarAgentPendingCommand<BazaarAgentReplayCommand>? seen = null;
        PumpReplayQueueOnce(
            f,
            pending =>
            {
                seen = pending;
                return new BazaarAgentServerResponse(202, "{}");
            }
        );

        using var http = Http();
        var res = await http.PostAsync(
            $"http://127.0.0.1:{f.Port}/v1/replay/record?battleId=battle-q",
            new ByteArrayContent(new byte[] { 0x01 })
        );
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal("battle-q", seen!.Command.BattleId);
    }

    [Fact]
    public async Task PostReplayRecord_Returns400_OnEmptyBody()
    {
        using var f = new ServerFixture();

        using var http = Http();
        var res = await http.PostAsync(
            $"http://127.0.0.1:{f.Port}/v1/replay/record",
            new ByteArrayContent(Array.Empty<byte>())
        );
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"invalid\"", body);
        Assert.Contains("empty body", body);
    }

    [Fact]
    public async Task PostReplayRecord_Returns413_WhenDeclaredLengthExceedsRecordCap()
    {
        using var f = new ServerFixture();

        // Raw socket: declare an over-cap Content-Length without sending the body — the server
        // must reject from the header pre-check alone (an HttpClient streaming the full body
        // races the early close and dies on a broken pipe instead of reading the 413).
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, f.Port);
        using var stream = tcp.GetStream();
        var declared = (long)BazaarAgentRuntimeDefaults.MaxRecordBodyBytes + 1;
        var request =
            $"POST /v1/replay/record HTTP/1.1\r\n"
            + $"Host: 127.0.0.1:{f.Port}\r\n"
            + $"Content-Length: {declared}\r\n"
            + "\r\n";
        var requestBytes = System.Text.Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
        await stream.FlushAsync();

        using var reader = new System.IO.StreamReader(stream);
        var statusLine = await reader.ReadLineAsync();
        Assert.NotNull(statusLine);
        Assert.Contains("413", statusLine);
    }

    [Fact]
    public async Task PostReplayRecord_AllowsBodiesAboveActionCap()
    {
        // The record route must not inherit the 64 KB action-body cap.
        using var f = new ServerFixture();

        PumpReplayQueueOnce(f, _ => new BazaarAgentServerResponse(202, "{}"));

        using var http = Http();
        var res = await http.PostAsync(
            $"http://127.0.0.1:{f.Port}/v1/replay/record",
            new ByteArrayContent(new byte[100_000])
        );
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
    }

    [Fact]
    public async Task PostReplayContinue_RoundTripsThroughQueue()
    {
        using var f = new ServerFixture();

        BazaarAgentPendingCommand<BazaarAgentReplayCommand>? seen = null;
        PumpReplayQueueOnce(
            f,
            pending =>
            {
                seen = pending;
                return BazaarAgentReplayControlProcessor.MapOutcome(
                    pending.Command.Kind,
                    new BazaarAgentReplayControlOutcome(
                        BazaarAgentReplayControlStatus.Accepted,
                        null,
                        null
                    )
                );
            }
        );

        using var http = Http();
        var res = await http.PostAsync(
            $"http://127.0.0.1:{f.Port}/v1/replay/continue",
            new ByteArrayContent(Array.Empty<byte>())
        );
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"accepted\":true", body);
        Assert.Contains("\"status\":\"continue-triggered\"", body);

        Assert.NotNull(seen);
        Assert.Equal(BazaarAgentReplayControlKind.Continue, seen!.Command.Kind);
        Assert.Null(seen.Command.Payload);
    }

    [Fact]
    public async Task PostReplayRecord_Returns503_WhenQueueTimesOut()
    {
        using var f = new ServerFixture(replayTimeoutMs: 200);
        // No pump — let the timer fire.

        using var http = Http();
        var res = await http.PostAsync(
            $"http://127.0.0.1:{f.Port}/v1/replay/record",
            new ByteArrayContent(new byte[] { 0x01 })
        );
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }
}
