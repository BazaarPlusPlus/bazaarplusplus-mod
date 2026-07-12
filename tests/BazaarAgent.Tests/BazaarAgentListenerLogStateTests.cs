#nullable enable
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public sealed class BazaarAgentListenerLogStateTests
{
    [Fact]
    public void Initial_success_emits_started_once()
    {
        var logger = new CapturingBazaarAgentLogger();
        var state = new BazaarAgentListenerLogState();

        state.OnStartSucceeded(47900, logger);
        state.OnStartSucceeded(47900, logger);

        var logEvent = Assert.Single(logger.Events);
        Assert.Equal(BazaarAgentLogSeverity.Info, logEvent.Definition.Severity);
        Assert.Equal("agent.listener.started", logEvent.Definition.EventId);
        Assert.Equal(47900, Assert.Single(logEvent.Values).Value);
    }

    [Fact]
    public void Failure_episode_emits_one_degraded_then_one_recovered()
    {
        var logger = new CapturingBazaarAgentLogger();
        var state = new BazaarAgentListenerLogState();
        var first = new InvalidOperationException("port busy");

        state.OnStartFailed(47900, first, logger);
        state.OnStartFailed(47900, new InvalidOperationException("still busy"), logger);
        state.OnStartSucceeded(47900, logger);
        state.OnStartSucceeded(47900, logger);

        Assert.Collection(
            logger.Events,
            logEvent =>
            {
                Assert.Equal(BazaarAgentLogSeverity.Warning, logEvent.Definition.Severity);
                Assert.Equal("agent.listener.degraded", logEvent.Definition.EventId);
                Assert.Same(first, logEvent.Exception);
            },
            logEvent =>
            {
                Assert.Equal(BazaarAgentLogSeverity.Info, logEvent.Definition.Severity);
                Assert.Equal("agent.listener.recovered", logEvent.Definition.EventId);
                Assert.Null(logEvent.Exception);
            }
        );
    }

    [Fact]
    public void Stop_report_keeps_running_cleanup_and_retains_the_first_failure()
    {
        var report = new BazaarAgentListenerStopReport();
        var first = new InvalidOperationException("cancel failed");
        var second = new IOException("close failed");
        var completed = false;

        report.Capture(BazaarAgentListenerStopPhase.Cancellation, () => throw first);
        report.Capture(BazaarAgentListenerStopPhase.ListenerStop, () => completed = true);
        report.Capture(BazaarAgentListenerStopPhase.ListenerClose, () => throw second);

        Assert.True(completed);
        Assert.Equal(2, report.FailedPhaseCount);
        Assert.Equal(BazaarAgentListenerStopPhase.Cancellation, report.FirstFailedPhase);
        Assert.Same(first, report.FirstException);
    }
}
