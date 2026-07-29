#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class ReplayRecordingMotionSuppression
{
    private static int _leaseCount;

    internal static bool IsActive => Volatile.Read(ref _leaseCount) > 0;

    internal static IDisposable Begin()
    {
        Interlocked.Increment(ref _leaseCount);
        return new Lease();
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
