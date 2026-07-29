#nullable enable
using System.Reflection;
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public sealed class BazaarAgentStructuredLoggingTests
{
    [Fact]
    public void Published_event_catalog_matches_the_locked_manifest()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agent.snapshot.ready"] = "Info|state:Public:Low:None",
            ["agent.action.failed"] =
                "Error|request_id:Public:High:Short,action_kind:Public:Low:None,reason_code:Public:Low:None",
            ["agent.listener.restart_started"] =
                "Debug|old_port:Public:Low:None,new_port:Public:Low:None",
            ["agent.listener.started"] = "Info|port:Public:Low:None",
            ["agent.listener.recovered"] = "Info|port:Public:Low:None",
            ["agent.listener.degraded"] =
                "Warning|port:Public:Low:None,reason_code:Public:Low:None",
            ["agent.listener.stop_degraded"] =
                "Warning|reason_code:Public:Low:None,failed_phase_count:Public:Low:None,first_failed_phase:Public:Low:None",
            ["agent.replay_request.failed"] =
                "Error|request_id:Public:High:Short,action_kind:Public:Low:None,battle_id:Public:High:Short,reason_code:Public:Low:None",
            ["agent.decision_log.append_failed"] =
                "Error|decision_id:Public:High:Short,run_id:Public:High:Short,request_id:Public:High:Short,reason_code:Public:Low:None",
            ["agent.context_capture.failed"] =
                "Error|tick_id:Public:High:None,state:Public:Low:None,reason_code:Public:Low:None",
            ["agent.listener.failed"] = "Error|port:Public:Low:None,reason_code:Public:Low:None",
            ["agent.http_request.failed"] =
                "Error|request_id:Public:High:Short,route:Public:Low:None,method:Public:Low:None,reason_code:Public:Low:None",
            ["agent.http_response.close_failed"] =
                "Debug|request_id:Public:High:Short,route:Public:Low:None,reason_code:Public:Low:None",
            ["agent.rejected_body_drain.stopped"] =
                "Debug|request_id:Public:High:Short,route:Public:Low:None,reason_code:Public:Low:None",
            ["agent.host.initialization_failed"] = "Error|reason_code:Public:Low:None",
            ["agent.host.initialized"] = "Info|",
            ["agent.context.degraded"] = "Warning|reason_code:Public:Low:None",
            ["agent.context.recovered"] = "Info|",
            ["agent.scene_probe.state_changed"] =
                "Debug|scene_name:UntrustedText:High:None,scene_ready:Public:Low:None,app_state_null:Public:Low:None,profile_loaded:Public:Low:None",
            ["agent.scene_probe.degraded"] = "Warning|reason_code:Public:Low:None",
            ["agent.scene_probe.recovered"] = "Info|",
        };
        var actual = typeof(BazaarAgentLogEvents)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(BazaarAgentLogEventDefinition))
            .Select(field => Assert.IsType<BazaarAgentLogEventDefinition>(field.GetValue(null)))
            .ToDictionary(
                definition => definition.EventId,
                definition =>
                    definition.Severity
                    + "|"
                    + string.Join(
                        ",",
                        definition.Fields.Select(field =>
                            $"{field.Name}:{field.Privacy}:{field.Cardinality}:{field.Correlation}"
                        )
                    ),
                StringComparer.Ordinal
            );

        Assert.Equal(expected, actual);
        Assert.Empty(typeof(BazaarAgentLogEvent).GetConstructors());
        Assert.Empty(typeof(BazaarAgentLogEventDefinition).GetConstructors());
        Assert.False(BazaarAgentLogEvents.ActionFailedDefinition.Fields is Array);
        Assert.False(
            BazaarAgentLogEvents
                .ActionFailed(
                    "01JABCDEFGHJKMNPQRSTVWXYZ",
                    BazaarAgentActionKind.Wait,
                    BazaarAgentLogReasonCode.ActionProcessingException
                )
                .Values is Array
        );
    }

    [Fact]
    public void Action_failure_is_captured_as_a_governed_structured_event()
    {
        var logger = new CapturingBazaarAgentLogger();
        var exception = new InvalidOperationException("dispatch failed");

        logger.TryEmit(
            BazaarAgentLogEvents.ActionFailed(
                "01JABCDEFGHJKMNPQRSTVWXYZ",
                BazaarAgentActionKind.MoveItem,
                BazaarAgentLogReasonCode.ActionDispatchException,
                exception
            )
        );

        var logEvent = Assert.Single(logger.Events);
        Assert.Equal(BazaarAgentLogSeverity.Error, logEvent.Definition.Severity);
        Assert.Equal("agent.action.failed", logEvent.Definition.EventId);
        Assert.Same(exception, logEvent.Exception);
        Assert.Collection(
            logEvent.Definition.Fields,
            field =>
                AssertField(
                    field,
                    "request_id",
                    BazaarAgentLogFieldPrivacy.Public,
                    BazaarAgentLogCardinality.High,
                    BazaarAgentLogCorrelation.Short
                ),
            field =>
                AssertField(
                    field,
                    "action_kind",
                    BazaarAgentLogFieldPrivacy.Public,
                    BazaarAgentLogCardinality.Low,
                    BazaarAgentLogCorrelation.None
                ),
            field =>
                AssertField(
                    field,
                    "reason_code",
                    BazaarAgentLogFieldPrivacy.Public,
                    BazaarAgentLogCardinality.Low,
                    BazaarAgentLogCorrelation.None
                )
        );
        Assert.Collection(
            logEvent.Values,
            value => Assert.Equal("01JABCDEFGHJKMNPQRSTVWXYZ", value.Value),
            value => Assert.Equal(BazaarAgentActionKind.MoveItem, value.Value),
            value => Assert.Equal(BazaarAgentLogReasonCode.ActionDispatchException, value.Value)
        );
    }

    [Fact]
    public void Storm_policies_match_the_locked_manifest_keys()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agent.action.failed"] = "request_id",
            ["agent.replay_request.failed"] = "request_id,battle_id",
            ["agent.decision_log.append_failed"] = "decision_id,run_id",
            ["agent.context_capture.failed"] = "state,reason_code",
            ["agent.listener.degraded"] = "port,reason_code",
            ["agent.listener.stop_degraded"] = "reason_code",
            ["agent.listener.failed"] = "",
            ["agent.http_request.failed"] = "request_id",
            ["agent.context.degraded"] = "reason_code",
            ["agent.scene_probe.degraded"] = "reason_code",
        };
        var definitions = typeof(BazaarAgentLogEvents)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(BazaarAgentLogEventDefinition))
            .Select(field => Assert.IsType<BazaarAgentLogEventDefinition>(field.GetValue(null)))
            .ToArray();

        var actual = definitions
            .Where(definition => definition.StormPolicy != null)
            .ToDictionary(
                definition => definition.EventId,
                definition =>
                    string.Join(",", definition.StormPolicy!.KeyFields.Select(field => field.Name)),
                StringComparer.Ordinal
            );

        Assert.Equal(expected, actual);
        Assert.All(
            definitions.Where(definition => definition.StormPolicy != null),
            definition => Assert.False(definition.StormPolicy!.KeyFields is Array)
        );
        var recoveries = definitions
            .Where(definition => definition.RecoversEventId != null)
            .ToDictionary(
                definition => definition.EventId,
                definition => definition.RecoversEventId,
                StringComparer.Ordinal
            );
        Assert.Equal(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["agent.listener.recovered"] = "agent.listener.degraded",
                ["agent.context.recovered"] = "agent.context.degraded",
                ["agent.scene_probe.recovered"] = "agent.scene_probe.degraded",
            },
            recoveries
        );
    }

    [Fact]
    public void Runtime_storm_summary_has_a_governed_stable_schema()
    {
        var definition = BazaarAgentLogRuntimeEvents.StormSuppressedDefinition;

        Assert.Equal("agent.storm.suppressed", definition.EventId);
        Assert.Null(definition.StormPolicy);
        Assert.Collection(
            definition.Fields,
            field => Assert.Equal("source_event", field.Name),
            field => Assert.Equal("suppressed_count", field.Name),
            field => Assert.Equal("window_ms", field.Name),
            field => Assert.Equal("flush_reason", field.Name)
        );
        var logEvent = BazaarAgentLogRuntimeEvents.StormSuppressed(
            "agent.context.degraded",
            3,
            30000,
            BazaarAgentLogStormFlushReason.Recovered
        );
        Assert.Collection(
            logEvent.Values,
            value => Assert.Equal("agent.context.degraded", value.Value),
            value => Assert.Equal(3, value.Value),
            value => Assert.Equal(30000L, value.Value),
            value => Assert.Equal(BazaarAgentLogStormFlushReason.Recovered, value.Value)
        );
    }

    [Fact]
    public void TryEmit_never_throws_when_the_logger_sink_throws()
    {
        var logger = new ThrowingBazaarAgentLogger();

        var thrown = Record.Exception(() => logger.TryEmit(BazaarAgentLogEvents.HostInitialized()));

        Assert.Null(thrown);
    }

    [Fact]
    public async Task Capturing_logger_waits_for_the_requested_count_after_an_earlier_emit()
    {
        var logger = new CapturingBazaarAgentLogger();
        logger.Emit(BazaarAgentLogEvents.HostInitialized());
        var delayed = Task.Run(async () =>
        {
            await Task.Delay(50);
            logger.Emit(BazaarAgentLogEvents.HostInitialized());
        });

        var reached = logger.WaitForCount(2, TimeSpan.FromSeconds(2));
        await delayed;

        Assert.True(reached);
        Assert.Equal(2, logger.Events.Count);
    }

    private static void AssertField(
        BazaarAgentLogFieldDefinition field,
        string name,
        BazaarAgentLogFieldPrivacy privacy,
        BazaarAgentLogCardinality cardinality,
        BazaarAgentLogCorrelation correlation
    )
    {
        Assert.Equal(name, field.Name);
        Assert.Equal(privacy, field.Privacy);
        Assert.Equal(cardinality, field.Cardinality);
        Assert.Equal(correlation, field.Correlation);
    }

    private sealed class ThrowingBazaarAgentLogger : IBazaarAgentLogger
    {
        public void Emit(BazaarAgentLogEvent logEvent) =>
            throw new InvalidOperationException("sink failed");
    }
}
