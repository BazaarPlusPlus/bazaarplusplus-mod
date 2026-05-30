#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Second-pass muxer: combines the silent first-pass video with the captured audio WAV into the
/// final MP4 using <c>ffmpeg -c:v copy -c:a aac</c>. Runs entirely off the main thread and never
/// touches Unity types so it stays headless-testable. Any failure (no AAC encoder, ffmpeg error,
/// timeout, missing WAV) falls back to promoting the silent video to the final path so the
/// first-pass product is never lost.
/// </summary>
internal sealed class ReplayVideoAudioMuxer
{
    private const string LogComponent = "CombatReplayVideo";
    private const int MuxTimeoutMs = 60_000;

    private static readonly object s_pendingLock = new();
    private static readonly HashSet<Task> s_pendingTasks = new();

    // One-time AAC-encoder probe per resolved ffmpeg executable.
    private static readonly object s_aacProbeLock = new();
    private static readonly Dictionary<string, bool> s_aacProbeCache = new(
        StringComparer.OrdinalIgnoreCase
    );

    private readonly string _ffmpegExecutable;

    public ReplayVideoAudioMuxer(string ffmpegExecutable)
    {
        if (string.IsNullOrWhiteSpace(ffmpegExecutable))
            throw new ArgumentException("FFmpeg executable is required.", nameof(ffmpegExecutable));

        _ffmpegExecutable = ffmpegExecutable;
    }

    internal enum MuxStatus
    {
        Muxed,
        FellBackToSilent,
        Failed,
    }

    internal readonly struct MuxResult
    {
        public readonly MuxStatus Status;
        public readonly string FinalFilePath;
        public readonly long FileSizeBytes;
        public readonly string? Error;

        public MuxResult(MuxStatus status, string finalFilePath, long fileSizeBytes, string? error)
        {
            Status = status;
            FinalFilePath = finalFilePath;
            FileSizeBytes = fileSizeBytes;
            Error = error;
        }
    }

    /// <summary>
    /// Dispatches the mux as a tracked background task. The task is registered so
    /// <see cref="TryDrainPendingForShutdown"/> can best-effort wait on it at shutdown. The optional
    /// <paramref name="onCompleted"/> callback is invoked on the background thread after the mux
    /// resolves (success or fallback); the recorder wires it to persist SaveFinish metadata without
    /// coupling this muxer to the MonoBehaviour. The callback is best-effort and its exceptions are
    /// swallowed (logged at Debug).
    /// </summary>
    public Task DispatchAsync(
        string silentVideoTempPath,
        string? wavPath,
        string finalPath,
        Action<MuxResult>? onCompleted = null,
        int audioBitrateKbps = 192
    )
    {
        var task = Task.Run(() =>
        {
            MuxResult result;
            try
            {
                result = MuxOrPromote(silentVideoTempPath, wavPath, finalPath, audioBitrateKbps);
            }
            catch (Exception ex)
            {
                // MuxOrPromote is defensive, but never let a background task crash unobserved.
                BppLog.Error(
                    LogComponent,
                    $"Unexpected failure while muxing replay audio for '{finalPath}'.",
                    ex
                );
                result = new MuxResult(MuxStatus.Failed, finalPath, 0, ex.Message);
            }

            if (onCompleted != null)
            {
                try
                {
                    onCompleted(result);
                }
                catch (Exception ex)
                {
                    BppLog.Debug(
                        LogComponent,
                        $"Mux completion callback threw: {ex.GetType().Name} {ex.Message}"
                    );
                }
            }
        });

        Track(task);
        return task;
    }

    /// <summary>
    /// Resolves the final file from the silent video plus an optional WAV. When the WAV is missing
    /// or the resolved ffmpeg lacks an AAC encoder, promotes the silent video directly (no ffmpeg
    /// invocation). Otherwise runs <see cref="Mux"/>. Background thread only.
    /// </summary>
    public MuxResult MuxOrPromote(
        string silentVideoTempPath,
        string? wavPath,
        string finalPath,
        int audioBitrateKbps = 192
    )
    {
        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
        {
            return PromoteAndReport(
                silentVideoTempPath,
                wavPath,
                finalPath,
                MuxStatus.FellBackToSilent,
                reason: "no audio WAV available",
                warn: false
            );
        }

        if (!HasAacEncoder(_ffmpegExecutable))
        {
            return PromoteAndReport(
                silentVideoTempPath,
                wavPath,
                finalPath,
                MuxStatus.FellBackToSilent,
                reason: "ffmpeg has no AAC encoder",
                warn: true
            );
        }

        return Mux(silentVideoTempPath, wavPath!, finalPath, audioBitrateKbps);
    }

