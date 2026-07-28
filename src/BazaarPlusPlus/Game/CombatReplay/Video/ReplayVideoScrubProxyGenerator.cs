#nullable enable
using System.Diagnostics;
using System.Text;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum ReplayVideoScrubProxyReasonCode
{
    Created,
    AlreadyExists,
    InvalidInput,
    FfmpegUnavailable,
    InputMissing,
    ProcessStartFailed,
    ProcessTimeout,
    NonZeroExit,
    EmptyOutput,
    CommitFailed,
    UnexpectedException,
}

internal readonly record struct ReplayVideoScrubProxyResult(
    bool Available,
    string FilePath,
    ReplayVideoScrubProxyReasonCode ReasonCode,
    int? ExitCode = null,
    string StderrTail = "",
    Exception? Exception = null
);

internal static class ReplayVideoScrubProxyGenerator
{
    private const int ProcessTimeoutMs = 90_000;
    private const long MinimumUsableFileSizeBytes = 1024;

    internal static ReplayVideoScrubProxyResult Create(
        string? ffmpegExecutable,
        string finalVideoFilePath
    )
    {
        string proxyFilePath;
        try
        {
            proxyFilePath = ReplayVideoScrubProxy.BuildFilePath(finalVideoFilePath);
        }
        catch (Exception ex)
        {
            return new ReplayVideoScrubProxyResult(
                false,
                string.Empty,
                ReplayVideoScrubProxyReasonCode.InvalidInput,
                Exception: ex
            );
        }

        if (IsUsable(proxyFilePath))
        {
            return new ReplayVideoScrubProxyResult(
                true,
                proxyFilePath,
                ReplayVideoScrubProxyReasonCode.AlreadyExists
            );
        }
        if (string.IsNullOrWhiteSpace(ffmpegExecutable) || !File.Exists(ffmpegExecutable))
        {
            return new ReplayVideoScrubProxyResult(
                false,
                proxyFilePath,
                ReplayVideoScrubProxyReasonCode.FfmpegUnavailable
            );
        }
        if (!File.Exists(finalVideoFilePath))
        {
            return new ReplayVideoScrubProxyResult(
                false,
                proxyFilePath,
                ReplayVideoScrubProxyReasonCode.InputMissing
            );
        }

        var outputDirectory = Path.GetDirectoryName(proxyFilePath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return new ReplayVideoScrubProxyResult(
                false,
                proxyFilePath,
                ReplayVideoScrubProxyReasonCode.InvalidInput
            );
        }

        var temporaryFilePath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileNameWithoutExtension(proxyFilePath)}.{Guid.NewGuid():N}.tmp.mp4"
        );
        Process? process = null;
        Thread? stderrThread = null;
        var stderr = new BoundedTextTail();
        try
        {
            Directory.CreateDirectory(outputDirectory);
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExecutable,
                    Arguments = BuildArguments(finalVideoFilePath, temporaryFilePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                },
            };
            if (!process.Start())
            {
                return Failure(
                    proxyFilePath,
                    ReplayVideoScrubProxyReasonCode.ProcessStartFailed
                );
            }

            stderrThread = new Thread(() =>
            {
                try
                {
                    stderr.ReadFrom(process.StandardError);
                }
                catch
                {
                    // The process exited or its stream was closed.
                }
            })
            {
                IsBackground = true,
                Name = "BPP.CombatReplayVideo.ScrubProxyStderr",
            };
            stderrThread.Start();

            if (!process.WaitForExit(ProcessTimeoutMs))
            {
                ForceKill(process);
                FinishStderrDrain(process, stderrThread);
                return Failure(
                    proxyFilePath,
                    ReplayVideoScrubProxyReasonCode.ProcessTimeout,
                    stderrTail: stderr.Value
                );
            }

            var exitCode = process.ExitCode;
            FinishStderrDrain(process, stderrThread);
            if (exitCode != 0)
            {
                return Failure(
                    proxyFilePath,
                    ReplayVideoScrubProxyReasonCode.NonZeroExit,
                    exitCode,
                    stderr.Value
                );
            }
            if (!IsUsable(temporaryFilePath))
            {
                return Failure(
                    proxyFilePath,
                    ReplayVideoScrubProxyReasonCode.EmptyOutput,
                    exitCode,
                    stderr.Value
                );
            }

            try
            {
                if (File.Exists(proxyFilePath))
                    File.Delete(proxyFilePath);
                File.Move(temporaryFilePath, proxyFilePath);
            }
            catch (Exception ex)
            {
                return Failure(
                    proxyFilePath,
                    ReplayVideoScrubProxyReasonCode.CommitFailed,
                    exitCode,
                    stderr.Value,
                    ex
                );
            }

            return new ReplayVideoScrubProxyResult(
                true,
                proxyFilePath,
                ReplayVideoScrubProxyReasonCode.Created,
                exitCode,
                stderr.Value
            );
        }
        catch (Exception ex)
        {
            return Failure(
                proxyFilePath,
                ReplayVideoScrubProxyReasonCode.UnexpectedException,
                stderrTail: stderr.Value,
                exception: ex
            );
        }
        finally
        {
            TryDelete(temporaryFilePath);
            try
            {
                process?.Dispose();
            }
            catch
            {
                // Best effort.
            }
        }
    }

    internal static string BuildArguments(string inputFilePath, string outputFilePath)
    {
        var arguments = new StringBuilder();
        arguments.Append("-hide_banner -loglevel warning -nostdin -y ");
        arguments.Append("-i ").Append(VideoProcessHelpers.QuoteArg(inputFilePath)).Append(' ');
        arguments.Append("-map 0:v:0 -map_metadata -1 -an ");
        arguments.Append("-vf scale=-2:540:flags=fast_bilinear ");
        arguments.Append("-c:v libx264 -preset veryfast -tune fastdecode -crf 30 ");
        arguments.Append("-pix_fmt yuv420p -bf 0 -sc_threshold 0 ");
        arguments.Append("-force_key_frames expr:gte(t,n_forced*0.25) ");
        arguments.Append("-movflags +faststart ");
        arguments.Append(VideoProcessHelpers.QuoteArg(outputFilePath));
        return arguments.ToString();
    }

    private static ReplayVideoScrubProxyResult Failure(
        string filePath,
        ReplayVideoScrubProxyReasonCode reasonCode,
        int? exitCode = null,
        string? stderrTail = null,
        Exception? exception = null
    ) =>
        new(
            false,
            filePath,
            reasonCode,
            exitCode,
            stderrTail ?? string.Empty,
            exception
        );

    private static bool IsUsable(string filePath)
    {
        try
        {
            return File.Exists(filePath)
                && new FileInfo(filePath).Length >= MinimumUsableFileSizeBytes;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static void FinishStderrDrain(Process process, Thread stderrThread)
    {
        if (stderrThread.Join(TimeSpan.FromSeconds(1)))
            return;
        try
        {
            process.StandardError.Close();
        }
        catch
        {
            // Best effort.
        }
        stderrThread.Join(TimeSpan.FromMilliseconds(500));
    }

    private static void ForceKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
            process.WaitForExit(500);
        }
        catch
        {
            // Best effort.
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // A unique temp file can be reclaimed by the user's normal directory cleanup.
        }
    }
}
