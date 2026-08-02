#nullable enable
using System.Collections;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class ReplayItemPresentationReadiness
{
    private static readonly object Sync = new();
    private static readonly Dictionary<int, TrackedSetup> LatestSetups = [];
    private static bool _tracking;

    internal static IDisposable BeginTracking()
    {
        lock (Sync)
        {
            if (_tracking)
                throw new InvalidOperationException(
                    "Replay item setup tracking is already active."
                );

            LatestSetups.Clear();
            _tracking = true;
        }

        return new TrackingScope();
    }

    internal static Task Track(ItemController controller, Task setup)
    {
        if (controller == null)
            return setup;

        lock (Sync)
        {
            if (!_tracking)
                return setup;

            var tracked = AwaitOriginal(setup);
            LatestSetups[controller.GetInstanceID()] = new TrackedSetup(controller, tracked);
            return tracked;
        }
    }

    internal static async Task WaitForActiveSetupsAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var pending = SnapshotActivePendingSetups();
            if (pending.Length > 0)
            {
                await AwaitBeforeDeadlineAsync(Task.WhenAll(pending), deadline);
                continue;
            }

            await AwaitBeforeDeadlineAsync(WaitForRenderBoundaryAsync(), deadline);
            if (SnapshotActivePendingSetups().Length == 0)
                return;
        }
    }

    private static Task[] SnapshotActivePendingSetups()
    {
        lock (Sync)
        {
            if (!_tracking)
                throw new InvalidOperationException("Replay item setup tracking is not active.");

            foreach (
                var stale in LatestSetups.Where(pair => pair.Value.Controller == null).ToList()
            )
                LatestSetups.Remove(stale.Key);

            return LatestSetups
                .Values.Where(setup =>
                    setup.Controller != null
                    && setup.Controller.gameObject.activeInHierarchy
                    && !setup.Task.IsCompletedSuccessfully
                )
                .Select(setup => setup.Task)
                .ToArray();
        }
    }

    private static async Task AwaitBeforeDeadlineAsync(Task task, DateTime deadline)
    {
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero || await Task.WhenAny(task, Task.Delay(remaining)) != task)
            throw new TimeoutException("Timed out while waiting for replay item presentation.");

        await task;
    }

    private static Task WaitForRenderBoundaryAsync()
    {
        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null)
            throw new InvalidOperationException("BoardManager is unavailable.");

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        boardManager.StartCoroutine(CompleteAfterRenderBoundary(completion));
        return completion.Task;
    }

    private static IEnumerator CompleteAfterRenderBoundary(TaskCompletionSource<bool> completion)
    {
        yield return new WaitForEndOfFrame();
        completion.TrySetResult(true);
    }

    private static async Task AwaitOriginal(Task setup) => await setup;

    private sealed record TrackedSetup(ItemController Controller, Task Task);

    private sealed class TrackingScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (Sync)
            {
                LatestSetups.Clear();
                _tracking = false;
            }
            _disposed = true;
        }
    }
}
