using System.Threading;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.PreviewSurface;
using UnityEngine;

await TestSecondRenderCancelsFirstAsync();
await TestHideClearsAndCancelsAsync();
await TestCancelledRenderCannotClearReplacementAsync();

Console.WriteLine("PreviewSurfaceHost checks passed.");

static async Task TestSecondRenderCancelsFirstAsync()
{
    var surface = new RecordingBoardSurface();
    var target = new PreviewBoardRenderTarget(surface);
    var firstModel = new BoardRenderModel
    {
        Data = new PreviewBoardModel { Signature = "first" },
        Presentation = new PreviewBoardPresentation { Visible = true },
    };
    var secondModel = new BoardRenderModel
    {
        Data = new PreviewBoardModel { Signature = "second" },
        Presentation = new PreviewBoardPresentation { Visible = true },
    };

    target.Render(firstModel);
    await surface.WaitForRenderStartAsync(1);

    target.Render(secondModel);
    await surface.WaitForRenderCompletionCountAsync(2);

    Assert(surface.RenderCallCount == 2, "Each request should be forwarded.");
    Assert(surface.CancelledRenderCount >= 1, "A newer request should cancel the previous one.");
    Assert(surface.LastRenderedSignature == "second", "The latest request should win.");
}

static async Task TestHideClearsAndCancelsAsync()
{
    var surface = new RecordingBoardSurface();
    var target = new PreviewBoardRenderTarget(surface);
    var model = new BoardRenderModel
    {
        Data = new PreviewBoardModel { Signature = "visible" },
        Presentation = new PreviewBoardPresentation { Visible = true },
    };

    target.Render(model);
    await surface.WaitForRenderStartAsync(1);

    target.SetVisible(false);
    await surface.WaitForRenderCompletionCountAsync(1);
    await surface.WaitForClearCountAsync(1);

    Assert(surface.ClearCallCount == 1, "Hide should clear the board surface.");
    Assert(surface.CancelledRenderCount == 1, "Hide should cancel the in-flight render.");
    Assert(surface.VisibleStates.Count >= 1 && surface.VisibleStates[^1] == false, "Hide should update visibility.");
}

static async Task TestCancelledRenderCannotClearReplacementAsync()
{
    var surface = new ReplacementSensitiveBoardSurface();
    var target = new PreviewBoardRenderTarget(surface);
    var firstModel = new BoardRenderModel
    {
        Data = new PreviewBoardModel { Signature = "first" },
        Presentation = new PreviewBoardPresentation { Visible = true },
    };
    var secondModel = new BoardRenderModel
    {
        Data = new PreviewBoardModel { Signature = "second" },
        Presentation = new PreviewBoardPresentation { Visible = true },
    };

    target.Render(firstModel);
    await surface.WaitForRenderStartAsync(1);

    target.Render(secondModel);
    await surface.WaitForRenderCompletionCountAsync(1);
    await surface.WaitForRenderCompletionCountAsync(2);

    Assert(surface.LastRenderedSignature == "second", "Cancelled render should not clear the replacement render.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class ReplacementSensitiveBoardSurface : IPreviewBoardSurface
{
    private readonly object _sync = new();
    private readonly List<TaskCompletionSource<bool>> _renderStarts = new();
    private readonly List<TaskCompletionSource<bool>> _renderCompletions = new();
    private readonly List<TaskCompletionSource<bool>> _clearCompletions = new();
    private int _renderCallCount;

    public Transform RootTransform => null!;

    public bool IsAlive => true;

    public string LastRenderedSignature { get; private set; } = string.Empty;

    public void SetPresentation(PreviewBoardPresentation presentation) { }

    public void SetDebugOptions(PreviewBoardDebugOptions debugOptions) { }

    public void SetVisible(bool visible) { }

    public void UpdateAnchor(Vector3 position, Quaternion rotation) { }

    public async Task RenderAsync(
        PreviewBoardModel model,
        CancellationToken cancellationToken = default
    )
    {
        var renderIndex = Interlocked.Increment(ref _renderCallCount);
        EnsureSource(_renderStarts, renderIndex).TrySetResult(true);

        if (renderIndex == 1)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await Task.Delay(20);
                Clear();
            }
            finally
            {
                EnsureSource(_renderCompletions, renderIndex).TrySetResult(true);
            }

            return;
        }

        await Task.Delay(5, cancellationToken);
        LastRenderedSignature = model?.Signature ?? string.Empty;
        EnsureSource(_renderCompletions, renderIndex).TrySetResult(true);
    }

    public void Clear()
    {
        LastRenderedSignature = string.Empty;
    }

    public void Dispose() { }

    public Task WaitForRenderStartAsync(int count)
    {
        return EnsureSource(_renderStarts, count).Task;
    }

    public Task WaitForRenderCompletionCountAsync(int count)
    {
        return EnsureSource(_renderCompletions, count).Task;
    }

    private TaskCompletionSource<bool> EnsureSource(List<TaskCompletionSource<bool>> list, int count)
    {
        lock (_sync)
        {
            while (list.Count < count)
                list.Add(new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

            return list[count - 1];
        }
    }
}

internal sealed class RecordingBoardSurface : IPreviewBoardSurface
{
    private readonly object _sync = new();
    private readonly List<TaskCompletionSource<bool>> _renderStarts = new();
    private readonly List<TaskCompletionSource<bool>> _renderCompletions = new();
    private readonly List<TaskCompletionSource<bool>> _clearCompletions = new();
    private int _renderCallCount;
    private int _cancelledRenderCount;
    private int _clearCallCount;

    public Transform RootTransform => null!;

    public bool IsAlive => true;

    public int RenderCallCount => _renderCallCount;

    public int CancelledRenderCount => _cancelledRenderCount;

    public int ClearCallCount => _clearCallCount;

    public string LastRenderedSignature { get; private set; } = string.Empty;

    public List<bool> VisibleStates { get; } = new();

    public void SetPresentation(PreviewBoardPresentation presentation) { }

    public void SetDebugOptions(PreviewBoardDebugOptions debugOptions) { }

    public void SetVisible(bool visible)
    {
        VisibleStates.Add(visible);
    }

    public void UpdateAnchor(Vector3 position, Quaternion rotation) { }

    public async Task RenderAsync(
        PreviewBoardModel model,
        CancellationToken cancellationToken = default
    )
    {
        var renderIndex = Interlocked.Increment(ref _renderCallCount);
        EnsureSource(_renderStarts, renderIndex).TrySetResult(true);

        try
        {
            await Task.Delay(50, cancellationToken);
            LastRenderedSignature = model?.Signature ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelledRenderCount);
        }
        finally
        {
            EnsureSource(_renderCompletions, renderIndex).TrySetResult(true);
        }
    }

    public void Clear()
    {
        var clearCount = Interlocked.Increment(ref _clearCallCount);
        EnsureSource(_clearCompletions, clearCount).TrySetResult(true);
    }

    public void Dispose() { }

    public Task WaitForRenderStartAsync(int count)
    {
        return EnsureSource(_renderStarts, count).Task;
    }

    public Task WaitForRenderCompletionCountAsync(int count)
    {
        return EnsureSource(_renderCompletions, count).Task;
    }

    public Task WaitForClearCountAsync(int count)
    {
        return EnsureSource(_clearCompletions, count).Task;
    }

    private TaskCompletionSource<bool> EnsureSource(List<TaskCompletionSource<bool>> list, int count)
    {
        lock (_sync)
        {
            while (list.Count < count)
                list.Add(new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

            return list[count - 1];
        }
    }
}
