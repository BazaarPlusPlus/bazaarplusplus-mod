#nullable enable
using System;
using System.Diagnostics;
using System.IO;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class FfmpegLocator
{
    private static readonly object SyncRoot = new();
    private static bool _resolved;
    private static string? _resolvedPath;

    public static string? Resolve(string? toolsDirectoryPath)
    {
        lock (SyncRoot)
        {
            if (_resolved)
                return _resolvedPath;

            _resolvedPath = TryResolveBundled(toolsDirectoryPath) ?? TryResolveOnPath();
            _resolved = true;

            if (string.IsNullOrEmpty(_resolvedPath))
            {
                BppLog.Info(
                    "CombatReplayVideo",
                    $"FFmpeg not detected. Drop a binary under '{toolsDirectoryPath}/ffmpeg/' or install it on PATH to enable replay video recording."
                );
            }
            else
            {
                BppLog.Info("CombatReplayVideo", $"FFmpeg detected: {_resolvedPath}");
            }

            return _resolvedPath;
        }
    }

    public static void ResetForTests()
    {
        lock (SyncRoot)
        {
            _resolved = false;
            _resolvedPath = null;
        }
    }

    private static string? TryResolveBundled(string? toolsDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(toolsDirectoryPath))
            return null;

        var fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var candidate = Path.Combine(toolsDirectoryPath, "ffmpeg", fileName);
        if (!File.Exists(candidate))
            return null;

        return TryProbe(candidate) ? candidate : null;
    }

    private static string? TryResolveOnPath()
    {
        var fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return TryProbe(fileName) ? fileName : null;
    }

    private static bool TryProbe(string executable)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
                return false;

            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // ignore
                }

                BppLog.Warn(
                    "CombatReplayVideo",
                    $"FFmpeg probe timed out: {executable}. Treating as unavailable."
                );
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"FFmpeg probe failed for '{executable}': {ex.GetType().Name} {ex.Message}"
            );
            return false;
        }
    }

    private static class OperatingSystem
    {
        public static bool IsWindows() =>
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows
            );
    }
}
