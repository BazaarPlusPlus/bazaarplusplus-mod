#nullable enable
using System.Collections;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

/// <summary>
/// Tracks asynchronous native presentation work started while a saved replay is rebuilt.
/// Item setup and socket-effect VFX loading must both finish before their pooled controllers
/// can safely cross the replay-state boundary.
/// </summary>
internal static class ReplayCardPresentationReadiness
{
    private static readonly object Sync = new();
    private static readonly Dictionary<int, TrackedPresentation> LatestTasks = [];
    private static bool _tracking;

    internal static IDisposable BeginTracking()
    {
        lock (Sync)
        {
            if (_tracking)
                throw new InvalidOperationException(
                    "Replay card presentation tracking is already active."
                );

            LatestTasks.Clear();
            _tracking = true;
        }

        return new TrackingScope();
    }

    internal static Task Track(MonoBehaviour controller, Task setup)
    {
        if (controller == null)
            return setup;

        lock (Sync)
        {
            if (!_tracking)
                return setup;

            var tracked = AwaitOriginal(setup);
            LatestTasks[controller.GetInstanceID()] = new TrackedPresentation(controller, tracked);
            return tracked;
        }
    }

    internal static async Task WaitForActiveTasksAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var pending = SnapshotActivePendingTasks();
            if (pending.Length > 0)
            {
                await AwaitBeforeDeadlineAsync(Task.WhenAll(pending), deadline);
                continue;
            }

            await AwaitBeforeDeadlineAsync(WaitForRenderBoundaryAsync(), deadline);
            if (SnapshotActivePendingTasks().Length == 0)
                return;
        }
    }

    private static Task[] SnapshotActivePendingTasks()
    {
        lock (Sync)
        {
            if (!_tracking)
                throw new InvalidOperationException(
                    "Replay card presentation tracking is not active."
                );

            foreach (var stale in LatestTasks.Where(pair => pair.Value.Controller == null).ToList())
                LatestTasks.Remove(stale.Key);

            return LatestTasks
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
            throw new TimeoutException("Timed out while waiting for replay card presentation.");

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

    private sealed record TrackedPresentation(MonoBehaviour Controller, Task Task);

    private sealed class TrackingScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (Sync)
            {
                LatestTasks.Clear();
                _tracking = false;
            }
            _disposed = true;
        }
    }
}
