#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

internal static class ReplayAudioTapStopper
{
    public static List<string> StopAndCollectUsableWavPaths(List<IReplayAudioCaptureTap> taps)
    {
        var results = Stop(taps);
        var usableWavPaths = new List<string>(results.Count);
        foreach (var result in results)
        {
            if (result.Usable)
                usableWavPaths.Add(result.WavPath);
        }

        return usableWavPaths;
    }

    public static List<ReplayAudioCaptureResult> Stop(List<IReplayAudioCaptureTap> taps)
    {
        if (taps == null)
            throw new ArgumentNullException(nameof(taps));

        var snapshot = new List<IReplayAudioCaptureTap>(taps);
        taps.Clear();

        var results = new List<ReplayAudioCaptureResult>(snapshot.Count);
        foreach (var tap in snapshot)
        {
            var capturedAnySamples = tap.CapturedAnySamples;
            var sampleFloats = tap.CapturedSampleFloats;
            var wavPath = tap.WavFilePath;
            var capturePointLabel = tap.CapturePointLabel;
            var rmsAmplitude = tap.RmsAmplitude;
            var peakAmplitude = tap.PeakAmplitude;

            try
            {
                tap.Stop();
            }
            catch (Exception ex)
            {
                BppLog.Warn("CombatReplayAudio", $"Audio tap stop failed: {ex.Message}");
            }

            var fileSize = TryGetFileSize(wavPath);
            var usable = IsUsable(capturedAnySamples, wavPath);
            BppLog.Info(
                "CombatReplayAudio",
                $"Audio tap stopped source={capturePointLabel} captured={capturedAnySamples} sampleFloats={sampleFloats} rms_db={FormatAmplitudeDb(rmsAmplitude)} peak_db={FormatAmplitudeDb(peakAmplitude)} size_bytes={fileSize} file={wavPath}"
            );

            if (!usable)
                DeleteWavBestEffort(wavPath);

            results.Add(
                new ReplayAudioCaptureResult
                {
                    WavPath = wavPath,
                    CapturedAnySamples = capturedAnySamples,
                    CapturedSampleFloats = sampleFloats,
                    FileSizeBytes = fileSize,
                    CapturePointLabel = capturePointLabel,
                    RmsAmplitude = rmsAmplitude,
                    PeakAmplitude = peakAmplitude,
                    Usable = usable,
                }
            );
        }

        return results;
    }

    public static bool IsUsable(bool capturedAnySamples, string wavPath)
    {
        return capturedAnySamples && !string.IsNullOrWhiteSpace(wavPath) && File.Exists(wavPath);
    }

    private static long TryGetFileSize(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return new FileInfo(path).Length;
        }
        catch
        {
            // best-effort diagnostic only
        }

        return 0;
    }

    private static string FormatAmplitudeDb(double amplitude)
    {
        if (amplitude <= 0 || double.IsNaN(amplitude) || double.IsInfinity(amplitude))
            return "-inf";

        return (20.0 * Math.Log10(amplitude)).ToString("F1", CultureInfo.InvariantCulture);
    }

    private static void DeleteWavBestEffort(string? wavPath)
    {
        if (string.IsNullOrEmpty(wavPath))
            return;

        try
        {
            if (File.Exists(wavPath))
                File.Delete(wavPath);
        }
        catch
        {
            // best-effort
        }
    }
}
