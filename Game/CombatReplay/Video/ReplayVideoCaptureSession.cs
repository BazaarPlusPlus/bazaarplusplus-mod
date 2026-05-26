#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoCaptureSession : IDisposable
{
    private readonly ReplayVideoCaptureRequest _request;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly double _frameInterval;
    private readonly Dictionary<int, byte[]?> _orderingBuffer = new();
    private readonly object _disposeLock = new();
    private RenderTexture? _captureRenderTexture;
    private FfmpegRawVideoEncoder? _encoder;
    private double _nextCaptureTime;
    private int _issuedSequence;
    private int _nextExpectedSequence = 1;
    private int _outstandingReadbackCount;
    private int _capturedFrames;
    private int _droppedFrames;
    private bool _started;
    private bool _disposed;
    private bool _finalized;
    private string? _failureReason;

    public ReplayVideoCaptureSession(ReplayVideoCaptureRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _startedAtUtc = DateTimeOffset.UtcNow;
        _frameInterval = 1.0 / Math.Max(1, request.Fps);
    }

    public ReplayVideoCaptureRequest Request => _request;

    public bool IsActive => _started && !_finalized && !_disposed && _failureReason == null;

    public int CapturedFrames => _capturedFrames;

    public int DroppedFrames => _droppedFrames;

    public void Start()
    {
        if (_started)
            throw new InvalidOperationException("Session is already started.");

        EnsureOutputDirectory();

        _captureRenderTexture = new RenderTexture(
            _request.Width,
            _request.Height,
            depth: 0,
            format: RenderTextureFormat.ARGB32
        )
        {
            name = "BPP_CombatReplayVideoCapture",
            useMipMap = false,
            autoGenerateMips = false,
        };
        if (!_captureRenderTexture.Create())
        {
            UnityEngine.Object.Destroy(_captureRenderTexture);
            _captureRenderTexture = null;
            throw new InvalidOperationException(
                $"Failed to create RenderTexture {_request.Width}x{_request.Height} for replay video capture."
            );
        }

        _encoder = new FfmpegRawVideoEncoder(
            _request.FfmpegExecutable,
            _request.OutputFilePath,
            _request.Width,
            _request.Height,
            _request.Fps,
            _request.Crf,
            _request.Preset,
            _request.MaxQueuedFrames
        );

        try
        {
            _encoder.Start();
        }
        catch
        {
            _encoder.Dispose();
            _encoder = null;
            ReleaseRenderTexture();
            throw;
        }

        _nextCaptureTime = Time.unscaledTimeAsDouble;
        _started = true;
    }

    public void CaptureFrameIfDue()
    {
        if (!IsActive || _encoder == null || _captureRenderTexture == null)
            return;

        var encoder = _encoder;
        if (encoder.WriterFailed)
        {
            _failureReason ??= encoder.FailureReason ?? "Encoder writer reported a failure.";
            return;
        }

        var now = Time.unscaledTimeAsDouble;
        var capturesThisTick = 0;
        const int maxCapturesPerTick = 3;
        while (now >= _nextCaptureTime && capturesThisTick < maxCapturesPerTick)
        {
            if (!TryRequestReadback())
                break;

            _nextCaptureTime += _frameInterval;
            capturesThisTick++;
        }

        if (now - _nextCaptureTime > _frameInterval * 5)
        {
            _nextCaptureTime = now + _frameInterval;
        }
    }

    public ReplayVideoCaptureResult Finalize(string endReason)
    {
        if (_finalized)
            return BuildResult(endReason);

        _finalized = true;

        try
        {
            AsyncGPUReadback.WaitAllRequests();
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"AsyncGPUReadback.WaitAllRequests threw during finalize: {ex.Message}"
            );
        }

        FlushOrderingBuffer(force: true);

        var encoder = _encoder;
        if (encoder != null)
        {
            try
            {
                var success = encoder.WaitForCompletion(TimeSpan.FromSeconds(20));
                if (!success)
                {
                    _failureReason ??= encoder.FailureReason ?? "FFmpeg failed to finalize within timeout.";
                }
            }
            catch (Exception ex)
            {
                _failureReason ??= $"Encoder finalize crashed: {ex.GetType().Name} {ex.Message}";
                BppLog.Error("CombatReplayVideo", "Encoder finalize crashed.", ex);
            }
        }

        ReleaseRenderTexture();

        var result = BuildResult(endReason);
        BppLog.Info(
            "CombatReplayVideo",
            $"Replay video capture finalized status={result.Status} frames={result.CapturedFrames} dropped={result.DroppedFrames} duration_ms={result.DurationMs} size_bytes={result.FileSizeBytes} file={result.OutputFilePath}"
        );
        return result;
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        try
        {
            if (!_finalized)
            {
                _failureReason ??= "Session disposed without finalize.";
                _encoder?.Dispose();
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _encoder = null;
            ReleaseRenderTexture();
        }
    }

    private bool TryRequestReadback()
    {
        var rt = _captureRenderTexture;
        if (rt == null)
            return false;

        try
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
            var sequenceNumber = Interlocked.Increment(ref _issuedSequence);
            Interlocked.Increment(ref _outstandingReadbackCount);
            AsyncGPUReadback.Request(rt, 0, request => OnReadbackComplete(request, sequenceNumber));
            return true;
        }
        catch (Exception ex)
        {
            _failureReason ??= $"ScreenCapture failed: {ex.GetType().Name} {ex.Message}";
            BppLog.Error("CombatReplayVideo", "ScreenCapture.CaptureScreenshotIntoRenderTexture failed.", ex);
            return false;
        }
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest request, int sequenceNumber)
    {
        Interlocked.Decrement(ref _outstandingReadbackCount);

        if (_disposed)
            return;

        if (request.hasError)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"AsyncGPUReadback request {sequenceNumber} returned with error."
            );
            _orderingBuffer[sequenceNumber] = null;
            _droppedFrames++;
            FlushOrderingBuffer(force: false);
            return;
        }

        try
        {
            var data = request.GetData<byte>();
            var buffer = new byte[data.Length];
            data.CopyTo(buffer);
            ReplayVideoFrameTransforms.FlipVerticalRgba32(buffer, _request.Width, _request.Height);
            _orderingBuffer[sequenceNumber] = buffer;
        }
        catch (Exception ex)
        {
            _orderingBuffer[sequenceNumber] = null;
            _droppedFrames++;
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to copy readback {sequenceNumber}: {ex.GetType().Name} {ex.Message}"
            );
        }

        FlushOrderingBuffer(force: false);
    }

    private void FlushOrderingBuffer(bool force)
    {
        var encoder = _encoder;
        if (encoder == null)
            return;

        while (_orderingBuffer.TryGetValue(_nextExpectedSequence, out var frame))
        {
            _orderingBuffer.Remove(_nextExpectedSequence);
            _nextExpectedSequence++;

            if (frame == null)
                continue;

            if (encoder.WriterFailed)
            {
                _failureReason ??= encoder.FailureReason ?? "Encoder writer failed.";
                _droppedFrames++;
                continue;
            }

            if (encoder.TryEnqueueFrame(frame))
            {
                _capturedFrames++;
            }
            else
            {
                _droppedFrames++;
            }
        }

        if (force && _orderingBuffer.Count > 0)
        {
            foreach (var kv in _orderingBuffer)
            {
                if (kv.Value != null)
                    _droppedFrames++;
            }
            _orderingBuffer.Clear();
        }
    }

    private void ReleaseRenderTexture()
    {
        var rt = _captureRenderTexture;
        if (rt == null)
            return;

        _captureRenderTexture = null;
        try
        {
            if (rt.IsCreated())
                rt.Release();
            UnityEngine.Object.Destroy(rt);
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to release capture RenderTexture: {ex.Message}"
            );
        }
    }

    private void EnsureOutputDirectory()
    {
        var directory = Path.GetDirectoryName(_request.OutputFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private ReplayVideoCaptureResult BuildResult(string endReason)
    {
        var endedAt = DateTimeOffset.UtcNow;
        var durationMs = (long)Math.Max(0, (endedAt - _startedAtUtc).TotalMilliseconds);
        var fileSize = FfmpegRawVideoEncoder.TryGetFileSize(_request.OutputFilePath);

        var status =
            _failureReason != null
                ? ReplayVideoCaptureStatus.Failed
                : (_capturedFrames > 0
                    ? ReplayVideoCaptureStatus.Completed
                    : ReplayVideoCaptureStatus.Failed);
        var error = _failureReason;
        if (status == ReplayVideoCaptureStatus.Failed && error == null && _capturedFrames == 0)
            error = $"No frames captured before {endReason}.";

        return new ReplayVideoCaptureResult
        {
            VideoId = _request.VideoId,
            BattleId = _request.BattleId,
            Source = _request.Source,
            OutputFilePath = _request.OutputFilePath,
            Width = _request.Width,
            Height = _request.Height,
            Fps = _request.Fps,
            Codec = "libx264",
            Crf = _request.Crf,
            Preset = _request.Preset,
            StartedAtUtc = _startedAtUtc,
            EndedAtUtc = endedAt,
            DurationMs = durationMs,
            CapturedFrames = _capturedFrames,
            DroppedFrames = _droppedFrames,
            FileSizeBytes = fileSize,
            Status = status,
            Error = error,
        };
    }
}
