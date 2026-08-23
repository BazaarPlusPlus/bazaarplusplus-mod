#nullable enable
using BazaarPlusPlus.GameInterop.Tooltips;

namespace BazaarPlusPlus.Game.Screenshots;

/// <summary>
/// Owns the independent native-tooltip and BPP-chrome suppression lifetimes for one capture.
/// </summary>
internal sealed class EndOfRunCaptureSuppressionLifecycle
{
    private readonly object _gate = new();
    private IDisposable? _bppSuppression;
    private INativeTooltipSuppressionLease? _nativeTooltipSuppression;
    private bool _closed;

    internal bool TryInstall(
        IDisposable? bppSuppression,
        INativeTooltipSuppressionLease nativeTooltipSuppression
    )
    {
        if (nativeTooltipSuppression == null)
            throw new ArgumentNullException(nameof(nativeTooltipSuppression));

        var alreadyInstalled = false;
        lock (_gate)
        {
            if (!_closed && _nativeTooltipSuppression == null)
            {
                _bppSuppression = bppSuppression;
                _nativeTooltipSuppression = nativeTooltipSuppression;
                return true;
            }
            alreadyInstalled = !_closed;
        }

        DisposeNative(nativeTooltipSuppression);
        DisposeBpp(bppSuppression);
        if (alreadyInstalled)
            throw new InvalidOperationException("Capture suppression is already installed.");
        return false;
    }

    internal EndOfRunCleanFrameDecision ObserveCleanFrame(
        EndOfRunCleanFramePreparationCore preparation,
        Func<EndOfRunCleanFrameVisualObservation> captureVisual,
        float nowSeconds
    )
    {
        if (preparation == null)
            throw new ArgumentNullException(nameof(preparation));
        if (captureVisual == null)
            throw new ArgumentNullException(nameof(captureVisual));

        INativeTooltipSuppressionLease? nativeTooltipSuppression;
        lock (_gate)
            nativeTooltipSuppression = _nativeTooltipSuppression;

        NativeTooltipCleanFrameAudit tooltipAudit;
        try
        {
            tooltipAudit =
                nativeTooltipSuppression?.AuditCleanFrame()
                ?? new NativeTooltipCleanFrameAudit(NativeTooltipCleanFrameState.Unavailable);
        }
        catch
        {
            ReleaseNative();
            return EndOfRunCleanFrameDecision.CaptureDegraded(
                ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable
            );
        }
        if (tooltipAudit.State == NativeTooltipCleanFrameState.Unavailable)
        {
            ReleaseNative();
            return EndOfRunCleanFrameDecision.CaptureDegraded(
                ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable
            );
        }

        EndOfRunCleanFrameVisualObservation visual;
        try
        {
            visual = captureVisual();
        }
        catch
        {
            return EndOfRunCleanFrameDecision.CaptureDegraded(
                ScreenshotCaptureReasonCode.CleanFrameVisualUnavailable
            );
        }

        var decision = preparation.Observe(tooltipAudit, visual, nowSeconds);
        if (
            decision.Kind == EndOfRunCleanFrameDecisionKind.Capture
            && decision.ReasonCode
                == ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable
        )
        {
            ReleaseNative();
        }
        return decision;
    }

    internal void ReleaseAll()
    {
        IDisposable? bppSuppression;
        INativeTooltipSuppressionLease? nativeTooltipSuppression;
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            bppSuppression = _bppSuppression;
            nativeTooltipSuppression = _nativeTooltipSuppression;
            _bppSuppression = null;
            _nativeTooltipSuppression = null;
        }

        DisposeNative(nativeTooltipSuppression);
        DisposeBpp(bppSuppression);
    }

    private void ReleaseNative()
    {
        INativeTooltipSuppressionLease? nativeTooltipSuppression;
        lock (_gate)
        {
            nativeTooltipSuppression = _nativeTooltipSuppression;
            _nativeTooltipSuppression = null;
        }
        DisposeNative(nativeTooltipSuppression);
    }

    private static void DisposeNative(INativeTooltipSuppressionLease? suppression)
    {
        try
        {
            suppression?.Dispose();
        }
        catch
        {
            // Native teardown is best-effort; BPP chrome still has an independent lifetime.
        }
    }

    private static void DisposeBpp(IDisposable? suppression)
    {
        try
        {
            suppression?.Dispose();
        }
        catch
        {
            // The workflow owns fail-open even when the Unity surface disappeared.
        }
    }
}
