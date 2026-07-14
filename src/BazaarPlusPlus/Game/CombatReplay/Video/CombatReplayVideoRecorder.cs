#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay.Audio;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.Rendering;

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
    private string? _activeRecordingTempPath;
    private string? _activeRecordingFinalPath;
    private readonly List<IReplayAudioCaptureTap> _audioTaps = new();
    private List<string>? _activeAudioWavPaths;
    private string? _activeFfmpegExecutable;
    private ReplayVideoAudioMuxer? _muxer;
    private readonly ReplayVideoRecordingLifecycle _operations = new();
    private ReplayVideoRecordingOperation? _activeOperation;
    private ReplayVideoAudioStatus _activeAudioStatus = ReplayVideoAudioStatus.Silent;
    private ReplayVideoMetadataStatus _activeMetadataStatus = ReplayVideoMetadataStatus.Unavailable;
    private ReplayVideoRecordingReasonCode? _activeDegradationReason;
    private Exception? _activeDegradationException;
    private Exception? _metadataInitializationException;

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out _);

        var runLogDatabasePath = services.Paths.RunLogDatabasePath;
        if (!string.IsNullOrWhiteSpace(runLogDatabasePath))
        {
            try
            {
                _metadataStore = new CombatReplayVideoMetadataStore(runLogDatabasePath);
            }
            catch (Exception ex)
            {
                _metadataInitializationException = ex;
                _metadataStore = null;
            }
        }

        // ComponentMount.Mount calls AddComponent (which fires OnEnable synchronously on an
        // active host) BEFORE this initializer runs, so the first OnEnable saw a null _services
        // and could not subscribe. Now that services are available, ensure we are subscribed.
        if (isActiveAndEnabled)
            EnsureEventSubscriptions();
    }

    private void OnEnable()
    {
        EnsureEventSubscriptions();
    }

    private void EnsureEventSubscriptions()
    {
        var services = _services;
        if (services == null || _startingSubscription != null)
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

        // Best-effort drain of any in-flight background mux tasks so a recording
        // that just ended gets a chance to produce its final file before the app
        // tears down. This is the only viable shutdown seam (no Application.quitting
        // hook); un-drained temps are acceptable and reclaimed on the next launch.
        try
        {
            if (!ReplayVideoAudioMuxer.TryDrainPendingForShutdown(TimeSpan.FromMilliseconds(4000)))
            {
                BppLog.DebugEvent(
                    CombatReplayVideoLogEvents.RecordingLifecycleObserved,
                    () =>
                        [
                            CombatReplayVideoLogEvents.LifecycleStage.Bind(
                                ReplayVideoLogStage.MuxDrain
                            ),
                            CombatReplayVideoLogEvents.LifecycleRecordingId.Bind(null),
                            CombatReplayVideoLogEvents.LifecycleBattleId.Bind(null),
                            CombatReplayVideoLogEvents.LifecyclePendingCount.Bind(
                                _operations.Count
                            ),
                        ]
                );
            }
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.VideoMuxDiagnosticObserved,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.MuxRecordingId.Bind(null),
                        CombatReplayVideoLogEvents.MuxStage.Bind(ReplayVideoLogStage.MuxDrain),
                        CombatReplayVideoLogEvents.MuxReasonCode.Bind(
                            ReplayVideoDiagnosticReasonCode.DrainFailed
                        ),
                        CombatReplayVideoLogEvents.MuxPath.Bind(null),
                        CombatReplayVideoLogEvents.MuxPendingCount.Bind(_operations.Count),
                    ]
            );
        }
        finally
        {
            // A mux callback is best-effort inside the muxer. Sweep even after a successful
            // drain so a callback exception can never strand a registered operation.
            _operations.CompletePending(ReplayVideoRecordingReasonCode.ShutdownTimeout);
        }
    }

    private void OnPlaybackStarting(CombatReplayPlaybackStarting evt)
    {
        if (evt == null || string.IsNullOrWhiteSpace(evt.BattleId))
            return;

        if (_activeOperation != null)
            AbortActiveSession("superseded");

        if (!evt.RecordVideo)
            return;

        var operation = _operations.Start(evt.BattleId, evt.Source, DateTimeOffset.UtcNow);

        try
        {
            var services = _services;
            if (services == null)
            {
                CompletePreflightFailure(
                    operation,
                    ReplayVideoRecordingReasonCode.OutputPathUnavailable
                );
                return;
            }

            var pluginsDirectoryPath = services.Paths.PluginsDirectoryPath;
            var gate = CombatReplayRecordingGate.Evaluate(
                pluginsDirectoryPath,
                services.Paths.CombatReplayVideoDirectoryPath
            );
            if (!gate.CanRecord)
            {
                CompletePreflightFailure(operation, MapGateBlocker(gate.Blocker));
                return;
            }

            var request = BuildCaptureRequest(
                operation.RecordingId,
                evt,
                gate.FfmpegExecutable!,
                gate.VideoDirectoryPath!
            );
            if (request == null)
            {
                CompletePreflightFailure(
                    operation,
                    ReplayVideoRecordingReasonCode.InvalidDimensions
                );
                return;
            }

            BeginRecording(operation, request, services);
        }
        catch (Exception ex)
        {
            _activeDegradationException = ex;
            AbortActiveSession("begin-exception");
            if (!operation.IsCompleted)
            {
                _operations.TryComplete(
                    operation,
                    new ReplayVideoRecordingCompletion
                    {
                        ReasonCode = ReplayVideoRecordingReasonCode.BeginException,
                        AudioStatus = ReplayVideoAudioStatus.Failed,
                        MetadataStatus = ReplayVideoMetadataStatus.Unavailable,
                        Exception = ex,
                    }
                );
            }
        }
    }

    private void OnPlaybackEnded(CombatReplayPlaybackEnded evt)
    {
        if (evt == null || _activeSession == null || _activeOperation == null)
            return;

        var session = _activeSession;
        var operation = _activeOperation;
        var reason = evt.Reason ?? (evt.Failed ? "playback-failed" : "playback-ended");
        ReplayVideoCaptureResult? result = null;

        try
        {
            StopCaptureCoroutine();
            result = session.Finalize(reason);
            // Closes + unlocks the WAVs before the muxer reads them, returning
            // only paths whose tap actually pushed PCM. Header-only WAVs are
            // deleted inside ReplayAudioTapStopper.
            var wavPaths = ReplayAudioTapStopper.StopAndCollectUsableWavPaths(
                _audioTaps,
                operation.RecordingId,
                out var audioResults
            );
            ApplyAudioStopOutcomes(audioResults, wavPaths.Count > 0);

            // Capture locals before nulling instance fields: the mux runs on a
            // background thread after this method returns, so it must not read
            // mutable recorder state.
            var tempVideoPath = _activeRecordingTempPath ?? result.OutputFilePath;
            var finalPath = _activeRecordingFinalPath ?? StripTempSuffix(result.OutputFilePath);
            var ffmpegExecutable = _activeFfmpegExecutable;
            var videoDir = _services?.Paths.CombatReplayVideoDirectoryPath;
            var store = _metadataStore;
            var audioStatus = _activeAudioStatus;
            var metadataStatus = _activeMetadataStatus;
            var degradationReason = _activeDegradationReason;
            var degradationException = _activeDegradationException;

            DisposeUiState();
            ClearActiveState();

            _muxer ??= new ReplayVideoAudioMuxer();
            _muxer.Resolve(
                operation.RecordingId,
                result.Status,
                tempVideoPath,
                finalPath,
                wavPaths,
                ffmpegExecutable,
                mux =>
                {
                    try
                    {
                        var resolvedMetadata = TrySaveFinishMetadataFor(
                            store,
                            videoDir,
                            mux.FinalFilePath,
                            result,
                            mux.Status != ReplayVideoAudioMuxer.MuxStatus.Failed,
                            mux.FileSizeBytes,
                            metadataStatus
                        );
                        _operations.CompleteResolved(
                            operation,
                            result,
                            mux,
                            audioStatus,
                            resolvedMetadata.Status,
                            degradationReason,
                            degradationException ?? resolvedMetadata.Exception
                        );
                    }
                    catch (Exception ex)
                    {
                        _operations.CompleteMuxCallbackFailure(
                            operation,
                            result,
                            mux,
                            audioStatus,
                            metadataStatus,
                            ex
                        );
                    }
                }
            );
        }
        catch (Exception ex)
        {
            var wavPaths = _activeAudioWavPaths;
            ReplayAudioTapStopper.StopAndCollectUsableWavPaths(
                _audioTaps,
                operation.RecordingId,
                out _
            );
            try
            {
                session.Dispose();
            }
            catch
            {
                // ignore
            }
            DisposeUiState();
            DeleteTempFile();
            DeleteWavBestEffort(wavPaths);
            var finalPath = _activeRecordingFinalPath ?? string.Empty;
            ClearActiveState();
            _operations.TryComplete(
                operation,
                new ReplayVideoRecordingCompletion
                {
                    FinalFilePath = finalPath,
                    CapturedFrames = result?.CapturedFrames ?? 0,
                    DroppedFrames = result?.DroppedFrames ?? 0,
                    AudioStatus = ReplayVideoAudioStatus.Failed,
                    MetadataStatus = ReplayVideoMetadataStatus.Failed,
                    ReasonCode =
                        result?.ReasonCode is { } resultReason
                        && resultReason != ReplayVideoRecordingReasonCode.Completed
                            ? resultReason
                            : ReplayVideoRecordingReasonCode.CaptureFailed,
                    ExitCode = result?.ExitCode,
                    StderrTail = result?.StderrTail,
                    Exception = ex,
                }
            );
        }
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

    private static void DeleteWavBestEffort(IReadOnlyList<string>? wavPaths)
    {
        if (wavPaths == null)
            return;

        foreach (var wavPath in wavPaths)
            DeleteWavBestEffort(wavPath);
    }

    private void CompletePreflightFailure(
        ReplayVideoRecordingOperation operation,
        ReplayVideoRecordingReasonCode reasonCode
    )
    {
        _operations.CompletePreflight(operation, reasonCode);
    }

    private static ReplayVideoRecordingReasonCode MapGateBlocker(
        CombatReplayRecordingBlocker blocker
    ) =>
        blocker switch
        {
            CombatReplayRecordingBlocker.NoAsyncGpuReadback =>
                ReplayVideoRecordingReasonCode.AsyncGpuReadbackUnavailable,
            CombatReplayRecordingBlocker.FfmpegUnavailable =>
                ReplayVideoRecordingReasonCode.FfmpegUnavailable,
            CombatReplayRecordingBlocker.VideoDirectoryUnset =>
                ReplayVideoRecordingReasonCode.OutputPathUnavailable,
            _ => ReplayVideoRecordingReasonCode.CaptureFailed,
        };

    private void ApplyAudioStopOutcomes(
        IReadOnlyList<ReplayAudioCaptureResult> results,
        bool hasUsableAudio
    )
    {
        ReplayAudioCaptureResult? firstFailure = null;
        for (var index = 0; index < results.Count; index++)
        {
            if (results[index].FailureReason != ReplayAudioFailureReasonCode.None)
            {
                firstFailure = results[index];
                break;
            }
        }

        if (firstFailure.HasValue)
        {
            _activeAudioStatus = ReplayVideoAudioStatus.Failed;
            _activeDegradationReason ??= ReplayVideoRecordingReasonCode.AudioStopFailed;
            _activeDegradationException ??= firstFailure.Value.FailureException;
            return;
        }

        _activeAudioStatus = hasUsableAudio
            ? ReplayVideoAudioStatus.Full
            : ReplayVideoAudioStatus.Silent;
        if (!hasUsableAudio)
            _activeDegradationReason ??= ReplayVideoRecordingReasonCode.AudioUnavailable;
    }

    private void ClearActiveState()
    {
        _activeSession = null;
        _activeOperation = null;
        _captureCoroutine = null;
        _activeRecordingTempPath = null;
        _activeRecordingFinalPath = null;
        _activeAudioWavPaths = null;
        _activeFfmpegExecutable = null;
        _activeAudioStatus = ReplayVideoAudioStatus.Silent;
        _activeMetadataStatus = ReplayVideoMetadataStatus.Unavailable;
        _activeDegradationReason = null;
        _activeDegradationException = null;
    }

    private void BeginRecording(
        ReplayVideoRecordingOperation operation,
        ReplayVideoCaptureRequest request,
        IBppServices services
    )
    {
        var session = new ReplayVideoCaptureSession(request);
        _activeOperation = operation;
        _activeSession = session;
        _activeRecordingTempPath = request.OutputFilePath;
        _activeRecordingFinalPath = StripTempSuffix(request.OutputFilePath);
        _activeFfmpegExecutable = request.FfmpegExecutable;
        _activeAudioStatus = ReplayVideoAudioStatus.Silent;
        _activeMetadataStatus = ReplayVideoMetadataStatus.Unavailable;
        _activeDegradationReason = null;
        _activeDegradationException = null;
        try
        {
            session.Start();
        }
        catch
        {
            try
            {
                session.Dispose();
            }
            catch { }
            throw;
        }

        StartAudioTaps(operation, request.OutputFilePath);

        _uiSuppressionScope = BeginUiSuppression(operation.RecordingId);

        _activeMetadataStatus = TrySaveStartMetadata(request, services);
        if (_activeMetadataStatus != ReplayVideoMetadataStatus.Complete)
        {
            _activeDegradationReason ??= ReplayVideoRecordingReasonCode.MetadataFailed;
            _activeDegradationException ??= _metadataInitializationException;
        }

        _captureCoroutine = StartCoroutine(CaptureLoop(session));

        BppLog.DebugEvent(
            CombatReplayVideoLogEvents.RecordingLifecycleObserved,
            () =>
                [
                    CombatReplayVideoLogEvents.LifecycleStage.Bind(
                        ReplayVideoLogStage.SessionStarted
                    ),
                    CombatReplayVideoLogEvents.LifecycleRecordingId.Bind(operation.RecordingId),
                    CombatReplayVideoLogEvents.LifecycleBattleId.Bind(operation.BattleId),
                    CombatReplayVideoLogEvents.LifecyclePendingCount.Bind(_operations.Count),
                ]
        );
    }

    private void StartAudioTaps(ReplayVideoRecordingOperation operation, string tempVideoPath)
    {
        // Audio is additive: capture failure must never abort the video recording. Capture the device
        // output (loopback) so we record exactly what the player hears — music, settlement, and the
        // spatialised combat/board SFX that no FMOD channel group exposes.
        var wavPath = ReplayVideoAudioTapPlan.DeriveAudioWavPath(tempVideoPath);
        _activeAudioWavPaths = new List<string> { wavPath };
        TryStartAudioTap(operation, wavPath);

        if (_audioTaps.Count == 0)
        {
            DeleteWavBestEffort(_activeAudioWavPaths);
            _activeAudioWavPaths = null;
        }
    }

    private void TryStartAudioTap(ReplayVideoRecordingOperation operation, string wavPath)
    {
        try
        {
            IReplayAudioCaptureTap tap = ReplayAudioCaptureFactory.Create(wavPath);
            var outcome = tap.TryStart();
            if (outcome.Started)
            {
                _audioTaps.Add(tap);
                _activeAudioStatus = ReplayVideoAudioStatus.Full;
                BppLog.DebugEvent(
                    CombatReplayVideoLogEvents.AudioCaptureStarted,
                    () =>
                        [
                            CombatReplayVideoLogEvents.AudioStartedRecordingId.Bind(
                                operation.RecordingId
                            ),
                            CombatReplayVideoLogEvents.AudioStartedBackend.Bind(tap.Backend),
                            CombatReplayVideoLogEvents.AudioStartedSampleRate.Bind(
                                tap.SampleRateHz
                            ),
                            CombatReplayVideoLogEvents.AudioStartedChannels.Bind(tap.Channels),
                            CombatReplayVideoLogEvents.AudioStartedSampleFormat.Bind(
                                tap.SampleFormat
                            ),
                        ]
                );
                return;
            }

            _activeAudioStatus = ReplayVideoAudioStatus.Silent;
            _activeDegradationReason =
                outcome.ReasonCode == ReplayAudioFailureReasonCode.UnsupportedPlatform
                    ? ReplayVideoRecordingReasonCode.AudioUnavailable
                    : ReplayVideoRecordingReasonCode.AudioCaptureFailed;
            _activeDegradationException = outcome.Exception;
            tap.Dispose();
            DeleteWavBestEffort(wavPath);
        }
        catch (Exception ex)
        {
            _activeAudioStatus = ReplayVideoAudioStatus.Silent;
            _activeDegradationReason = ReplayVideoRecordingReasonCode.AudioCaptureFailed;
            _activeDegradationException = ex;
            DeleteWavBestEffort(wavPath);
        }
    }

    private ReplayVideoMetadataStatus TrySaveStartMetadata(
        ReplayVideoCaptureRequest request,
        IBppServices services
    )
    {
        var store = _metadataStore;
        if (store == null)
            return ReplayVideoMetadataStatus.Unavailable;

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
                    Codec = request.EncoderProfile.Codec,
                    Crf = request.EncoderProfile.Crf,
                    Preset = request.EncoderProfile.Preset,
                    StartedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            return ReplayVideoMetadataStatus.Complete;
        }
        catch (Exception ex)
        {
            _activeDegradationException = ex;
            return ReplayVideoMetadataStatus.Failed;
        }
    }

    // Parameterized so it is race-free under the async mux: every input is a value
    // captured on the main thread before instance fields are nulled. Safe to call on
    // a background thread because the store opens a fresh SQLite connection per call.
    // FileSizeBytes always comes from the resolved final path, never the first-pass temp.
    private MetadataWriteOutcome TrySaveFinishMetadataFor(
        CombatReplayVideoMetadataStore? store,
        string? videoDir,
        string finalPath,
        ReplayVideoCaptureResult result,
        bool finalResolutionSucceeded,
        long finalFileSize,
        ReplayVideoMetadataStatus initialStatus
    )
    {
        if (store == null)
            return new MetadataWriteOutcome(initialStatus, null);

        try
        {
            var relativePath = ComputeRelativePath(videoDir, finalPath);
            var endedAt = result.EndedAtUtc ?? DateTimeOffset.UtcNow;
            var status = ReplayVideoMetadataResolution.ResolvePersistedStatus(
                result.Status,
                finalResolutionSucceeded,
                finalFileSize
            );

            store.SaveFinish(
                new VideoRecordingFinished
                {
                    VideoId = result.VideoId,
                    VideoRelativePath = relativePath,
                    EndedAtUtc = endedAt,
                    DurationMs = result.DurationMs,
                    CapturedFrames = result.CapturedFrames,
                    DroppedFrames = result.DroppedFrames,
                    FileSizeBytes = finalFileSize,
                    Status = status,
                    Error = result.Error,
                }
            );
            return new MetadataWriteOutcome(
                initialStatus == ReplayVideoMetadataStatus.Complete
                    ? ReplayVideoMetadataStatus.Complete
                    : ReplayVideoMetadataStatus.Failed,
                null
            );
        }
        catch (Exception ex)
        {
            return new MetadataWriteOutcome(ReplayVideoMetadataStatus.Failed, ex);
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
        var operation = _activeOperation;
        ReplayVideoCaptureResult? result = null;
        var terminalReason = reason switch
        {
            "superseded" => ReplayVideoRecordingReasonCode.Superseded,
            "begin-exception" => ReplayVideoRecordingReasonCode.BeginException,
            _ => ReplayVideoRecordingReasonCode.Aborted,
        };
        var terminalException = _activeDegradationException;

        StopCaptureCoroutine();
        // Tear the tap down synchronously: stop + join before any new recording's
        // capture thread starts (covers superseded, OnDisable, OnDestroy, and the
        // scene-change-driven OnDisable). Abort never muxes — it deletes temps.
        var wavPaths = _activeAudioWavPaths;
        if (operation != null)
        {
            var usable = ReplayAudioTapStopper.StopAndCollectUsableWavPaths(
                _audioTaps,
                operation.RecordingId,
                out var audioResults
            );
            ApplyAudioStopOutcomes(audioResults, usable.Count > 0);
        }

        if (session != null)
        {
            var finalResolutionSucceeded = false;
            try
            {
                result = session.Finalize(reason);
                var tempPath = _activeRecordingTempPath ?? result.OutputFilePath;
                var finalPath = _activeRecordingFinalPath ?? StripTempSuffix(result.OutputFilePath);
                if (result.Status == ReplayVideoCaptureStatus.Completed)
                {
                    try
                    {
                        ReplayVideoAudioMuxer.PromoteSilentToFinal(tempPath, finalPath);
                        finalResolutionSucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        terminalReason = ReplayVideoRecordingReasonCode.PromotionFailed;
                        terminalException = ex;
                    }
                }
                else if (!string.Equals(reason, "begin-exception", StringComparison.Ordinal))
                {
                    terminalReason = result.ReasonCode;
                }
                var finalFileSize = FfmpegRawVideoEncoder.TryGetFileSize(finalPath);
                var metadataOutcome = TrySaveFinishMetadataFor(
                    _metadataStore,
                    _services?.Paths.CombatReplayVideoDirectoryPath,
                    finalPath,
                    result,
                    finalResolutionSucceeded,
                    finalFileSize,
                    _activeMetadataStatus
                );
                _activeMetadataStatus = metadataOutcome.Status;
                terminalException ??= metadataOutcome.Exception;
            }
            catch (Exception ex)
            {
                terminalReason = ReplayVideoRecordingReasonCode.CaptureFailed;
                terminalException = ex;
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

        DisposeUiState();
        DeleteTempFile();
        DeleteWavBestEffort(wavPaths);
        var finalOutputPath = _activeRecordingFinalPath ?? string.Empty;
        var audioStatus = _activeAudioStatus;
        var metadataStatus = _activeMetadataStatus;
        ClearActiveState();
        if (operation != null)
        {
            _operations.TryComplete(
                operation,
                new ReplayVideoRecordingCompletion
                {
                    FinalFilePath = finalOutputPath,
                    CapturedFrames = result?.CapturedFrames ?? 0,
                    DroppedFrames = result?.DroppedFrames ?? 0,
                    AudioStatus = audioStatus,
                    MetadataStatus = metadataStatus,
                    ReasonCode = terminalReason,
                    ExitCode = result?.ExitCode,
                    StderrTail = result?.StderrTail,
                    Exception = terminalException ?? result?.Exception,
                }
            );
        }
    }

    private static IDisposable? BeginUiSuppression(string recordingId)
    {
        try
        {
            // The combat status bar is intentionally NOT suppressed here: during a recorded replay
            // it stays visible just like in a normal replay, so it is captured into the MP4 (the
            // recorder uses full-screen ScreenCapture). The remaining BPP overlays stay suppressed
            // to keep them out of the recording.
            return BppUiChromeSuppression.Begin(BppUiChromeSuppressionMode.ReplayRecording);
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.RecordingCleanupFailed,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.CleanupRecordingId.Bind(recordingId),
                        CombatReplayVideoLogEvents.CleanupStage.Bind(
                            ReplayVideoLogStage.UiSuppression
                        ),
                        CombatReplayVideoLogEvents.CleanupPath.Bind(null),
                    ]
            );
            return null;
        }
    }

    private void DisposeUiState()
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
    }

    private ReplayVideoCaptureRequest? BuildCaptureRequest(
        string recordingId,
        CombatReplayPlaybackStarting evt,
        string ffmpegExecutable,
        string videoDirectoryPath
    )
    {
        if (!ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out var captureSettings))
            return null;
        var fps = captureSettings.Fps;
        var width = captureSettings.Width;
        var height = captureSettings.Height;

        ReplayVideoBufferPlan bufferPlan;
        try
        {
            bufferPlan = ReplayVideoBufferPlan.Create(width, height);
        }
        catch
        {
            return null;
        }

        var encoderProfile = FfmpegVideoEncoderSelector.SelectOrPrewarm(
            ffmpegExecutable,
            videoDirectoryPath,
            width,
            height,
            fps
        );

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
            VideoId = recordingId,
            BattleId = evt.BattleId,
            Source = evt.Source,
            FfmpegExecutable = ffmpegExecutable,
            OutputFilePath = outputFilePath,
            OutputDirectoryPath = outputDirectory,
            Width = width,
            Height = height,
            Fps = fps,
            EncoderProfile = encoderProfile,
            BufferPlan = bufferPlan,
        };
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
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.RecordingCleanupFailed,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.CleanupRecordingId.Bind(
                            _activeOperation?.RecordingId
                        ),
                        CombatReplayVideoLogEvents.CleanupStage.Bind(
                            ReplayVideoLogStage.TempDelete
                        ),
                        CombatReplayVideoLogEvents.CleanupPath.Bind(tempPath),
                    ]
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

    private readonly struct MetadataWriteOutcome
    {
        internal MetadataWriteOutcome(ReplayVideoMetadataStatus status, Exception? exception)
        {
            Status = status;
            Exception = exception;
        }

        internal ReplayVideoMetadataStatus Status { get; }
        internal Exception? Exception { get; }
    }
}
