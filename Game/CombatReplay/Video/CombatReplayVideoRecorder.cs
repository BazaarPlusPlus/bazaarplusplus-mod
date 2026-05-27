#nullable enable
using System;
using System.Collections;
using System.IO;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.Rendering;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class CombatReplayVideoRecorder : MonoBehaviour
{
    private IBppServices? _services;
    private CombatReplayVideoMetadataStore? _metadataStore;
    private IDisposable? _startingSubscription;
    private IDisposable? _endedSubscription;
    private ReplayVideoCaptureSession? _activeSession;
    private Coroutine? _captureCoroutine;
    private IDisposable? _uiSuppressionScope;
    private float? _savedCombatSpeed;
    private string? _activeRecordingTempPath;
    private string? _activeRecordingFinalPath;

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        var runLogDatabasePath = services.Paths.RunLogDatabasePath;
        if (!string.IsNullOrWhiteSpace(runLogDatabasePath))
        {
            try
            {
                _metadataStore = new CombatReplayVideoMetadataStore(runLogDatabasePath);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "CombatReplayVideo",
                    $"Failed to open replay video metadata store: {ex.Message}. Metadata will not be persisted."
                );
                _metadataStore = null;
            }
        }
    }

    private void OnEnable()
    {
        var services = _services;
        if (services == null)
            return;

        _startingSubscription = services.EventBus.Subscribe<CombatReplayPlaybackStarting>(
            OnPlaybackStarting
        );
        _endedSubscription = services.EventBus.Subscribe<CombatReplayPlaybackEnded>(
            OnPlaybackEnded
        );
    }

    private void OnDisable()
    {
        _startingSubscription?.Dispose();
        _startingSubscription = null;
        _endedSubscription?.Dispose();
        _endedSubscription = null;

        AbortActiveSession("recorder-disabled");
    }

    private void OnDestroy()
    {
        AbortActiveSession("recorder-destroyed");
    }

    private void OnPlaybackStarting(CombatReplayPlaybackStarting evt)
    {
        if (evt == null || string.IsNullOrWhiteSpace(evt.BattleId))
            return;

        if (_activeSession != null)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"Replay playback started for {evt.BattleId} while a previous capture session was still active; aborting old session."
            );
            AbortActiveSession("superseded");
        }

        var services = _services;
        if (services == null)
            return;

        var config = services.Config;
        if (config.CombatReplayVideoEnabled?.Value != true)
            return;

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            BppLog.Info(
                "CombatReplayVideo",
                "SystemInfo.supportsAsyncGPUReadback is false on this device; video recording is disabled."
            );
            return;
        }

        var toolsDirectoryPath = services.Paths.ToolsDirectoryPath;
        var ffmpegExecutable = FfmpegLocator.Resolve(toolsDirectoryPath);
        if (string.IsNullOrEmpty(ffmpegExecutable))
            return;

        var videoDirectoryPath = services.Paths.CombatReplayVideoDirectoryPath;
        if (string.IsNullOrWhiteSpace(videoDirectoryPath))
        {
            BppLog.Warn(
                "CombatReplayVideo",
                "CombatReplayVideoDirectoryPath is not configured; cannot record replay video."
            );
            return;
        }

        var request = BuildCaptureRequest(evt, services, ffmpegExecutable!, videoDirectoryPath);
        if (request == null)
            return;

        try
        {
            BeginRecording(request, services);
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "CombatReplayVideo",
                $"Failed to begin replay video recording for {evt.BattleId}.",
                ex
            );
            CleanupAfterAbort();
        }
    }

    private void OnPlaybackEnded(CombatReplayPlaybackEnded evt)
    {
        if (evt == null || _activeSession == null)
            return;

        var session = _activeSession;
        var reason = evt.Reason ?? (evt.Failed ? "playback-failed" : "playback-ended");

        try
        {
            StopCaptureCoroutine();
            var result = session.Finalize(reason);
            FinalizeOutputFile(result);
            TrySaveFinishMetadata(result);
            DisposeUiAndSpeedState();
            _activeSession = null;
            _activeRecordingTempPath = null;
            _activeRecordingFinalPath = null;

            BppLog.Info(
                "CombatReplayVideo",
                $"Recording for {result.BattleId} stopped with status={result.Status} captured={result.CapturedFrames} dropped={result.DroppedFrames}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "CombatReplayVideo",
                $"Failed to finalize replay video recording for {evt.BattleId}.",
                ex
            );
            try
            {
                session.Dispose();
            }
            catch
            {
                // ignore
            }
            DisposeUiAndSpeedState();
            DeleteTempFile();
            _activeSession = null;
            _activeRecordingTempPath = null;
            _activeRecordingFinalPath = null;
        }
    }

    private void BeginRecording(ReplayVideoCaptureRequest request, IBppServices services)
    {
        var session = new ReplayVideoCaptureSession(request);
        try
        {
            session.Start();
        }
        catch
        {
            session.Dispose();
            throw;
        }

        _activeSession = session;
        _activeRecordingTempPath = request.OutputFilePath;
        _activeRecordingFinalPath = StripTempSuffix(request.OutputFilePath);

        if (request.SuppressBppOverlays)
            _uiSuppressionScope = BeginUiSuppression();

        if (request.ForceSpeed1x)
            ApplyForcedCombatSpeed();

        TrySaveStartMetadata(request, services);

        _captureCoroutine = StartCoroutine(CaptureLoop(session));

        BppLog.Info(
            "CombatReplayVideo",
            $"Recording started battle={request.BattleId} source={request.Source} -> {request.OutputFilePath}"
        );
    }

    private void TrySaveStartMetadata(ReplayVideoCaptureRequest request, IBppServices services)
    {
        var store = _metadataStore;
        if (store == null)
            return;

        try
        {
            var videoDirectoryPath = services.Paths.CombatReplayVideoDirectoryPath;
            var relativePath = ComputeRelativePath(
                videoDirectoryPath,
                _activeRecordingFinalPath ?? request.OutputFilePath
            );

            store.SaveStart(
                new VideoRecordingStarted
                {
                    VideoId = request.VideoId,
                    BattleId = request.BattleId,
                    Source = request.Source.ToString(),
                    VideoRelativePath = relativePath,
                    Width = request.Width,
                    Height = request.Height,
                    Fps = request.Fps,
                    Codec = "libx264",
                    Crf = request.Crf,
                    Preset = request.Preset,
                    StartedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"Failed to save start metadata for video {request.VideoId}: {ex.Message}"
            );
        }
    }

    private void TrySaveFinishMetadata(ReplayVideoCaptureResult result)
    {
        var store = _metadataStore;
        if (store == null)
            return;

        try
        {
            var videoDirectoryPath = _services?.Paths.CombatReplayVideoDirectoryPath;
            var finalPath = _activeRecordingFinalPath ?? StripTempSuffix(result.OutputFilePath);
            var relativePath = ComputeRelativePath(videoDirectoryPath, finalPath);
            var endedAt = result.EndedAtUtc ?? DateTimeOffset.UtcNow;
            var status = result.Status switch
            {
                ReplayVideoCaptureStatus.Completed => "COMPLETED",
                ReplayVideoCaptureStatus.Failed => "FAILED",
                _ => "FAILED",
            };

            store.SaveFinish(
                new VideoRecordingFinished
                {
                    VideoId = result.VideoId,
                    VideoRelativePath = relativePath,
                    EndedAtUtc = endedAt,
                    DurationMs = result.DurationMs,
                    CapturedFrames = result.CapturedFrames,
                    DroppedFrames = result.DroppedFrames,
                    FileSizeBytes = result.FileSizeBytes,
                    Status = status,
                    Error = result.Error,
                }
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"Failed to save finish metadata for video {result.VideoId}: {ex.Message}"
            );
        }
    }

    private static string ComputeRelativePath(string? rootDirectory, string filePath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(filePath))
            return filePath ?? string.Empty;

        try
        {
            var rootFull = Path.GetFullPath(rootDirectory);
            var fileFull = Path.GetFullPath(filePath);
            if (
                fileFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)
                && fileFull.Length > rootFull.Length
            )
            {
                var trimmed = fileFull.Substring(rootFull.Length);
                return trimmed.TrimStart(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                );
            }
        }
        catch
        {
            // fall through
        }

        return filePath;
    }

    private IEnumerator CaptureLoop(ReplayVideoCaptureSession session)
    {
        var waitForEndOfFrame = new WaitForEndOfFrame();
        while (session.IsActive && _activeSession == session)
        {
            yield return waitForEndOfFrame;
            if (!session.IsActive || _activeSession != session)
                break;
            session.CaptureFrameIfDue();
        }
    }

    private void StopCaptureCoroutine()
    {
        if (_captureCoroutine != null)
        {
            try
            {
                StopCoroutine(_captureCoroutine);
            }
            catch
            {
                // ignore
            }
            _captureCoroutine = null;
        }
    }

    private void AbortActiveSession(string reason)
    {
        var session = _activeSession;
        _activeSession = null;

        StopCaptureCoroutine();

        if (session != null)
        {
            try
            {
                var result = session.Finalize(reason);
                FinalizeOutputFile(result);
                TrySaveFinishMetadata(result);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "CombatReplayVideo",
                    $"Abort finalize failed reason={reason}: {ex.Message}"
                );
            }
            finally
            {
                try
                {
                    session.Dispose();
                }
                catch
                {
                    // ignore
                }
            }
        }

        DisposeUiAndSpeedState();
        DeleteTempFile();
        _activeRecordingTempPath = null;
        _activeRecordingFinalPath = null;
    }

    private void CleanupAfterAbort()
    {
        DisposeUiAndSpeedState();
        DeleteTempFile();
        _activeRecordingTempPath = null;
        _activeRecordingFinalPath = null;
    }

    private void ApplyForcedCombatSpeed()
    {
        try
        {
            _savedCombatSpeed = CombatStatusBarFeature.CombatSpeedMultiplier;
            CombatStatusBarFeature.SetCombatSpeed(1f);
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to apply forced 1x combat speed: {ex.Message}"
            );
            _savedCombatSpeed = null;
        }
    }

    private void RestoreCombatSpeed()
    {
        if (!_savedCombatSpeed.HasValue)
            return;

        var savedSpeed = _savedCombatSpeed.Value;
        _savedCombatSpeed = null;
        try
        {
            CombatStatusBarFeature.SetCombatSpeed(savedSpeed);
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to restore combat speed to {savedSpeed:F2}: {ex.Message}"
            );
        }
    }

    private static IDisposable? BeginUiSuppression()
    {
        try
        {
            return UiSuppressionScope.Begin(
                BppSettingsDockController.BeginScreenshotSuppression,
                CombatStatusBarFeature.BeginScreenshotSuppression
            );
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to begin BPP overlay suppression scope: {ex.Message}"
            );
            return null;
        }
    }

    private void DisposeUiAndSpeedState()
    {
        if (_uiSuppressionScope != null)
        {
            try
            {
                _uiSuppressionScope.Dispose();
            }
            catch
            {
                // ignore
            }
            _uiSuppressionScope = null;
        }

        RestoreCombatSpeed();
    }

    private ReplayVideoCaptureRequest? BuildCaptureRequest(
        CombatReplayPlaybackStarting evt,
        IBppServices services,
        string ffmpegExecutable,
        string videoDirectoryPath
    )
    {
        var config = services.Config;
        var fps = Math.Clamp(config.CombatReplayVideoFps?.Value ?? 30, 10, 60);
        var crf = Math.Clamp(config.CombatReplayVideoCrf?.Value ?? 23, 0, 51);
        var preset = config.CombatReplayVideoPreset?.Value ?? "veryfast";
        if (string.IsNullOrWhiteSpace(preset))
            preset = "veryfast";

        var configuredWidth = config.CombatReplayVideoWidth?.Value ?? 0;
        var configuredHeight = config.CombatReplayVideoHeight?.Value ?? 0;
        var width = configuredWidth > 0 ? configuredWidth : Screen.width;
        var height = configuredHeight > 0 ? configuredHeight : Screen.height;
        if (width <= 0 || height <= 0)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"Cannot record video with invalid size {width}x{height}; skipping."
            );
            return null;
        }

        // Round to even dimensions for yuv420p compatibility.
        if ((width & 1) != 0)
            width--;
        if ((height & 1) != 0)
            height--;

        var maxQueued = Math.Max(8, config.CombatReplayVideoMaxQueuedFrames?.Value ?? 90);
        var forceSpeed1x = config.CombatReplayVideoForceSpeed1x?.Value ?? true;
        var suppressOverlays = config.CombatReplayVideoSuppressBppOverlays?.Value ?? true;

        var nowLocal = DateTimeOffset.Now;
        var datePart = nowLocal.ToString("yyyy-MM-dd");
        var stampPart = nowLocal.ToString("yyyyMMdd-HHmmss");
        var sanitizedBattleId = SanitizeForPath(evt.BattleId);
        var outputDirectory = Path.Combine(videoDirectoryPath, datePart);
        var finalFileName = $"{sanitizedBattleId}.{stampPart}.mp4";
        var tempFileName = $"{sanitizedBattleId}.{stampPart}.recording.mp4";
        var outputFilePath = Path.Combine(outputDirectory, tempFileName);

        return new ReplayVideoCaptureRequest
        {
            BattleId = evt.BattleId,
            Source = evt.Source,
            FfmpegExecutable = ffmpegExecutable,
            OutputFilePath = outputFilePath,
            OutputDirectoryPath = outputDirectory,
            Width = width,
            Height = height,
            Fps = fps,
            Crf = crf,
            Preset = preset,
            MaxQueuedFrames = maxQueued,
            ForceSpeed1x = forceSpeed1x,
            SuppressBppOverlays = suppressOverlays,
        };
    }

    private void FinalizeOutputFile(ReplayVideoCaptureResult result)
    {
        if (result.Status != ReplayVideoCaptureStatus.Completed)
        {
            DeleteTempFile();
            return;
        }

        var tempPath = _activeRecordingTempPath ?? result.OutputFilePath;
        var finalPath = _activeRecordingFinalPath ?? StripTempSuffix(result.OutputFilePath);

        try
        {
            if (!File.Exists(tempPath))
                return;

            var finalDir = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrWhiteSpace(finalDir))
                Directory.CreateDirectory(finalDir);

            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(tempPath, finalPath);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"Failed to rename recording '{tempPath}' to '{finalPath}': {ex.Message}"
            );
        }
        finally
        {
            _activeRecordingTempPath = null;
            _activeRecordingFinalPath = null;
        }
    }

    private void DeleteTempFile()
    {
        var tempPath = _activeRecordingTempPath;
        if (string.IsNullOrWhiteSpace(tempPath))
            return;

        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to delete temp recording '{tempPath}': {ex.Message}"
            );
        }
    }

    private static string StripTempSuffix(string tempPath)
    {
        const string suffix = ".recording.mp4";
        if (string.IsNullOrEmpty(tempPath) || !tempPath.EndsWith(suffix, StringComparison.Ordinal))
            return tempPath;

        return tempPath.Substring(0, tempPath.Length - suffix.Length) + ".mp4";
    }

    private static string SanitizeForPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }
}
