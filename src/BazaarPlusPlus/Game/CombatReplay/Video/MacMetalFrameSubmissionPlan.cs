#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Separates frames that can be encoded from wall-clock slots skipped by the pacer. Successful
/// Metal frames are packed at consecutive CFR timestamps, matching the FFmpeg path; putting a PTS
/// gap behind a renderer hitch would make the preceding frame visibly freeze and inflate duration.
/// </summary>
internal readonly record struct MacMetalFrameSubmissionPlan(
    int EncodeFrameCount,
    int TimelineFrameCount,
    int CapturedFrameCount,
    int RepeatedFrameCount,
    int PacerDroppedFrameCount
)
{
    internal int DroppedFrameCountOnSubmissionFailure =>
        checked(EncodeFrameCount + PacerDroppedFrameCount);

    internal static MacMetalFrameSubmissionPlan Create(
        int emitFrameCount,
        int repeatFrameCount,
        int pacerDroppedFrameCount
    )
    {
        if (emitFrameCount < 0)
            throw new ArgumentOutOfRangeException(nameof(emitFrameCount));
        if (repeatFrameCount < 0 || repeatFrameCount > emitFrameCount)
            throw new ArgumentOutOfRangeException(nameof(repeatFrameCount));
        if (pacerDroppedFrameCount < 0)
            throw new ArgumentOutOfRangeException(nameof(pacerDroppedFrameCount));

        return new MacMetalFrameSubmissionPlan(
            emitFrameCount,
            emitFrameCount,
            emitFrameCount - repeatFrameCount,
            repeatFrameCount,
            pacerDroppedFrameCount
        );
    }
}
