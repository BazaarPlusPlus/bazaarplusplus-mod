#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public class BazaarAgentReplayControlProcessorTests
{
    private const string RequestId = "01JABCDEFGHJKMNPQRSTVWXYZ";

    private sealed class RecordingSink : IBazaarAgentReplayControlSink
    {
        public List<(string RequestId, byte[] Payload, string? BattleId)> StartCalls { get; } =
            new();
        public int ContinueCalls { get; private set; }
        public BazaarAgentReplayControlOutcome NextOutcome { get; set; } =
            new(BazaarAgentReplayControlStatus.Accepted, null, null);
        public Exception? ThrowOnStart { get; set; }

        public BazaarAgentReplayControlOutcome Start(
            string requestId,
            byte[] ghostBattlePayloadBytes,
            string? battleId
        )
        {
            if (ThrowOnStart is not null)
                throw ThrowOnStart;
            StartCalls.Add((requestId, ghostBattlePayloadBytes, battleId));
            return NextOutcome;
        }

        public BazaarAgentReplayControlOutcome Continue()
        {
            ContinueCalls++;
            return NextOutcome;
        }
    }

    private static async Task<BazaarAgentServerResponse> RunAsync(
        BazaarAgentCommandQueue<BazaarAgentReplayCommand> queue,
        Task<BazaarAgentServerResponse> awaiting,
        RecordingSink sink,
        IBazaarAgentLogger? logger = null
    )
    {
        var pending = queue.TryDequeue();
        Assert.NotNull(pending);
        BazaarAgentReplayControlProcessor.Process(
            pending!,
            sink,
            logger ?? new CapturingBazaarAgentLogger()
        );
        return await awaiting;
    }

    [Fact]
    public async Task StartCommand_ReachesSinkStart_NeverContinue()
    {
        using var queue = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(5000);
        var sink = new RecordingSink
        {
            NextOutcome = new(BazaarAgentReplayControlStatus.Accepted, null, "b-1"),
        };
        var payload = new byte[] { 1, 2, 3 };

        var task = queue.EnqueueAndAwaitAsync(
            RequestId,
            new BazaarAgentReplayCommand(BazaarAgentReplayControlKind.Start, payload, "b-1")
        );
        var res = await RunAsync(queue, task, sink);

        Assert.Equal(202, res.HttpStatus);
        Assert.Contains("\"recording-started\"", res.JsonBody);
        Assert.Contains("\"battleId\":\"b-1\"", res.JsonBody);
        var call = Assert.Single(sink.StartCalls);
        Assert.Equal(RequestId, call.RequestId);
        Assert.Equal(payload, call.Payload);
        Assert.Equal("b-1", call.BattleId);
        Assert.Equal(0, sink.ContinueCalls);
    }

    [Fact]
    public async Task ContinueCommand_ReachesSinkContinue_NeverStart()
    {
        using var queue = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(5000);
        var sink = new RecordingSink();

        var task = queue.EnqueueAndAwaitAsync(
            RequestId,
            new BazaarAgentReplayCommand(BazaarAgentReplayControlKind.Continue, null, null)
        );
        var res = await RunAsync(queue, task, sink);

        Assert.Equal(200, res.HttpStatus);
        Assert.Contains("\"continue-triggered\"", res.JsonBody);
        Assert.Empty(sink.StartCalls);
        Assert.Equal(1, sink.ContinueCalls);
    }

    [Theory]
    [InlineData(BazaarAgentReplayControlStatus.InvalidPayload, 400, "invalid")]
    [InlineData(BazaarAgentReplayControlStatus.Rejected, 409, "stale-or-unavailable")]
    [InlineData(BazaarAgentReplayControlStatus.Unavailable, 503, "unavailable")]
    public async Task FailureOutcomes_MapOntoExistingErrorCodeTable(
        BazaarAgentReplayControlStatus status,
        int expectedHttp,
        string expectedCode
    )
    {
        using var queue = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(5000);
        var sink = new RecordingSink { NextOutcome = new(status, "why it failed", null) };

        var logger = new CapturingBazaarAgentLogger();
        var task = queue.EnqueueAndAwaitAsync(
            RequestId,
            new BazaarAgentReplayCommand(BazaarAgentReplayControlKind.Start, new byte[] { 1 }, null)
        );
        var res = await RunAsync(queue, task, sink, logger);

        Assert.Equal(expectedHttp, res.HttpStatus);
        Assert.Contains($"\"error\":\"{expectedCode}\"", res.JsonBody);
        Assert.Contains("why it failed", res.JsonBody);
        var logEvent = Assert.Single(logger.Events);
        Assert.Equal(BazaarAgentLogSeverity.Error, logEvent.Definition.Severity);
        Assert.Equal("agent.replay_request.failed", logEvent.Definition.EventId);
        Assert.Equal(RequestId, logEvent.Values[0].Value);
        Assert.Null(logEvent.Values[2].Value);
        Assert.Equal(
            status switch
            {
                BazaarAgentReplayControlStatus.InvalidPayload =>
                    BazaarAgentLogReasonCode.ReplayInvalidPayload,
                BazaarAgentReplayControlStatus.Rejected =>
                    BazaarAgentLogReasonCode.ReplayRejected,
                _ => BazaarAgentLogReasonCode.ReplayUnavailable,
            },
            logEvent.Values[3].Value
        );
    }

    [Fact]
    public async Task SinkException_Returns500Internal()
    {
        using var queue = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(5000);
        var exception = new InvalidOperationException("boom");
        var sink = new RecordingSink { ThrowOnStart = exception };

        var task = queue.EnqueueAndAwaitAsync(
            RequestId,
            new BazaarAgentReplayCommand(
                BazaarAgentReplayControlKind.Start,
                new byte[] { 1 },
                "0123456789abcdef0123456789abcdef"
            )
        );
        var pending = queue.TryDequeue();
        Assert.NotNull(pending);
        var logger = new CapturingBazaarAgentLogger(_ =>
            Assert.True(pending!.ResponseTask.IsCompleted)
        );
        BazaarAgentReplayControlProcessor.Process(pending!, sink, logger);
        var res = await task;

        Assert.Equal(500, res.HttpStatus);
        Assert.Contains("\"error\":\"internal\"", res.JsonBody);
        var logEvent = Assert.Single(logger.Events);
        Assert.Equal(BazaarAgentLogSeverity.Error, logEvent.Definition.Severity);
        Assert.Equal("agent.replay_request.failed", logEvent.Definition.EventId);
        Assert.Same(exception, logEvent.Exception);
        Assert.Collection(
            logEvent.Values,
            value => Assert.Equal(RequestId, value.Value),
            value => Assert.Equal(BazaarAgentReplayLogAction.Record, value.Value),
            value => Assert.Equal("0123456789abcdef0123456789abcdef", value.Value),
            value => Assert.Equal(BazaarAgentLogReasonCode.ReplaySinkException, value.Value)
        );
    }

    [Fact]
    public async Task SinkException_does_not_copy_an_unvalidated_request_battle_id_into_the_log()
    {
        using var queue = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(5000);
        var sink = new RecordingSink { ThrowOnStart = new InvalidOperationException("boom") };
        var task = queue.EnqueueAndAwaitAsync(
            RequestId,
            new BazaarAgentReplayCommand(
                BazaarAgentReplayControlKind.Start,
                new byte[] { 1 },
                "token=must-not-appear"
            )
        );
        var pending = Assert.IsType<BazaarAgentPendingCommand<BazaarAgentReplayCommand>>(
            queue.TryDequeue()
        );
        var logger = new CapturingBazaarAgentLogger();

        BazaarAgentReplayControlProcessor.Process(pending, sink, logger);
        _ = await task;

        var logEvent = Assert.Single(logger.Events);
        Assert.Null(logEvent.Values[2].Value);
        Assert.DoesNotContain(
            logEvent.Values,
            value =>
                value.Value?.ToString()?.Contains("must-not-appear", StringComparison.Ordinal)
                == true
        );
    }

    [Fact]
    public async Task QueueTimeout_Returns503AndDiscardsPending()
    {
        using var queue = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(
            timeoutMilliseconds: 50
        );
        var task = queue.EnqueueAndAwaitAsync(
            RequestId,
            new BazaarAgentReplayCommand(BazaarAgentReplayControlKind.Continue, null, null)
        );
        var res = await task;
        Assert.Equal(503, res.HttpStatus);

        var pending = queue.TryDequeue();
        Assert.True(pending is null || pending.IsDiscarded);
    }

    [Fact]
    public async Task Dispose_CompletesPendingWith503()
    {
        var queue = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(60_000);
        var task = queue.EnqueueAndAwaitAsync(
            RequestId,
            new BazaarAgentReplayCommand(BazaarAgentReplayControlKind.Start, new byte[] { 1 }, "b")
        );
        queue.Dispose();
        var res = await task;
        Assert.Equal(503, res.HttpStatus);

        // Post-dispose enqueues short-circuit to 503 without queueing.
        var after = await queue.EnqueueAndAwaitAsync(
            RequestId,
            new BazaarAgentReplayCommand(BazaarAgentReplayControlKind.Continue, null, null)
        );
        Assert.Equal(503, after.HttpStatus);
    }
}
