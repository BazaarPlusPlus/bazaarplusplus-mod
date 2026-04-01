#nullable enable
using System;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal enum StartupUploadAttemptDecision
{
    Wait,
    Start,
    SkipLiveRun,
    Done,
}

internal sealed class StartupUploadAttemptGate
{
    private readonly float _eligibleAt;
    private bool _resolved;

    public StartupUploadAttemptGate(float startupDelaySeconds)
    {
        if (startupDelaySeconds < 0f)
            throw new ArgumentOutOfRangeException(
                nameof(startupDelaySeconds),
                "Startup delay must be non-negative."
            );

        _eligibleAt = startupDelaySeconds;
    }

    public StartupUploadAttemptDecision Poll(float currentTimeSeconds, bool liveRunActive)
    {
        if (_resolved)
            return StartupUploadAttemptDecision.Done;

        if (currentTimeSeconds < _eligibleAt)
            return StartupUploadAttemptDecision.Wait;

        _resolved = true;
        return liveRunActive
            ? StartupUploadAttemptDecision.SkipLiveRun
            : StartupUploadAttemptDecision.Start;
    }
}
