#nullable enable
using System;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal readonly struct CollectionSourcePoolRetryScheduleResult
{
    private CollectionSourcePoolRetryScheduleResult(bool isExhausted, bool shouldLogWarning)
    {
        IsExhausted = isExhausted;
        ShouldLogWarning = shouldLogWarning;
    }

    public bool IsExhausted { get; }

    public bool ShouldLogWarning { get; }

    public static CollectionSourcePoolRetryScheduleResult Scheduled() => new(false, false);

    public static CollectionSourcePoolRetryScheduleResult Exhausted(bool shouldLogWarning) =>
        new(true, shouldLogWarning);
}

internal sealed class CollectionSourcePoolRetryState
{
    private string? _pendingKey;
    private float _nextRetryAt = float.NaN;
    private int _attempts;
    private bool _warningLogged;

    public string? PendingKey => _pendingKey;

    public float NextRetryAt => _nextRetryAt;

    public int Attempts => _attempts;

    public bool WarningLogged => _warningLogged;

    public CollectionSourcePoolRetryScheduleResult Schedule(
        string cacheKey,
        float now,
        float retrySeconds,
        int maxAttempts
    )
    {
        if (!string.Equals(_pendingKey, cacheKey, StringComparison.Ordinal))
        {
            _pendingKey = cacheKey;
            _attempts = 0;
            _warningLogged = false;
        }

        if (_attempts >= maxAttempts)
        {
            _nextRetryAt = float.NaN;
            var shouldLog = !_warningLogged;
            _warningLogged = true;
            return CollectionSourcePoolRetryScheduleResult.Exhausted(shouldLog);
        }

        _nextRetryAt = now + retrySeconds;
        return CollectionSourcePoolRetryScheduleResult.Scheduled();
    }

    public bool TryConsumeDueRetry(float now)
    {
        if (float.IsNaN(_nextRetryAt))
            return false;
        if (now < _nextRetryAt)
            return false;

        _nextRetryAt = float.NaN;
        _attempts++;
        return true;
    }

    public void Reset()
    {
        _pendingKey = null;
        _nextRetryAt = float.NaN;
        _attempts = 0;
        _warningLogged = false;
    }
}
