#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentHttpServer : IDisposable
{
    private const int MaxBodyBytes = 65536;

    private static readonly JsonSerializerSettings _json = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    private readonly Func<BazaarAgentContextSnapshot?> _snapshotGetter;
    private readonly BazaarAgentActionQueue _queue;
    private readonly BazaarAgentReplayControlQueue _replayQueue;
    private readonly IBazaarAgentLogger _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _started;

    public int Port { get; }
    public bool IsRunning { get; private set; }

    public BazaarAgentHttpServer(
        int port,
        Func<BazaarAgentContextSnapshot?> snapshotGetter,
        BazaarAgentActionQueue queue,
        BazaarAgentReplayControlQueue replayQueue,
        IBazaarAgentLogger logger
    )
    {
        Port = port;
        _snapshotGetter = snapshotGetter;
        _queue = queue;
        _replayQueue = replayQueue;
        _logger = logger;
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => AcceptLoop(token));

        IsRunning = true;
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
            return;
        IsRunning = false;
        try
        {
            _cts?.Cancel();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Listener cancellation failed: {FormatException(ex)}");
        }
        try
        {
            _listener?.Stop();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Listener stop failed: {FormatException(ex)}");
        }
        try
        {
            _listener?.Close();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Listener close failed: {FormatException(ex)}");
        }
        _listener = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(CancellationToken token)
    {
        var listener = _listener;
        if (listener is null)
            return;
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    IsRunning = false;
                    Interlocked.Exchange(ref _started, 0);
                    _logger.Warning($"Listener accept loop stopped: {FormatException(ex)}");
                }
                return;
            }
            _ = HandleContextAsync(ctx);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            var method = ctx.Request.HttpMethod;
            if (
                string.Equals(path, "/v1/context", StringComparison.OrdinalIgnoreCase)
                && method == "GET"
            )
            {
                await HandleGetContext(ctx).ConfigureAwait(false);
            }
            else if (
                string.Equals(path, "/v1/actions", StringComparison.OrdinalIgnoreCase)
                && method == "POST"
            )
            {
                await HandlePostActions(ctx).ConfigureAwait(false);
            }
            else if (
                string.Equals(path, "/v1/replay/record", StringComparison.OrdinalIgnoreCase)
                && method == "POST"
            )
            {
                await HandlePostReplayRecord(ctx).ConfigureAwait(false);
            }
            else if (
                string.Equals(path, "/v1/replay/continue", StringComparison.OrdinalIgnoreCase)
                && method == "POST"
            )
            {
                await HandlePostReplayContinue(ctx).ConfigureAwait(false);
            }
            else
            {
                WriteErrorEnvelope(ctx, 404, "not-found", "unknown route");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"HTTP handler threw on {ctx.Request.Url?.AbsolutePath}", ex);
            try
            {
                WriteErrorEnvelope(ctx, 500, "internal", ex.GetType().Name);
            }
            catch (Exception writeEx)
            {
                _logger.Warning($"Failed to write HTTP error envelope: {FormatException(writeEx)}");
            }
        }
        finally
        {
            try
            {
                ctx.Response.Close();
            }
            catch (Exception closeEx)
            {
                _logger.Warning($"Failed to close HTTP response: {FormatException(closeEx)}");
            }
        }
    }

    private async Task HandleGetContext(HttpListenerContext ctx)
    {
        var snap = _snapshotGetter();
        if (snap is null)
        {
            WriteErrorEnvelope(ctx, 503, "unavailable", null);
            return;
        }

        var inm = ctx.Request.Headers["If-None-Match"];
        if (inm == snap.ETag)
        {
            ctx.Response.StatusCode = 304;
            return;
        }

        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["ETag"] = snap.ETag;
        var body = JsonConvert.SerializeObject(snap.Context, _json);
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private async Task HandlePostActions(HttpListenerContext ctx)
    {
        var body = await ReadBodyWithCap(ctx, MaxBodyBytes).ConfigureAwait(false);
        if (body is null)
            return;

        BazaarAgentAction? action;
        try
        {
            var json = Encoding.UTF8.GetString(body);
            action = JsonConvert.DeserializeObject<BazaarAgentAction>(json, _json);
        }
        catch (JsonException)
        {
            WriteErrorEnvelope(ctx, 400, "invalid", "malformed json");
            return;
        }

        if (action is null)
        {
            WriteErrorEnvelope(ctx, 400, "invalid", "empty body");
            return;
        }

        var res = await _queue.EnqueueAndAwaitAsync(action).ConfigureAwait(false);
        await WriteQueueResponse(ctx, res).ConfigureAwait(false);
    }

    private async Task HandlePostReplayRecord(HttpListenerContext ctx)
    {
        // Raw binary route: the body is a GhostBattlePayload msgpack+gzip blob, never JSON.
        // It gets its own (much larger) cap and bypasses the action parser entirely.
        var body = await ReadBodyWithCap(ctx, BazaarAgentRuntimeDefaults.MaxRecordBodyBytes)
            .ConfigureAwait(false);
        if (body is null)
            return;

        if (body.Length == 0)
        {
            WriteErrorEnvelope(ctx, 400, "invalid", "empty body");
            return;
        }

        var battleId = ctx.Request.Headers["X-Bpp-Battle-Id"];
        if (string.IsNullOrWhiteSpace(battleId))
            battleId = ctx.Request.QueryString["battleId"];

        var res = await _replayQueue
            .EnqueueAndAwaitAsync(BazaarAgentReplayControlKind.Start, body, battleId)
            .ConfigureAwait(false);
        await WriteQueueResponse(ctx, res).ConfigureAwait(false);
    }

    private async Task HandlePostReplayContinue(HttpListenerContext ctx)
    {
        var res = await _replayQueue
            .EnqueueAndAwaitAsync(BazaarAgentReplayControlKind.Continue, null, null)
            .ConfigureAwait(false);
        await WriteQueueResponse(ctx, res).ConfigureAwait(false);
    }

    /// <summary>Reads the request body up to <paramref name="maxBytes"/>. Returns <c>null</c>
    /// after writing a 413/400 error envelope when the cap is exceeded or the read fails.</summary>
    private async Task<byte[]?> ReadBodyWithCap(HttpListenerContext ctx, int maxBytes)
    {
        // Pre-check Content-Length header
        var declaredHeader = ctx.Request.Headers["Content-Length"];
        if (long.TryParse(declaredHeader, out var declared) && declared > maxBytes)
        {
            WriteErrorEnvelope(ctx, 413, "invalid", "body too large");
            return null;
        }

        // Stream-read with size cap
        try
        {
            using var ms = new MemoryStream();
            var buf = new byte[8192];
            var total = 0;
            while (true)
            {
                var read = await ctx
                    .Request.InputStream.ReadAsync(buf, 0, buf.Length)
                    .ConfigureAwait(false);
                if (read <= 0)
                    break;
                total += read;
                if (total > maxBytes)
                {
                    WriteErrorEnvelope(ctx, 413, "invalid", "body too large");
                    return null;
                }
                ms.Write(buf, 0, read);
            }
            return ms.ToArray();
        }
        catch (IOException ex)
        {
            _logger.Error("POST body read failed", ex);
            WriteErrorEnvelope(ctx, 400, "invalid", "read failed");
            return null;
        }
    }

    private static async Task WriteQueueResponse(
        HttpListenerContext ctx,
        BazaarAgentServerResponse res
    )
    {
        ctx.Response.StatusCode = res.HttpStatus;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(res.JsonBody);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private void WriteErrorEnvelope(
        HttpListenerContext ctx,
        int status,
        string code,
        string? details
    )
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var envelope = new Dictionary<string, object?> { ["error"] = code };
        if (details is not null)
            envelope["details"] = details;
        var json = JsonConvert.SerializeObject(envelope, _json);
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to write HTTP error envelope: {FormatException(ex)}");
        }
    }

    private static string FormatException(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
