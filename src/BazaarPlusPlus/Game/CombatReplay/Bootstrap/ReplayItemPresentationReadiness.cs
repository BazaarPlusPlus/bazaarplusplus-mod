#nullable enable
using System.Collections;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class ReplayItemPresentationReadiness
{
    private static readonly object Sync = new();
    private static readonly Dictionary<int, TrackedSetup> LatestSetups = [];

    internal static Task Track(ItemController controller, Task setup)
    {
        if (controller == null)
            return setup;

        var tracked = AwaitOriginal(setup);
        lock (Sync)
            LatestSetups[controller.GetInstanceID()] = new TrackedSetup(controller, tracked);
        return tracked;
    }

    internal static async Task WaitForActiveSetupsAsync()
    {
        while (true)
        {
            var pending = SnapshotActivePendingSetups();
            if (pending.Length > 0)
            {
                await Task.WhenAll(pending);
                continue;
            }

            await WaitForRenderBoundaryAsync();
            if (SnapshotActivePendingSetups().Length == 0)
                return;
        }
    }

    private static Task[] SnapshotActivePendingSetups()
    {
        lock (Sync)
        {
            foreach (
                var stale in LatestSetups.Where(pair => pair.Value.Controller == null).ToList()
            )
                LatestSetups.Remove(stale.Key);

            return LatestSetups
                .Values.Where(setup =>
                    setup.Controller != null
                    && setup.Controller.gameObject.activeInHierarchy
                    && !setup.Task.IsCompleted
                )
                .Select(setup => setup.Task)
                .ToArray();
        }
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
}
