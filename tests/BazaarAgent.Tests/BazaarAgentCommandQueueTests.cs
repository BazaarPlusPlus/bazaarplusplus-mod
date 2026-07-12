using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public class BazaarAgentCommandQueueTests
{
    private const string RequestId = "01JABCDEFGHJKMNPQRSTVWXYZ";

    private static BazaarAgentAction WaitAction() =>
        new() { ActionKind = BazaarAgentActionKind.Wait };

    private static BazaarAgentCommandQueue<BazaarAgentAction> NewQueue(int timeoutMs) =>
        new(timeoutMs);

    [Fact]
    public async Task EnqueueDequeueSetResult_CompletesAwait_WithCorrectResponse()
    {
        var q = NewQueue(timeoutMs: 5_000);
        var task = q.EnqueueAndAwaitAsync(RequestId, WaitAction());
        var pending = q.TryDequeue();
        Assert.NotNull(pending);
        Assert.False(pending!.IsDiscarded);
        Assert.Equal(BazaarAgentActionKind.Wait, pending.Command.ActionKind);
        pending.SetResponse(new BazaarAgentServerResponse(200, "{\"ok\":true}"));
        var res = await task;
        Assert.Equal(200, res.HttpStatus);
        Assert.Equal("{\"ok\":true}", res.JsonBody);
    }

    [Fact]
    public async Task Request_id_round_trips_on_the_queue_envelope_only()
    {
        var q = NewQueue(timeoutMs: 5_000);

        var task = q.EnqueueAndAwaitAsync(RequestId, WaitAction());
        var pending = q.TryDequeue();

        Assert.NotNull(pending);
        Assert.Equal(RequestId, pending!.RequestId);
        Assert.DoesNotContain(
            typeof(BazaarAgentAction).GetProperties(),
            property => string.Equals(property.Name, "RequestId", StringComparison.Ordinal)
        );
        pending.SetResponse(new BazaarAgentServerResponse(200, "{}"));
        Assert.Equal(200, (await task).HttpStatus);
    }

    [Fact]
    public async Task Timeout_FiresWith503AndPendingIsSkippedAtDequeue()
    {
        var q = NewQueue(timeoutMs: 50);
        var task = q.EnqueueAndAwaitAsync(RequestId, WaitAction());
        var res = await task;
        Assert.Equal(503, res.HttpStatus);
        Assert.Contains("\"error\"", res.JsonBody);

        // The timed-out pending must not be claimable for execution.
        Assert.Null(q.TryDequeue());
    }

    [Fact]
    public async Task DequeueClaimsCommand_TimeoutCanNoLongerAnswer503()
    {
        // Regression: the timeout timer used to be able to fire AFTER the controller had
        // dequeued the command, answering 503 to the client while the command still executed
        // on the main thread. Claiming at dequeue disarms the timer.
        var q = NewQueue(timeoutMs: 50);
        var task = q.EnqueueAndAwaitAsync(RequestId, WaitAction());
        var pending = q.TryDequeue();
        Assert.NotNull(pending);

        // Simulate slow main-thread execution well past the timeout.
        await Task.Delay(200);

        pending!.SetResponse(new BazaarAgentServerResponse(200, "{\"ok\":true}"));
        var res = await task;
        Assert.Equal(200, res.HttpStatus);
    }

    [Fact]
    public async Task SetResponse_IsIdempotent()
    {
        var q = NewQueue(timeoutMs: 5_000);
        var task = q.EnqueueAndAwaitAsync(RequestId, WaitAction());
        var pending = q.TryDequeue()!;
        pending.SetResponse(new BazaarAgentServerResponse(200, "{\"first\":true}"));
        pending.SetResponse(new BazaarAgentServerResponse(500, "{\"second\":true}")); // should be ignored
        var res = await task;
        Assert.Equal(200, res.HttpStatus);
    }

    [Fact]
    public async Task TryDequeue_SkipsAlreadyDiscarded()
    {
        var q = NewQueue(timeoutMs: 30);
        var t1 = q.EnqueueAndAwaitAsync(RequestId, WaitAction());
        // Wait for t1 to time out
        var r1 = await t1;
        Assert.Equal(503, r1.HttpStatus);

        // Enqueue a fresh one; t1's pending is discarded but possibly still in queue.
        var t2 = q.EnqueueAndAwaitAsync(RequestId, WaitAction());
        var pending = q.TryDequeue();
        Assert.NotNull(pending);
        Assert.False(pending!.IsDiscarded);
        pending.SetResponse(new BazaarAgentServerResponse(200, "{\"ok\":true}"));
        var r2 = await t2;
        Assert.Equal(200, r2.HttpStatus);
    }

    [Fact]
    public async Task Dispose_CompletesPendingsWith503()
    {
        var q = NewQueue(timeoutMs: 60_000);
        var t = q.EnqueueAndAwaitAsync(RequestId, WaitAction());
        q.Dispose();
        var r = await t;
        Assert.Equal(503, r.HttpStatus);

        // Post-dispose enqueues short-circuit to 503 without queueing.
        var after = await q.EnqueueAndAwaitAsync(RequestId, WaitAction());
        Assert.Equal(503, after.HttpStatus);
    }

    [Fact]
    public async Task EnqueueAndAwait_ReturnsImmediately()
    {
        var q = NewQueue(timeoutMs: 5_000);
        var sw = Stopwatch.StartNew();
        var task = q.EnqueueAndAwaitAsync(RequestId, WaitAction());
        sw.Stop();
        Assert.True(
            sw.ElapsedMilliseconds < 50,
            $"Enqueue took {sw.ElapsedMilliseconds}ms — should be near-instant"
        );
        q.TryDequeue()!.SetResponse(new BazaarAgentServerResponse(200, "{}"));
        await task;
    }

    [Fact]
    public async Task ReplayCommandPayload_RoundTripsThroughQueue()
    {
        var q = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(5_000);
        var payload = new byte[] { 1, 2, 3 };
        var task = q.EnqueueAndAwaitAsync(
            RequestId,
            new BazaarAgentReplayCommand(BazaarAgentReplayControlKind.Start, payload, "b-1")
        );
        var pending = q.TryDequeue()!;
        Assert.Equal(BazaarAgentReplayControlKind.Start, pending.Command.Kind);
        Assert.Equal(payload, pending.Command.Payload);
        Assert.Equal("b-1", pending.Command.BattleId);
        pending.SetResponse(new BazaarAgentServerResponse(202, "{}"));
        Assert.Equal(202, (await task).HttpStatus);
    }
}
