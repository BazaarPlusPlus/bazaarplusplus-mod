#nullable enable
using System;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunScreenshot;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class ScreenshotCaptureOperation
{
    private readonly object _syncRoot = new();
    private readonly long _startedAtMilliseconds;
    private bool _completed;
    private int _attemptCount;
    private ScreenshotCaptureReasonCode? _degradationReason;
    private Exception? _degradationException;
    private string? _verifiedArtifactPath;

    internal ScreenshotCaptureOperation(
        string screenshotId,
        string? runId,
        RunScreenshotCaptureSource captureSource,
        long startedAtMilliseconds
    )
    {
        ScreenshotId = screenshotId ?? throw new ArgumentNullException(nameof(screenshotId));
        RunId = runId;
        CaptureSource = captureSource;
        _startedAtMilliseconds = startedAtMilliseconds;
    }

    internal string ScreenshotId { get; }

    internal string? RunId { get; }

    internal RunScreenshotCaptureSource CaptureSource { get; }

    internal int BeginAttempt()
    {
        lock (_syncRoot)
        {
            if (_completed)
                return _attemptCount;
            return ++_attemptCount;
        }
    }

    internal void RecordDegradation(
        ScreenshotCaptureReasonCode reasonCode,
        Exception? exception = null
    )
    {
        lock (_syncRoot)
        {
            if (_completed || _degradationReason.HasValue)
                return;
            _degradationReason = reasonCode;
            _degradationException = exception;
        }
    }

    internal void RecordVerifiedArtifact(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Verified artifact path is required.", nameof(filePath));

        lock (_syncRoot)
        {
            if (_completed)
                return;
            _verifiedArtifactPath = filePath;
        }
    }

    internal bool TryCompleteContextReset(long completedAtMilliseconds)
    {
        ScreenshotCaptureTerminalKind kind;
        ScreenshotArtifactStatus artifactStatus;
        string? filePath;
        lock (_syncRoot)
        {
            if (_completed)
                return false;

            _completed = true;
            filePath = _verifiedArtifactPath;
            var artifactVerified = !string.IsNullOrWhiteSpace(filePath);
            kind = artifactVerified
                ? ScreenshotCaptureTerminalKind.Degraded
                : ScreenshotCaptureTerminalKind.Failed;
            artifactStatus = artifactVerified
                ? ScreenshotArtifactStatus.MetadataPending
                : ScreenshotArtifactStatus.Unavailable;
        }

        Emit(
            kind,
            ScreenshotCaptureReasonCode.ContextExpired,
            artifactStatus,
            filePath,
            exception: null,
            completedAtMilliseconds
        );
        return true;
    }

    internal bool RecordAttemptFailure(
        ScreenshotCaptureReasonCode reasonCode,
        Exception? exception,
        bool willRetry,
        long completedAtMilliseconds
    )
    {
        if (willRetry)
            return false;
        return TryComplete(
            ScreenshotCaptureTerminalKind.Failed,
            reasonCode,
            ScreenshotArtifactStatus.Unavailable,
            filePath: null,
            exception,
            completedAtMilliseconds
        );
    }

    internal bool TryCompleteArtifact(
        bool artifactVerified,
        string? filePath,
        ScreenshotMetadataPersistenceOutcome metadata,
        long completedAtMilliseconds
    )
    {
        if (!artifactVerified)
        {
            return TryComplete(
                ScreenshotCaptureTerminalKind.Failed,
                ScreenshotCaptureReasonCode.CaptureArtifactUnavailable,
                ScreenshotArtifactStatus.Unavailable,
                filePath,
                exception: null,
                completedAtMilliseconds
            );
        }

        ScreenshotCaptureTerminalKind kind;
        ScreenshotCaptureReasonCode reasonCode;
        ScreenshotArtifactStatus artifactStatus;
        Exception? exception;
        lock (_syncRoot)
        {
            if (_completed)
                return false;

            _verifiedArtifactPath = filePath;

            switch (metadata.Status)
            {
                case ScreenshotMetadataPersistenceStatus.Failed:
                    kind = ScreenshotCaptureTerminalKind.Degraded;
                    reasonCode = ScreenshotCaptureReasonCode.MetadataFailed;
                    artifactStatus = ScreenshotArtifactStatus.FileOnly;
                    exception = metadata.Exception;
                    break;
                case ScreenshotMetadataPersistenceStatus.TimedOut:
                    kind = ScreenshotCaptureTerminalKind.Degraded;
                    reasonCode = ScreenshotCaptureReasonCode.MetadataTimeout;
                    artifactStatus = ScreenshotArtifactStatus.MetadataPending;
                    exception = null;
                    break;
                case ScreenshotMetadataPersistenceStatus.Unavailable:
                    kind = ScreenshotCaptureTerminalKind.Degraded;
                    reasonCode = ScreenshotCaptureReasonCode.MetadataUnavailable;
                    artifactStatus = ScreenshotArtifactStatus.FileOnly;
                    exception = null;
                    break;
                default:
                    kind = _degradationReason.HasValue
                        ? ScreenshotCaptureTerminalKind.Degraded
                        : ScreenshotCaptureTerminalKind.Succeeded;
                    reasonCode = _degradationReason ?? ScreenshotCaptureReasonCode.Completed;
                    artifactStatus = ScreenshotArtifactStatus.Complete;
                    exception = _degradationException;
                    break;
            }

            _completed = true;
        }

        Emit(kind, reasonCode, artifactStatus, filePath, exception, completedAtMilliseconds);
        return true;
    }

    private bool TryComplete(
        ScreenshotCaptureTerminalKind kind,
        ScreenshotCaptureReasonCode reasonCode,
        ScreenshotArtifactStatus artifactStatus,
        string? filePath,
        Exception? exception,
        long completedAtMilliseconds
    )
    {
        lock (_syncRoot)
        {
            if (_completed)
                return false;
            _completed = true;
        }
        Emit(kind, reasonCode, artifactStatus, filePath, exception, completedAtMilliseconds);
        return true;
    }

    private void Emit(
        ScreenshotCaptureTerminalKind kind,
        ScreenshotCaptureReasonCode reasonCode,
        ScreenshotArtifactStatus artifactStatus,
        string? filePath,
        Exception? exception,
        long completedAtMilliseconds
    )
    {
        var fields = new[]
        {
            ScreenshotCaptureLogEvents.ScreenshotId.Bind(ScreenshotId),
            ScreenshotCaptureLogEvents.RunId.Bind(RunId),
            ScreenshotCaptureLogEvents.CaptureSource.Bind(CaptureSource),
            ScreenshotCaptureLogEvents.ReasonCode.Bind(reasonCode),
            ScreenshotCaptureLogEvents.ArtifactStatus.Bind(artifactStatus),
            ScreenshotCaptureLogEvents.AttemptCount.Bind(_attemptCount),
            ScreenshotCaptureLogEvents.DurationMs.Bind(
                Math.Max(0, completedAtMilliseconds - _startedAtMilliseconds)
            ),
            ScreenshotCaptureLogEvents.FilePath.Bind(filePath),
        };
        var definition = kind switch
        {
            ScreenshotCaptureTerminalKind.Succeeded => ScreenshotCaptureLogEvents.CaptureSucceeded,
            ScreenshotCaptureTerminalKind.Degraded => ScreenshotCaptureLogEvents.CaptureDegraded,
            _ => ScreenshotCaptureLogEvents.CaptureFailed,
        };
        if (kind == ScreenshotCaptureTerminalKind.Succeeded)
            BppLog.InfoEvent(definition, fields);
        else if (kind == ScreenshotCaptureTerminalKind.Degraded)
        {
            if (exception == null)
                BppLog.WarnEvent(definition, fields);
            else
                BppLog.WarnEvent(definition, exception, fields);
        }
        else if (exception == null)
            BppLog.ErrorEvent(definition, fields);
        else
            BppLog.ErrorEvent(definition, exception, fields);
    }

    private enum ScreenshotCaptureTerminalKind
    {
        Succeeded,
        Degraded,
        Failed,
    }
}
