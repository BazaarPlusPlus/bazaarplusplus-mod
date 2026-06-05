#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentServerResponse
{
    public int HttpStatus { get; }
    public string JsonBody { get; }

    public BazaarAgentServerResponse(int status, string body)
    {
        HttpStatus = status;
        JsonBody = body;
    }
}

public sealed class PendingAction
{
    private readonly TaskCompletionSource<BazaarAgentServerResponse> _tcs = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int _completed;
    private volatile bool _discardedByTimer;
    private Timer? _timer;

    public BazaarAgentAction Action { get; }
    public Task<BazaarAgentServerResponse> ResponseTask => _tcs.Task;
    public bool IsDiscarded => _discardedByTimer;

    internal PendingAction(BazaarAgentAction action, int timeoutMilliseconds)
    {
        Action = action;
        _timer = new Timer(TimeoutCallback, null, timeoutMilliseconds, Timeout.Infinite);
    }

    private void TimeoutCallback(object? _)
    {
        _discardedByTimer = true;
        SetResponse(new BazaarAgentServerResponse(503, "{\"error\":\"unavailable\"}"));
    }

    public void SetResponse(BazaarAgentServerResponse response)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
            return;
        try
        {
            _timer?.Dispose();
        }
        catch
        {
            // Best-effort cleanup; completion is already published and the timer is collectible.
        }
        _timer = null;
        _tcs.TrySetResult(response);
    }
}

public sealed class BazaarAgentActionQueue : IDisposable
{
    private readonly ConcurrentQueue<PendingAction> _queue = new();
    private readonly int _timeoutMs;
    private int _disposed;

    public BazaarAgentActionQueue(int timeoutMilliseconds) => _timeoutMs = timeoutMilliseconds;

    public Task<BazaarAgentServerResponse> EnqueueAndAwaitAsync(BazaarAgentAction action)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return Task.FromResult(
                new BazaarAgentServerResponse(503, "{\"error\":\"unavailable\"}")
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
            p.SetResponse(new BazaarAgentServerResponse(503, "{\"error\":\"unavailable\"}"));
        }
    }
}
