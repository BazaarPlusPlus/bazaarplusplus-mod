#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum ReplayVideoBackend
{
    MacNative,
    Ffmpeg,
}

/// <summary>
/// Central platform split for capture, availability and finalize. Keeping this decision in one
/// place prevents a macOS preflight or mux path from accidentally reintroducing FFmpeg while the
/// frame encoder remains native.
/// </summary>
internal static class ReplayVideoBackendPolicy
{
    internal static ReplayVideoBackend Current =>
        ForPlatform(FfmpegVideoEncoderProfile.DetectPlatform());

    internal static ReplayVideoBackend ForPlatform(VideoEncoderPlatform platform) =>
        platform == VideoEncoderPlatform.MacOS
            ? ReplayVideoBackend.MacNative
            : ReplayVideoBackend.Ffmpeg;

    internal static bool RequiresFfmpeg(ReplayVideoBackend backend) =>
        backend == ReplayVideoBackend.Ffmpeg;
}