    /// <summary>
    /// Runs the ffmpeg mux pass. Background thread only. On success the silent temp and WAV are
    /// deleted (best-effort) and the final file is reported. On any failure the silent video is
    /// promoted to the final path so the recording is preserved.
    /// </summary>
    public MuxResult Mux(
        string silentVideoTempPath,
        string wavPath,
        string finalPath,
        int audioBitrateKbps = 192
    )
    {
        var arguments = BuildArguments(silentVideoTempPath, wavPath, finalPath, audioBitrateKbps);

        Process? process = null;
        var stderr = new StringBuilder(capacity: 2048);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegExecutable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
            };

            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return FallBack(
                    silentVideoTempPath,
                    wavPath,
                    finalPath,
                    "ffmpeg mux process failed to start"
                );
            }

            // Drain stderr on a worker so a full pipe buffer can never deadlock WaitForExit.
            var drainThread = new Thread(() =>
            {
                try
                {
                    var text = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(text))
                    {
                        lock (stderr)
                        {
                            stderr.Append(text);
                        }
                    }
                }
                catch
                {
                    // Process exited / stream closed; nothing to drain.
                }
            })
            {
                IsBackground = true,
                Name = "BPP.CombatReplayVideo.MuxStderr",
            };
            drainThread.Start();

            if (!process.WaitForExit(MuxTimeoutMs))
            {
                ForceKill(process);
                drainThread.Join(500);
                return FallBack(
                    silentVideoTempPath,
                    wavPath,
                    finalPath,
                    $"ffmpeg mux timed out after {MuxTimeoutMs}ms"
                );
            }

            drainThread.Join(500);
            var exitCode = process.ExitCode;
            var stderrTail = ReadStderrTail(stderr);

            if (exitCode == 0 && File.Exists(finalPath))
            {
                // ffmpeg with -shortest can exit 0 yet emit a zero-duration output
                // when the audio input is empty (e.g. a header-only WAV): it logs
                // "Output file is empty" and leaves a ~hundred-byte stub. Treat that
                // as a failure and fall back to the silent video. Validate BEFORE
                // deleting the silent temp so the good first-pass product survives.
                var mixedSize = FfmpegRawVideoEncoder.TryGetFileSize(finalPath);
                var silentSize = FfmpegRawVideoEncoder.TryGetFileSize(silentVideoTempPath);
                if (IsLikelyZeroDurationOutput(mixedSize, silentSize))
                {
                    return FallBack(
                        silentVideoTempPath,
                        wavPath,
                        finalPath,
                        $"ffmpeg exit 0 but output is empty/zero-duration "
                            + $"({mixedSize} bytes vs silent {silentSize} bytes). stderr tail: {stderrTail}"
                    );
                }

                TryDelete(silentVideoTempPath);
                TryDelete(wavPath);
                BppLog.Info(
                    LogComponent,
                    $"Muxed replay audio into '{finalPath}' ({mixedSize} bytes)."
                );
                return new MuxResult(MuxStatus.Muxed, finalPath, mixedSize, null);
            }

            return FallBack(
                silentVideoTempPath,
                wavPath,
                finalPath,
                $"ffmpeg exit code {exitCode}. stderr tail: {stderrTail}"
            );
        }
        catch (Exception ex)
        {
            return FallBack(
                silentVideoTempPath,
                wavPath,
                finalPath,
                $"{ex.GetType().Name}: {ex.Message}"
            );
        }
        finally
        {
            try
            {
                process?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    private MuxResult PromoteAndReport(
        string silentVideoTempPath,
        string? wavPath,
        string finalPath,
        MuxStatus status,
        string reason,
        bool warn
    )
    {
        if (warn)
        {
            BppLog.Warn(
                LogComponent,
                $"Skipping mux, falling back to silent video ({reason}): {finalPath}"
            );
        }
        else
        {
            BppLog.Info(LogComponent, $"Finalizing silent video ({reason}): {finalPath}");
        }

        try
        {
            var size = PromoteSilentToFinal(silentVideoTempPath, finalPath);
            TryDelete(wavPath);
            return new MuxResult(status, finalPath, size, reason);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                LogComponent,
                $"Failed to promote silent video '{silentVideoTempPath}' to '{finalPath}': {ex.Message}"
            );
            return new MuxResult(MuxStatus.Failed, finalPath, 0, ex.Message);
        }
    }

    private MuxResult FallBack(
        string silentVideoTempPath,
        string? wavPath,
        string finalPath,
        string reason
    )
    {
        BppLog.Warn(LogComponent, $"Mux failed, falling back to silent video: {reason}");

        try
        {
            var size = PromoteSilentToFinal(silentVideoTempPath, finalPath);
            TryDelete(wavPath);
            return new MuxResult(MuxStatus.FellBackToSilent, finalPath, size, reason);
        }
        catch (Exception ex)
        {
            return new MuxResult(MuxStatus.Failed, finalPath, 0, ex.Message);
        }
    }

    private string BuildArguments(
        string silentVideoTempPath,
        string wavPath,
        string finalPath,
        int audioBitrateKbps
    )
    {
        var bitrate = audioBitrateKbps > 0 ? audioBitrateKbps : 192;
        var sb = new StringBuilder();
        sb.Append("-hide_banner -loglevel warning -nostdin -y ");
        sb.Append("-i ").Append(QuoteArg(silentVideoTempPath)).Append(' ');
        sb.Append("-i ").Append(QuoteArg(wavPath)).Append(' ');
        sb.Append("-map 0:v:0 -map 1:a:0 ");
        sb.Append("-c:v copy -c:a aac ");
        sb.Append($"-b:a {bitrate}k ");
        sb.Append("-shortest -movflags +faststart ");
        sb.Append(QuoteArg(finalPath));
        return sb.ToString();
    }

    /// <summary>
    /// Heuristic guard against a zero-duration mux output. ffmpeg's <c>-shortest</c> trims the muxed
    /// file to the shortest input, so an empty audio input (e.g. a header-only WAV) makes it exit 0
    /// while emitting only a few hundred bytes of container with no media. Because the mux uses
    /// <c>-c:v copy</c>, a valid output always carries the full first-pass video payload and is at
    /// least as large as the silent input; a real but tiny recording is still bounded below by that
    /// silent size. So the output is considered zero-duration when it is empty, or when the silent
    /// input size is known and the output is less than half of it (generous slack for container /
    /// faststart differences, yet orders of magnitude above an empty stub). When the silent size is
    /// unknown (0), only a truly empty output is rejected.
    /// </summary>
    internal static bool IsLikelyZeroDurationOutput(long muxedSize, long silentSize)
    {
        if (muxedSize <= 0)
            return true;

        if (silentSize <= 0)
            return false;

        return muxedSize < silentSize / 2;
    }

    /// <summary>
    /// Promotes the silent first-pass video to the final path. Lifts the exact File.Move sequence
    /// previously inlined in <c>CombatReplayVideoRecorder.FinalizeOutputFile</c> so the logic lives
    /// once. Throws on hard IO failure (callers catch). If the temp is already gone, reports the
    /// current size of the final path (idempotent re-finalize).
    /// </summary>
    internal static long PromoteSilentToFinal(string tempPath, string finalPath)
    {
        if (!File.Exists(tempPath))
            return FfmpegRawVideoEncoder.TryGetFileSize(finalPath);

        var dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(tempPath, finalPath);
        return FfmpegRawVideoEncoder.TryGetFileSize(finalPath);
    }

    /// <summary>
    /// Best-effort, cached probe that the resolved ffmpeg exposes an AAC encoder. Parses
    /// <c>ffmpeg -hide_banner -encoders</c> once per executable. On probe failure assumes AAC is
    /// available so a transient probe error never silently strips audio (the mux itself still
    /// gracefully falls back if AAC is genuinely missing).
    /// </summary>
    internal static bool HasAacEncoder(string ffmpegExecutable)
    {
        if (string.IsNullOrWhiteSpace(ffmpegExecutable))
            return false;

        lock (s_aacProbeLock)
        {
            if (s_aacProbeCache.TryGetValue(ffmpegExecutable, out var cached))
                return cached;

            var hasAac = ProbeAacEncoder(ffmpegExecutable);
            s_aacProbeCache[ffmpegExecutable] = hasAac;
            return hasAac;
        }
    }

    internal static void ResetAacProbeCacheForTests()
    {
        lock (s_aacProbeLock)
        {
            s_aacProbeCache.Clear();
        }
    }

    private static bool ProbeAacEncoder(string ffmpegExecutable)
    {
        Process? process = null;
        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExecutable,
                    Arguments = "-hide_banner -encoders",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };

            if (!process.Start())
                return true; // Could not probe; do not strip audio over a launch hiccup.

            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                ForceKill(process);
                return true;
            }

            // ffmpeg lists encoders as e.g. " A..... aac                  AAC (Advanced Audio Coding)".
            // Match the encoder token "aac" specifically (built-in or "libfdk_aac" both satisfy a
            // " aac " token search; libfdk_aac also contains "aac" so a substring check suffices).
            var hasAac = HasAacToken(stdout);
            if (!hasAac)
            {
                BppLog.Warn(
                    LogComponent,
                    $"Resolved ffmpeg '{ffmpegExecutable}' reports no AAC encoder; replay audio will fall back to silent video."
                );
            }

            return hasAac;
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                LogComponent,
                $"AAC encoder probe failed for '{ffmpegExecutable}': {ex.GetType().Name} {ex.Message}"
            );
            return true; // Assume available on probe failure; mux still degrades gracefully.
        }
        finally
        {
            try
            {
                process?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static readonly char[] s_whitespace = { ' ', '\t', '\r', '\f', '\v' };

    private static bool HasAacToken(string? encodersOutput)
    {
        if (string.IsNullOrEmpty(encodersOutput))
            return false;

        // ffmpeg lists encoders one per row as "<flags> <name> <description...>", e.g.
        // " A..... aac                  AAC (Advanced Audio Coding)". Scan each row and match the
        // NAME column so a description word never produces a false positive. The first flag column
        // is 'A' for audio encoders; require it so a stray "aac" in prose is ignored.
        var lines = encodersOutput!.Split('\n');
        foreach (var line in lines)
        {
            var parts = line.Split(s_whitespace, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var flags = parts[0];
            if (flags.Length == 0 || flags[0] != 'A')
                continue;

            var name = parts[1];
            if (
                string.Equals(name, "aac", StringComparison.Ordinal)
                || name.IndexOf("aac", StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Best-effort waits for all outstanding mux tasks dispatched via <see cref="DispatchAsync"/> to
    /// complete, up to <paramref name="timeout"/>. Intended for app shutdown so in-flight muxes get
    /// a chance to finish; any not finished are abandoned (their temp files are reclaimed on the next
    /// launch). Returns true if all pending tasks completed within the timeout.
    /// </summary>
    public static bool TryDrainPendingForShutdown(TimeSpan timeout)
    {
        Task[] pending;
        lock (s_pendingLock)
        {
            if (s_pendingTasks.Count == 0)
                return true;
            pending = new Task[s_pendingTasks.Count];
            s_pendingTasks.CopyTo(pending);
        }

        try
        {
            return Task.WaitAll(pending, timeout);
        }
        catch (Exception ex)
        {
            // Faulted tasks already logged their own failure; surface nothing here.
            BppLog.Debug(
                LogComponent,
                $"Draining pending mux tasks observed an exception: {ex.GetType().Name} {ex.Message}"
            );
            return false;
        }
    }

    public static int PendingTaskCount
    {
        get
        {
            lock (s_pendingLock)
            {
                return s_pendingTasks.Count;
            }
        }
    }

    private static void Track(Task task)
    {
        lock (s_pendingLock)
        {
            s_pendingTasks.Add(task);
        }

        // Untrack on completion regardless of outcome. ContinueWith runs on a pool thread.
        task.ContinueWith(
            static t =>
            {
                lock (s_pendingLock)
                {
                    s_pendingTasks.Remove(t);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static string ReadStderrTail(StringBuilder stderr)
    {
        lock (stderr)
        {
            var text = stderr.ToString();
            if (text.Length <= 2048)
                return text;
            return text.Substring(text.Length - 2048);
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                LogComponent,
                $"Failed to delete '{path}': {ex.GetType().Name} {ex.Message}"
            );
        }
    }

    private static void ForceKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(500);
            }
        }
        catch
        {
            // ignore
        }
    }

    // Copied verbatim from FfmpegRawVideoEncoder.QuoteArg (private there) so quoting stays identical:
    // quote only when the value contains a space or a double quote.
    private static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        if (!value.Contains(' ') && !value.Contains('"'))
            return value;

        var escaped = value.Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
