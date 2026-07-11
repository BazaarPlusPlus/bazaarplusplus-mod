#nullable enable
using System;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotGate
{
    private const int MaxCaptureAttempts = 2;
    private bool _armed;
    private bool _captureAttemptInFlight;
    private bool _finishedForCurrentRun;
    private bool _capturedForCurrentRun;
    private bool _summaryObserved;
    private bool _summaryRevealStarted;
    private int _failedCaptureAttempts;
    private float _retryAvailableAtSeconds;
    private float _fallbackAtSeconds = -1f;

    public void ArmForEndOfRun()
    {
        if (_finishedForCurrentRun || _armed)
            return;

        _armed = true;
        _summaryObserved = false;
        _summaryRevealStarted = false;
        _failedCaptureAttempts = 0;
        _retryAvailableAtSeconds = 0f;
        _fallbackAtSeconds = -1f;
    }

    public bool TryBeginAutomaticCapture(
        EndOfRunCaptureReadinessState readiness,
        bool isCaptureEnabled,
        float nowSeconds,
        float fallbackTimeoutSeconds
    )
    {
        if (
            !isCaptureEnabled
            || !_armed
            || _finishedForCurrentRun
            || _captureAttemptInFlight
            || nowSeconds < _retryAvailableAtSeconds
        )
        {
            return false;
        }

        ObserveReadiness(readiness, nowSeconds, fallbackTimeoutSeconds);
        if (_finishedForCurrentRun || readiness == EndOfRunCaptureReadinessState.NotSummary)
            return false;

        var shouldCapture = readiness == EndOfRunCaptureReadinessState.Ready;
        if (!shouldCapture && readiness == EndOfRunCaptureReadinessState.DetectionFailed)
        {
            shouldCapture = _fallbackAtSeconds >= 0f && nowSeconds >= _fallbackAtSeconds;
        }

        if (!shouldCapture)
            return false;

        _captureAttemptInFlight = true;
        return true;
    }

    public bool ShouldBlockContinue(
        EndOfRunCaptureReadinessState readiness,
        bool isCaptureEnabled,
        float nowSeconds,
        float fallbackTimeoutSeconds
    )
    {
        if (!isCaptureEnabled || !_armed || _finishedForCurrentRun)
            return false;
        if (_captureAttemptInFlight)
            return true;

        ObserveReadiness(readiness, nowSeconds, fallbackTimeoutSeconds);
        if (_finishedForCurrentRun)
            return false;

        return readiness
            is EndOfRunCaptureReadinessState.RevealNotStarted
                or EndOfRunCaptureReadinessState.RevealInProgress
                or EndOfRunCaptureReadinessState.Ready
                or EndOfRunCaptureReadinessState.DetectionFailed;
    }

    public void MarkSummaryRevealStarted()
    {
        if (_armed && !_finishedForCurrentRun)
        {
            _summaryRevealStarted = true;
            _fallbackAtSeconds = -1f;
        }
    }

    public void CompleteCaptureAttempt()
    {
        _captureAttemptInFlight = false;
        _armed = false;
        _finishedForCurrentRun = true;
        _capturedForCurrentRun = true;
        _retryAvailableAtSeconds = 0f;
        _fallbackAtSeconds = -1f;
    }

    public bool AbortCaptureAttempt(float retryAvailableAtSeconds)
    {
        _captureAttemptInFlight = false;
        _failedCaptureAttempts++;
        if (_failedCaptureAttempts >= MaxCaptureAttempts)
        {
            FailOpen();
            return false;
        }

        _retryAvailableAtSeconds = retryAvailableAtSeconds;
        return true;
    }

    public void CancelCaptureAttempt()
    {
        _captureAttemptInFlight = false;
    }

    public void Disarm()
    {
        _armed = false;
        _captureAttemptInFlight = false;
        _summaryObserved = false;
        _summaryRevealStarted = false;
        _retryAvailableAtSeconds = 0f;
        _fallbackAtSeconds = -1f;
    }

    public void FailOpen()
    {
        _armed = false;
        _captureAttemptInFlight = false;
        _finishedForCurrentRun = true;
        _retryAvailableAtSeconds = 0f;
        _fallbackAtSeconds = -1f;
    }

    public bool IsCaptureAttemptInFlight() => _captureAttemptInFlight;

    public bool IsArmed() => _armed;

    public bool HasFinishedForCurrentRun() => _finishedForCurrentRun;

    public bool HasCapturedForCurrentRun() => _capturedForCurrentRun;

    public bool HasSummaryRevealStarted() => _summaryRevealStarted;

    public void ResetForNewRun()
    {
        _armed = false;
        _captureAttemptInFlight = false;
        _finishedForCurrentRun = false;
        _capturedForCurrentRun = false;
        _summaryObserved = false;
        _summaryRevealStarted = false;
        _failedCaptureAttempts = 0;
        _retryAvailableAtSeconds = 0f;
        _fallbackAtSeconds = -1f;
    }

    private void ObserveReadiness(
        EndOfRunCaptureReadinessState readiness,
        float nowSeconds,
        float fallbackTimeoutSeconds
    )
    {
        if (readiness == EndOfRunCaptureReadinessState.UnknownTarget)
        {
            FailOpen();
            return;
        }

        if (readiness == EndOfRunCaptureReadinessState.NotSummary)
        {
            if (_summaryObserved)
                FailOpen();
            return;
        }

        _summaryObserved = true;
        if (
            _fallbackAtSeconds < 0f
            && readiness
                is EndOfRunCaptureReadinessState.RevealNotStarted
                    or EndOfRunCaptureReadinessState.RevealInProgress
                    or EndOfRunCaptureReadinessState.DetectionFailed
        )
        {
            _fallbackAtSeconds = nowSeconds + Math.Max(0f, fallbackTimeoutSeconds);
        }

        if (
            _fallbackAtSeconds >= 0f
            && nowSeconds >= _fallbackAtSeconds
            && readiness
                is EndOfRunCaptureReadinessState.RevealNotStarted
                    or EndOfRunCaptureReadinessState.RevealInProgress
        )
        {
            // A known in-progress native animation must never become a screenshot target.
            // Give up the screenshot instead of saving another transient frame or trapping Continue.
            FailOpen();
        }
    }
}
