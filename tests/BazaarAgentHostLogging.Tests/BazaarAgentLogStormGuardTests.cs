#nullable enable
using BazaarPlusPlus.BazaarAgent;
using BazaarPlusPlus.BazaarAgentHost;
using Xunit;

public sealed class BazaarAgentLogStormGuardTests
{
    [Fact]
    public void Expiry_summarizes_suppression_then_starts_a_new_window()
    {
        var now = DateTimeOffset.UnixEpoch;
        var output = new List<string>();
        var renderer = new BazaarAgentLogRenderer();
        var guard = new BazaarAgentLogStormGuard(
            (_, message) =>
            {
                output.Add(message);
                return true;
            },
            renderer.Render,
            () => now
        );
        var exception = new InvalidOperationException("port busy");
        var logEvent = BazaarAgentLogEvents.ListenerDegraded(47900, exception);

        guard.Emit(logEvent, "source-1");
        guard.Emit(logEvent, "source-2");
        now += TimeSpan.FromSeconds(31);
        guard.Emit(logEvent, "source-3");

        Assert.Equal(3, output.Count);
        Assert.Equal("source-1", output[0]);
        Assert.Contains("suppressed_count=1", output[1]);
        Assert.Contains("flush_reason=expired", output[1]);
        Assert.Equal("source-3", output[2]);
    }

    [Fact]
    public void Recovery_flushes_the_summary_before_the_recovered_event()
    {
        var output = new List<string>();
        var renderer = new BazaarAgentLogRenderer();
        var guard = new BazaarAgentLogStormGuard(
            (_, message) =>
            {
                output.Add(message);
                return true;
            },
            renderer.Render,
            () => DateTimeOffset.UnixEpoch
        );
        var degraded = BazaarAgentLogEvents.ContextDegraded(
            BazaarAgentLogReasonCode.ContextBuildException,
            new InvalidOperationException("context failed")
        );
        var recovered = BazaarAgentLogEvents.ContextRecovered();

        guard.Emit(degraded, renderer.Render(degraded));
        guard.Emit(degraded, renderer.Render(degraded));
        guard.Emit(recovered, renderer.Render(recovered));

        Assert.Equal(3, output.Count);
        Assert.Contains("event=agent.context.degraded", output[0]);
        Assert.Contains("event=agent.storm.suppressed", output[1]);
        Assert.Contains("flush_reason=recovered", output[1]);
        Assert.Contains("event=agent.context.recovered", output[2]);
        Assert.Equal(0, guard.ActiveKeyCount);
    }

    [Fact]
    public void Active_key_count_remains_bounded_under_high_cardinality_errors()
    {
        var output = new List<string>();
        var renderer = new BazaarAgentLogRenderer();
        var guard = new BazaarAgentLogStormGuard(
            (_, message) =>
            {
                output.Add(message);
                return true;
            },
            renderer.Render,
            () => DateTimeOffset.UnixEpoch
        );

        for (var index = 0; index <= BazaarAgentLogStormGuard.MaximumActiveKeys; index++)
        {
            guard.Emit(
                BazaarAgentLogEvents.ActionFailed(
                    "request-" + index,
                    BazaarAgentActionKind.Wait,
                    BazaarAgentLogReasonCode.ActionProcessingException,
                    new InvalidOperationException("failure")
                ),
                "source-" + index
            );
        }

        Assert.Equal(BazaarAgentLogStormGuard.MaximumActiveKeys, guard.ActiveKeyCount);
        Assert.Equal(BazaarAgentLogStormGuard.MaximumActiveKeys + 1, output.Count);
    }

    [Fact]
    public async Task Concurrent_recovery_waits_for_the_first_source_delivery()
    {
        var output = new List<string>();
        var outputGate = new object();
        using var sourceEntered = new ManualResetEventSlim(false);
        using var releaseSource = new ManualResetEventSlim(false);
        var firstSource = 1;
        var renderer = new BazaarAgentLogRenderer();
        var guard = new BazaarAgentLogStormGuard(
            (_, message) =>
            {
                if (
                    message.Contains("event=agent.context.degraded", StringComparison.Ordinal)
                    && Interlocked.Exchange(ref firstSource, 0) == 1
                )
                {
                    sourceEntered.Set();
                    Assert.True(releaseSource.Wait(TimeSpan.FromSeconds(5)));
                }
                lock (outputGate)
                    output.Add(message);
                return true;
            },
            renderer.Render,
            () => DateTimeOffset.UnixEpoch
        );
        var degraded = BazaarAgentLogEvents.ContextDegraded(
            BazaarAgentLogReasonCode.ContextBuildException,
            new InvalidOperationException("context failed")
        );
        var recovered = BazaarAgentLogEvents.ContextRecovered();

        var sourceTask = Task.Run(() => guard.Emit(degraded, renderer.Render(degraded)));
        Assert.True(sourceEntered.Wait(TimeSpan.FromSeconds(5)));
        guard.Emit(degraded, renderer.Render(degraded));
        var recoveryTask = Task.Run(() => guard.Emit(recovered, renderer.Render(recovered)));
        var earlyCompletion = await Task.WhenAny(recoveryTask, Task.Delay(100));
        releaseSource.Set();
        Assert.NotSame(recoveryTask, earlyCompletion);
        await Task.WhenAll(sourceTask, recoveryTask);

        string[] snapshot;
        lock (outputGate)
            snapshot = output.ToArray();
        Assert.Equal(3, snapshot.Length);
        Assert.Contains("event=agent.context.degraded", snapshot[0]);
        Assert.Contains("event=agent.storm.suppressed", snapshot[1]);
        Assert.Contains("event=agent.context.recovered", snapshot[2]);
    }

