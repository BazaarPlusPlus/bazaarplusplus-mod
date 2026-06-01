#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BazaarPlusPlus.AutoBazaar;

public sealed class AutoBazaarServerResponse
{
    public int HttpStatus { get; }
    public string JsonBody { get; }

    public AutoBazaarServerResponse(int status, string body)
    {
        HttpStatus = status;
        JsonBody = body;
    }
}

public sealed class PendingAction
{
    private readonly TaskCompletionSource<AutoBazaarServerResponse> _tcs = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int _completed;
    private volatile bool _discardedByTimer;
    private Timer? _timer;

    public AutoBazaarAction Action { get; }
    public Task<AutoBazaarServerResponse> ResponseTask => _tcs.Task;
    public bool IsDiscarded => _discardedByTimer;

    internal PendingAction(AutoBazaarAction action, int timeoutMilliseconds)
    {
        Action = action;
        _timer = new Timer(TimeoutCallback, null, timeoutMilliseconds, Timeout.Infinite);
    }

    private void TimeoutCallback(object? _)
    {
        _discardedByTimer = true;
        SetResponse(new AutoBazaarServerResponse(503, "{\"error\":\"unavailable\"}"));
    }

    public void SetResponse(AutoBazaarServerResponse response)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
            return;
        try
        {
            _timer?.Dispose();
        }
        catch { }
        _timer = null;
        _tcs.TrySetResult(response);
    }
}

public sealed class AutoBazaarActionQueue : IDisposable
{
    private readonly ConcurrentQueue<PendingAction> _queue = new();
    private readonly int _timeoutMs;
    private int _disposed;

    public AutoBazaarActionQueue(int timeoutMilliseconds) => _timeoutMs = timeoutMilliseconds;

    public Task<AutoBazaarServerResponse> EnqueueAndAwaitAsync(AutoBazaarAction action)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return Task.FromResult(
                new AutoBazaarServerResponse(503, "{\"error\":\"unavailable\"}")
            );
        var p = new PendingAction(action, _timeoutMs);
        _queue.Enqueue(p);
        return p.ResponseTask;
    }

    public PendingAction? TryDequeue()
    {
        while (_queue.TryDequeue(out var p))
        {
            if (p.IsDiscarded)
                continue;
            return p;
        }
        return null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        while (_queue.TryDequeue(out var p))
        {
            p.SetResponse(new AutoBazaarServerResponse(503, "{\"error\":\"unavailable\"}"));
        }
    }
}
