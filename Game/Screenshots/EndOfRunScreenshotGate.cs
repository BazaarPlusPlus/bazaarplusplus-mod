#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotGate
{
    private bool _captureAttemptInFlight;
    private bool _captureConsumedForCurrentRun;
    private float _retryAvailableAtSeconds;

    public bool TryBeginCapture(bool isInteractionBlocked, float nowSeconds)
    {
        if (
            isInteractionBlocked
            || _captureConsumedForCurrentRun
            || _captureAttemptInFlight
            || nowSeconds < _retryAvailableAtSeconds
        )
            return false;

        _captureAttemptInFlight = true;
        return true;
    }

    public void CompleteCaptureAttempt()
    {
        _captureAttemptInFlight = false;
        _captureConsumedForCurrentRun = true;
        _retryAvailableAtSeconds = 0f;
    }

    public void AbortCaptureAttempt(float retryAvailableAtSeconds)
    {
        _captureAttemptInFlight = false;
        _retryAvailableAtSeconds = retryAvailableAtSeconds;
    }

    public void CancelCaptureAttempt()
    {
        _captureAttemptInFlight = false;
    }

    public bool IsCaptureAttemptInFlight()
    {
        return _captureAttemptInFlight;
    }

    public void ResetForNewRun()
    {
        _captureAttemptInFlight = false;
        _captureConsumedForCurrentRun = false;
        _retryAvailableAtSeconds = 0f;
    }
}
