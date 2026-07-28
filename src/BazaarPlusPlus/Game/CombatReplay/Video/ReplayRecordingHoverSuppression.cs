#nullable enable
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class ReplayRecordingHoverSuppression
{
    private static int _leaseCount;

    internal static bool IsActive => Volatile.Read(ref _leaseCount) > 0;

    internal static IDisposable Begin()
    {
        if (Interlocked.Increment(ref _leaseCount) == 1)
            ClearCurrentHoverState();

        return new Lease();
    }

    private static void ClearCurrentHoverState()
    {
        // The Harmony gates remain authoritative even if a native controller is being torn down
        // while recording starts. Cleanup is best-effort so a stale hover cannot cancel the rest
        // of the recorder's UI suppression scope.
        TryClear(() =>
        {
            var tooltipParent = Data.TooltipParentComponent;
            if (tooltipParent == null)
                return;

            tooltipParent.UnlockCardTooltipController();
            tooltipParent.HideAuxiliaryTooltipController();
            tooltipParent.HideSecondaryCardTooltipController();
            tooltipParent.HideCardTooltipController();
        });
        TryClear(() => Singleton<BoardManager>.Instance?.ClearCardHighlights());
        TryClear(() =>
        {
            foreach (
                var controller in UnityEngine.Object.FindObjectsOfType<CardController>(
                    includeInactive: false
                )
            )
            {
                TryClear(() =>
                {
                    controller.TriggerUnhover();
                    controller.ResetPosition();
                });
            }
        });
        TryClear(() =>
        {
            foreach (
                var renderer in UnityEngine.Object.FindObjectsOfType<SkillProxyRenderer>(
                    includeInactive: false
                )
            )
            {
                TryClear(() => renderer.OnPointerExit(null));
            }
        });
        TryClear(() =>
        {
            foreach (
                var controller in UnityEngine.Object.FindObjectsOfType<RecapItemVisualController>(
                    includeInactive: false
                )
            )
            {
                TryClear(() => controller.OnPointerExit(null));
            }
        });
    }

    private static void TryClear(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Native objects can disappear between FindObjectsOfType and cleanup during replay
            // transitions. The scoped Harmony gates still prevent any new hover presentation.
        }
    }

    private sealed class Lease : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Interlocked.Decrement(ref _leaseCount);
        }
    }
}
