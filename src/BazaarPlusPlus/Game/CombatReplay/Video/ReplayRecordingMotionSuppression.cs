#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class ReplayRecordingMotionSuppression
{
    internal const int TerminalPresentationHoldMilliseconds = 1000;

    private static int _leaseCount;

    internal static bool IsActive => Volatile.Read(ref _leaseCount) > 0;

    internal static IDisposable Begin()
    {
        Interlocked.Increment(ref _leaseCount);
        return new Lease();
    }

    internal static Task HoldTerminalPresentationAsync() =>
        Task.Delay(TerminalPresentationHoldMilliseconds);

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
