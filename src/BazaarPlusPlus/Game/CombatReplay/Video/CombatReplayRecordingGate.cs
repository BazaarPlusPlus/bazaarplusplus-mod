#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum CombatReplayRecordingBlocker
{
    None,
    NoAsyncGpuReadback,
    FfmpegUnavailable,
    NativeRecorderUnavailable,
    VideoDirectoryUnset,
}

internal readonly struct CombatReplayRecordingGateResult
{
    public CombatReplayRecordingGateResult(
        CombatReplayRecordingBlocker blocker,
        string? ffmpegExecutable,
        string? videoDirectoryPath
    )
    {
        Blocker = blocker;
        FfmpegExecutable = ffmpegExecutable;
        VideoDirectoryPath = videoDirectoryPath;
    }

    public CombatReplayRecordingBlocker Blocker { get; }
    public string? FfmpegExecutable { get; }
    public string? VideoDirectoryPath { get; }
    public bool CanRecord => Blocker == CombatReplayRecordingBlocker.None;
}

/// <summary>
/// The single source of truth for "can a replay video recording actually start". macOS requires
/// the in-process Metal/VideoToolbox plugin; Windows and other platforms retain the async GPU
/// readback + FFmpeg path. Every pre-check that promises a recording (HistoryPanel record button,
/// the BazaarAgent record endpoint's 202) must evaluate this same gate so it cannot promise a
/// recording that the capture path will silently reject. FFmpeg probing is relevant only outside
/// macOS and must be prewarmed off-thread before this gate runs on the Unity thread.
/// </summary>
internal static class CombatReplayRecordingGate
{
    public static CombatReplayRecordingGateResult Evaluate(
        string? pluginsDirectoryPath,
        string? videoDirectoryPath
    )
    {
        if (ReplayVideoBackendPolicy.Current == ReplayVideoBackend.MacNative)
        {
            if (!MacMetalVideoEncoder.TryGetAvailability(out _))
                return new(CombatReplayRecordingBlocker.NativeRecorderUnavailable, null, null);

            return string.IsNullOrWhiteSpace(videoDirectoryPath)
                ? new(CombatReplayRecordingBlocker.VideoDirectoryUnset, null, null)
                : new(CombatReplayRecordingBlocker.None, null, videoDirectoryPath);
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
            return new(CombatReplayRecordingBlocker.NoAsyncGpuReadback, null, null);

        var ffmpegExecutable = FfmpegLocator.Resolve(pluginsDirectoryPath);
        if (string.IsNullOrEmpty(ffmpegExecutable))
            return new(CombatReplayRecordingBlocker.FfmpegUnavailable, null, null);

        if (string.IsNullOrWhiteSpace(videoDirectoryPath))
            return new(CombatReplayRecordingBlocker.VideoDirectoryUnset, ffmpegExecutable, null);

        return new(CombatReplayRecordingBlocker.None, ffmpegExecutable, videoDirectoryPath);
    }
}
