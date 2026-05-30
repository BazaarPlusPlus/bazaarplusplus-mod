#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal readonly struct ReplayVideoAudioTapSpec
{
    public ReplayVideoAudioTapSpec(
        string wavPath,
        string studioBusPath,
        bool allowCoreMasterFallback
    )
    {
        WavPath = wavPath ?? throw new ArgumentNullException(nameof(wavPath));
        StudioBusPath = studioBusPath ?? throw new ArgumentNullException(nameof(studioBusPath));
        AllowCoreMasterFallback = allowCoreMasterFallback;
    }

    public string WavPath { get; }
    public string StudioBusPath { get; }
    public bool AllowCoreMasterFallback { get; }
}

internal static class ReplayVideoAudioTapPlan
{
    internal static IReadOnlyList<ReplayVideoAudioTapSpec> Create(string tempVideoPath) =>
        new[]
        {
            new ReplayVideoAudioTapSpec(
                DeriveAudioWavPath(tempVideoPath, "audio"),
                "bus:/",
                allowCoreMasterFallback: true
            ),
            new ReplayVideoAudioTapSpec(
                DeriveAudioWavPath(tempVideoPath, "sfx.audio"),
                "bus:/SFX",
                allowCoreMasterFallback: false
            ),
        };

    internal static IReadOnlyList<string> DeriveAudioWavPaths(string tempVideoPath)
    {
        var specs = Create(tempVideoPath);
        var paths = new List<string>(specs.Count);
        foreach (var spec in specs)
            paths.Add(spec.WavPath);

        return paths;
    }

    private static string DeriveAudioWavPath(string tempVideoPath, string audioSuffix)
    {
        const string suffix = ".recording.mp4";
        if (
            string.IsNullOrEmpty(tempVideoPath)
            || !tempVideoPath.EndsWith(suffix, StringComparison.Ordinal)
        )
        {
            return tempVideoPath + "." + audioSuffix + ".wav";
        }

        return tempVideoPath.Substring(0, tempVideoPath.Length - suffix.Length)
            + "."
            + audioSuffix
            + ".wav";
    }
}
