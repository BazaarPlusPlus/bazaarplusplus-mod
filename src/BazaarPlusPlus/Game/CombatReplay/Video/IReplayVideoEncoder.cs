#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Sealed video sink handed from the Unity capture loop to the background drain.
/// Implementations may own an external process or an in-process platform encoder.
/// </summary>
internal interface IReplayVideoEncoder : IDisposable
{
    bool WriterFailed { get; }

    FfmpegEncoderFailureReasonCode FailureReasonCode { get; }

    void SignalEndOfStream();

    FfmpegEncoderCompletionOutcome WaitForCompletion(TimeSpan timeout);
}
