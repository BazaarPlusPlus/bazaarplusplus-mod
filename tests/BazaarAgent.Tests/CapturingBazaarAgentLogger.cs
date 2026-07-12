#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.BazaarAgent;

internal sealed class CapturingBazaarAgentLogger : IBazaarAgentLogger
{
    private readonly System.Action<BazaarAgentLogEvent>? _onEmit;
    private readonly object _gate = new();
    private readonly List<BazaarAgentLogEvent> _events = new();

    public CapturingBazaarAgentLogger(System.Action<BazaarAgentLogEvent>? onEmit = null) =>
        _onEmit = onEmit;

    public IReadOnlyList<BazaarAgentLogEvent> Events
    {
        get
        {
            lock (_gate)
                return _events.ToArray();
        }
    }

    public void Emit(BazaarAgentLogEvent logEvent)
    {
        lock (_gate)
        {
            _events.Add(logEvent);
            System.Threading.Monitor.PulseAll(_gate);
        }
        _onEmit?.Invoke(logEvent);
    }

    public bool WaitForCount(int count, System.TimeSpan timeout)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        lock (_gate)
        {
            while (_events.Count < count)
            {
                var remaining = timeout - elapsed.Elapsed;
                if (remaining <= System.TimeSpan.Zero)
                    return false;
                if (!System.Threading.Monitor.Wait(_gate, remaining))
                    return _events.Count >= count;
            }
            return true;
        }
    }
}
