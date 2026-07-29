#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentActivityEvent
{
    public long Sequence { get; init; }
    public string TimestampUtc { get; init; } = "";
    public string Kind { get; init; } = "";
    public string RequestId { get; init; } = "";
    public string Route { get; init; } = "";
    public int? StatusCode { get; init; }
    public ulong? TickId { get; init; }
    public string Summary { get; init; } = "";
    public string? RequestJson { get; init; }
    public string? ResponseJson { get; init; }
}

public sealed class BazaarAgentActivitySnapshot
{
    public BazaarAgentActivitySnapshot(
        long earliestSequence,
        long latestSequence,
        IReadOnlyList<BazaarAgentActivityEvent> events
    )
    {
        EarliestSequence = earliestSequence;
        LatestSequence = latestSequence;
        Events = events;
    }

    public long EarliestSequence { get; }
    public long LatestSequence { get; }
    public IReadOnlyList<BazaarAgentActivityEvent> Events { get; }
}

/// <summary>
/// A bounded, in-memory record of the protocol traffic that a local dashboard can safely inspect.
/// Publication does not wait for a browser connection, and the feed intentionally has no disk
/// persistence: existing decision logs remain the durable audit artifact.
/// </summary>
public sealed class BazaarAgentActivityFeed
{
    private const int MaximumEvents = 512;
    private const int MaximumPayloadCharacters = 64 * 1024;
    private const int MaximumBufferedPayloadCharacters = 4 * 1024 * 1024;
    private readonly object _sync = new();
    private readonly List<BazaarAgentActivityEvent> _events = new();
    private TaskCompletionSource<long> _nextPublication = NewPublicationSignal();
    private long _latestSequence;
    private int _bufferedPayloadCharacters;

    public void Publish(
        string kind,
        string requestId,
        string route,
        string summary,
        int? statusCode = null,
        ulong? tickId = null,
        string? requestJson = null,
        string? responseJson = null
    )
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("An activity kind is required.", nameof(kind));

        BazaarAgentActivityEvent activity;
        TaskCompletionSource<long> signal;
        lock (_sync)
        {
            var sequence = checked(_latestSequence + 1);
            _latestSequence = sequence;
            activity = new BazaarAgentActivityEvent
            {
                Sequence = sequence,
                TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Kind = kind,
                RequestId = requestId ?? "",
                Route = route ?? "",
                StatusCode = statusCode,
                TickId = tickId,
                Summary = summary ?? "",
                RequestJson = TrimPayload(requestJson),
                ResponseJson = TrimPayload(responseJson),
            };
            _events.Add(activity);
            _bufferedPayloadCharacters += PayloadCharacterCount(activity);
            while (
                _events.Count > MaximumEvents
                || (
                    _events.Count > 1
                    && _bufferedPayloadCharacters > MaximumBufferedPayloadCharacters
                )
            )
            {
                var evicted = _events[0];
                _events.RemoveAt(0);
                _bufferedPayloadCharacters -= PayloadCharacterCount(evicted);
            }

            signal = _nextPublication;
            _nextPublication = NewPublicationSignal();
        }

        signal.TrySetResult(activity.Sequence);
    }

    public BazaarAgentActivitySnapshot GetSince(long afterSequence, int maximumEvents)
    {
        if (maximumEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEvents));

        lock (_sync)
        {
            var startIndex = _events.FindIndex(activity => activity.Sequence > afterSequence);
            var result =
                startIndex < 0
                    ? Array.Empty<BazaarAgentActivityEvent>()
                    : _events.Skip(startIndex).Take(maximumEvents).ToArray();
            var earliest = _events.Count == 0 ? _latestSequence : _events[0].Sequence;
            return new BazaarAgentActivitySnapshot(earliest, _latestSequence, result);
        }
    }

    public async Task<BazaarAgentActivitySnapshot> WaitForEventsAsync(
        long afterSequence,
        int maximumEvents,
        int waitMilliseconds
    )
    {
        var immediate = GetSince(afterSequence, maximumEvents);
        if (immediate.Events.Count > 0 || waitMilliseconds <= 0)
            return immediate;

        Task<long> signal;
        lock (_sync)
        {
            var refreshed = GetSinceUnsafe(afterSequence, maximumEvents);
            if (refreshed.Events.Count > 0)
                return refreshed;
            signal = _nextPublication.Task;
        }

        await Task.WhenAny(signal, Task.Delay(waitMilliseconds)).ConfigureAwait(false);
        return GetSince(afterSequence, maximumEvents);
    }

    private BazaarAgentActivitySnapshot GetSinceUnsafe(long afterSequence, int maximumEvents)
    {
        var startIndex = _events.FindIndex(activity => activity.Sequence > afterSequence);
        var result =
            startIndex < 0
                ? Array.Empty<BazaarAgentActivityEvent>()
                : _events.Skip(startIndex).Take(maximumEvents).ToArray();
        var earliest = _events.Count == 0 ? _latestSequence : _events[0].Sequence;
        return new BazaarAgentActivitySnapshot(earliest, _latestSequence, result);
    }

    private static TaskCompletionSource<long> NewPublicationSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string? TrimPayload(string? payload)
    {
        if (payload is null || payload.Length <= MaximumPayloadCharacters)
            return payload;
        return payload[..MaximumPayloadCharacters] + "\n[truncated by BazaarAgent activity feed]";
    }

    private static int PayloadCharacterCount(BazaarAgentActivityEvent activity) =>
        (activity.RequestJson?.Length ?? 0) + (activity.ResponseJson?.Length ?? 0);
}
