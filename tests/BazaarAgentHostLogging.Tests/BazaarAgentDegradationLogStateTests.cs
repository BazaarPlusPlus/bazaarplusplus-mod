#nullable enable
using BazaarPlusPlus.BazaarAgent;
using BazaarPlusPlus.BazaarAgentHost;
using Xunit;

public sealed class BazaarAgentDegradationLogStateTests
{
    [Fact]
    public void Repeated_failures_warn_once_and_first_success_recovers_once()
    {
        var logger = new CapturingLogger();
        var state = new BazaarAgentDegradationLogState();
        var first = new InvalidOperationException("first");

        state.ReportDegraded(logger, () => BazaarAgentLogEvents.ContextDegraded(first));
        state.ReportDegraded(
            logger,
            () => BazaarAgentLogEvents.ContextDegraded(new InvalidOperationException("repeat"))
        );
        state.ReportRecovered(logger, BazaarAgentLogEvents.ContextRecovered);
        state.ReportRecovered(logger, BazaarAgentLogEvents.ContextRecovered);

        Assert.Collection(
            logger.Events,
            logEvent =>
            {
                Assert.Equal("agent.context.degraded", logEvent.Definition.EventId);
                Assert.Same(first, logEvent.Exception);
            },
            logEvent => Assert.Equal("agent.context.recovered", logEvent.Definition.EventId)
        );
    }

    private sealed class CapturingLogger : IBazaarAgentLogger
    {
        public List<BazaarAgentLogEvent> Events { get; } = new();

        public void Emit(BazaarAgentLogEvent logEvent) => Events.Add(logEvent);
    }
}
