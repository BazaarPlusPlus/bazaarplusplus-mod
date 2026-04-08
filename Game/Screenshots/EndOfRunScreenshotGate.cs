#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotGate
{
    private bool _attemptInFlight;
    private bool _capturedForCurrentRun;
    private bool _allowNextContinuePassthrough;

    public bool ShouldCaptureOnContinue(bool isInteractionBlocked)
    {
        if (isInteractionBlocked || _capturedForCurrentRun || _attemptInFlight)
            return false;

        _attemptInFlight = true;
        return true;
    }

    public void ResetForNewRun()
    {
        _attemptInFlight = false;
        _capturedForCurrentRun = false;
        _allowNextContinuePassthrough = false;
    }

    public void MarkAttemptCompleted()
    {
        _attemptInFlight = false;
        _capturedForCurrentRun = true;
    }

    public void MarkAttemptAborted()
    {
        _attemptInFlight = false;
    }

    public void AllowNextContinuePassthrough()
    {
        _allowNextContinuePassthrough = true;
    }

    public bool ConsumeContinuePassthrough()
    {
        if (!_allowNextContinuePassthrough)
            return false;

        _allowNextContinuePassthrough = false;
        return true;
    }

    public bool IsAttemptInFlight()
    {
        return _attemptInFlight;
    }
}
