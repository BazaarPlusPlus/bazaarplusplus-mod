#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal sealed class AutoBazaarHttpServer : IDisposable
{
    private const int MaxBodyBytes = 65536;

    private static readonly JsonSerializerSettings _json = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    private readonly string _endpointJsonPath;
    private readonly Func<AutoBazaarContextSnapshot?> _snapshotGetter;
    private readonly AutoBazaarActionQueue _queue;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _started;

    public int Port { get; }
    public bool IsRunning { get; private set; }

    public AutoBazaarHttpServer(
        int port,
        string endpointJsonPath,
        Func<AutoBazaarContextSnapshot?> snapshotGetter,
        AutoBazaarActionQueue queue)
    {
        Port = port;
        _endpointJsonPath = endpointJsonPath;
        _snapshotGetter = snapshotGetter;
        _queue = queue;
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => AcceptLoop(token));

        try { WriteEndpointJson(); }
        catch (Exception ex) { BppLog.Error("AutoBazaar", "endpoint.json write failed", ex); }

        IsRunning = true;
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0) return;
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        try { DeleteEndpointJson(); } catch (Exception ex) { BppLog.Error("AutoBazaar", "endpoint.json delete failed", ex); }
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(CancellationToken token)
    {
        var listener = _listener;
        if (listener is null) return;
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { return; }
            _ = HandleContextAsync(ctx);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            var method = ctx.Request.HttpMethod;
            if (string.Equals(path, "/v1/context", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                await HandleGetContext(ctx).ConfigureAwait(false);
            }
            else if (string.Equals(path, "/v1/actions", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                await HandlePostActions(ctx).ConfigureAwait(false);
            }
            else
            {
                WriteErrorEnvelope(ctx, 404, "not-found", "unknown route");
            }
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", $"HTTP handler threw on {ctx.Request.Url?.AbsolutePath}", ex);
            try { WriteErrorEnvelope(ctx, 500, "internal", ex.GetType().Name); } catch { }
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
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
        // Pre-check Content-Length header
        var declaredHeader = ctx.Request.Headers["Content-Length"];
        if (long.TryParse(declaredHeader, out var declared) && declared > MaxBodyBytes)
        {
            WriteErrorEnvelope(ctx, 413, "invalid", "body too large");
            return;
        }

        // Stream-read with size cap
        byte[] body;
        try
        {
            using var ms = new MemoryStream();
            var buf = new byte[8192];
            var total = 0;
            while (true)
            {
                var read = await ctx.Request.InputStream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                if (read <= 0) break;
                total += read;
                if (total > MaxBodyBytes)
                {
                    WriteErrorEnvelope(ctx, 413, "invalid", "body too large");
                    return;
                }
                ms.Write(buf, 0, read);
            }
            body = ms.ToArray();
        }
        catch (IOException ex)
        {
            BppLog.Error("AutoBazaar", "POST body read failed", ex);
            WriteErrorEnvelope(ctx, 400, "invalid", "read failed");
            return;
        }

        AutoBazaarAction? action;
        try
        {
            var json = Encoding.UTF8.GetString(body);
            action = JsonConvert.DeserializeObject<AutoBazaarAction>(json, _json);
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
        ctx.Response.StatusCode = res.HttpStatus;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(res.JsonBody);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private void WriteErrorEnvelope(HttpListenerContext ctx, int status, string code, string? details)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var envelope = new Dictionary<string, object?> { ["error"] = code };
        if (details is not null) envelope["details"] = details;
        var json = JsonConvert.SerializeObject(envelope, _json);
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch { }
    }

    private void WriteEndpointJson()
    {
        var dir = Path.GetDirectoryName(_endpointJsonPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var payload = new
        {
            baseUrl = $"http://127.0.0.1:{Port}",
            schemaVersion = AutoBazaarSchema.Version,
            pid = System.Diagnostics.Process.GetCurrentProcess().Id,
        };
        var json = JsonConvert.SerializeObject(payload, _json);
        var tmp = _endpointJsonPath + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try { File.Delete(_endpointJsonPath); } catch { }
        File.Move(tmp, _endpointJsonPath);
    }

    private void DeleteEndpointJson()
    {
        try { File.Delete(_endpointJsonPath); } catch (IOException) { /* ok */ }
    }
}