    [Fact]
    public async Task Failed_first_source_skips_its_summary_and_allows_the_next_episode()
    {
        var output = new List<string>();
        var outputGate = new object();
        using var sourceEntered = new ManualResetEventSlim(false);
        using var releaseSource = new ManualResetEventSlim(false);
        var failFirstSource = 1;
        var renderer = new BazaarAgentLogRenderer();
        var guard = new BazaarAgentLogStormGuard(
            (_, message) =>
            {
                if (
                    message.Contains("event=agent.context.degraded", StringComparison.Ordinal)
                    && Interlocked.Exchange(ref failFirstSource, 0) == 1
                )
                {
                    sourceEntered.Set();
                    Assert.True(releaseSource.Wait(TimeSpan.FromSeconds(5)));
                    return false;
                }
                lock (outputGate)
                    output.Add(message);
                return true;
            },
            renderer.Render,
            () => DateTimeOffset.UnixEpoch
        );
        var degraded = BazaarAgentLogEvents.ContextDegraded(
            BazaarAgentLogReasonCode.ContextBuildException,
            new InvalidOperationException("context failed")
        );
        var recovered = BazaarAgentLogEvents.ContextRecovered();

        var sourceTask = Task.Run(() => guard.Emit(degraded, renderer.Render(degraded)));
        Assert.True(sourceEntered.Wait(TimeSpan.FromSeconds(5)));
        guard.Emit(degraded, renderer.Render(degraded));
        var recoveryTask = Task.Run(() => guard.Emit(recovered, renderer.Render(recovered)));
        releaseSource.Set();
        await Task.WhenAll(sourceTask, recoveryTask);
        guard.Emit(degraded, renderer.Render(degraded));

        string[] snapshot;
        lock (outputGate)
            snapshot = output.ToArray();
        Assert.Equal(2, snapshot.Length);
        Assert.Contains("event=agent.context.recovered", snapshot[0]);
        Assert.Contains("event=agent.context.degraded", snapshot[1]);
        Assert.DoesNotContain(
            snapshot,
            message => message.Contains("event=agent.storm.suppressed", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Same_thread_sink_flush_is_deferred_until_source_delivery_completes()
    {
        var output = new List<string>();
        var renderer = new BazaarAgentLogRenderer();
        BazaarAgentLogStormGuard? guard = null;
        guard = new BazaarAgentLogStormGuard(
            (_, message) =>
            {
                output.Add(message);
                guard!.Flush();
                return true;
            },
            renderer.Render,
            () => DateTimeOffset.UnixEpoch
        );
        var degraded = BazaarAgentLogEvents.ContextDegraded(
            BazaarAgentLogReasonCode.ContextBuildException,
            new InvalidOperationException("context failed")
        );

        var emission = Task.Run(() => guard.Emit(degraded, renderer.Render(degraded)));
        await emission.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(output);
        Assert.Contains("event=agent.context.degraded", output[0]);
        Assert.Equal(0, guard.ActiveKeyCount);
    }

    [Fact]
    public void Throwing_clock_fail_open_still_guards_reentrant_sink_calls()
    {
        var sinkCalls = 0;
        var renderer = new BazaarAgentLogRenderer();
        BazaarAgentLogStormGuard? guard = null;
        var degraded = BazaarAgentLogEvents.ContextDegraded(
            BazaarAgentLogReasonCode.ContextBuildException,
            new InvalidOperationException("context failed")
        );
        guard = new BazaarAgentLogStormGuard(
            (_, _) =>
            {
                sinkCalls++;
                if (sinkCalls < 10)
                    guard!.Emit(degraded, renderer.Render(degraded));
                guard!.Flush();
                return true;
            },
            renderer.Render,
            () => throw new InvalidOperationException("clock failed")
        );

        guard.Emit(degraded, renderer.Render(degraded));

        Assert.Equal(1, sinkCalls);
        Assert.Equal(0, guard.ActiveKeyCount);
    }
}
