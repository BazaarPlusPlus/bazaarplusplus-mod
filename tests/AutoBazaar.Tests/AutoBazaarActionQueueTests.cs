using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.AutoBazaar;
using Xunit;

public class AutoBazaarActionQueueTests
{
    private static AutoBazaarAction WaitAction() =>
        new() { ActionKind = AutoBazaarActionKind.Wait };

    [Fact]
    public async Task EnqueueDequeueSetResult_CompletesAwait_WithCorrectResponse()
    {
        var q = new AutoBazaarActionQueue(timeoutMilliseconds: 5_000);
        var task = q.EnqueueAndAwaitAsync(WaitAction());
        var pending = q.TryDequeue();
        Assert.NotNull(pending);
        Assert.False(pending!.IsDiscarded);
        pending.SetResponse(new AutoBazaarServerResponse(200, "{\"ok\":true}"));
        var res = await task;
        Assert.Equal(200, res.HttpStatus);
        Assert.Equal("{\"ok\":true}", res.JsonBody);
    }

    [Fact]
    public async Task Timeout_FiresWith503AndPendingIsDiscarded()
    {
        var q = new AutoBazaarActionQueue(timeoutMilliseconds: 50);
        var task = q.EnqueueAndAwaitAsync(WaitAction());
        var res = await task;
        Assert.Equal(503, res.HttpStatus);
        Assert.Contains("\"error\"", res.JsonBody);

        // Now a delayed dequeue: should see either null (auto-skipped) or a pending with IsDiscarded=true.
        var pending = q.TryDequeue();
        Assert.True(pending is null || pending.IsDiscarded);
    }

    [Fact]
    public async Task SetResponse_IsIdempotent()
    {
        var q = new AutoBazaarActionQueue(timeoutMilliseconds: 5_000);
        var task = q.EnqueueAndAwaitAsync(WaitAction());
        var pending = q.TryDequeue()!;
        pending.SetResponse(new AutoBazaarServerResponse(200, "{\"first\":true}"));
        pending.SetResponse(new AutoBazaarServerResponse(500, "{\"second\":true}")); // should be ignored
        var res = await task;
        Assert.Equal(200, res.HttpStatus);
    }

    [Fact]
    public async Task TryDequeue_SkipsAlreadyDiscarded()
    {
        var q = new AutoBazaarActionQueue(timeoutMilliseconds: 30);
        var t1 = q.EnqueueAndAwaitAsync(WaitAction());
        // Wait for t1 to time out
        var r1 = await t1;
        Assert.Equal(503, r1.HttpStatus);

        // Enqueue a fresh one; t1's pending is discarded but possibly still in queue.
        var t2 = q.EnqueueAndAwaitAsync(WaitAction());
        var pending = q.TryDequeue();
        Assert.NotNull(pending);
        Assert.False(pending!.IsDiscarded);
        pending.SetResponse(new AutoBazaarServerResponse(200, "{\"ok\":true}"));
        var r2 = await t2;
        Assert.Equal(200, r2.HttpStatus);
    }

    [Fact]
    public async Task Dispose_CompletesPendingsWith503()
    {
        var q = new AutoBazaarActionQueue(timeoutMilliseconds: 60_000);
        var t = q.EnqueueAndAwaitAsync(WaitAction());
        q.Dispose();
        var r = await t;
        Assert.Equal(503, r.HttpStatus);
    }

    [Fact]
    public async Task EnqueueAndAwait_ReturnsImmediately()
    {
        var q = new AutoBazaarActionQueue(timeoutMilliseconds: 5_000);
        var sw = Stopwatch.StartNew();
        var task = q.EnqueueAndAwaitAsync(WaitAction());
        sw.Stop();
        Assert.True(
            sw.ElapsedMilliseconds < 50,
            $"Enqueue took {sw.ElapsedMilliseconds}ms — should be near-instant"
        );
        q.TryDequeue()!.SetResponse(new AutoBazaarServerResponse(200, "{}"));
        await task;
    }
}
