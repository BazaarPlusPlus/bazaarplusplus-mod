#nullable enable
using BazaarPlusPlus.BazaarAgent;
using BazaarPlusPlus.BazaarAgentHost;
using BepInEx.Logging;
using Xunit;

public sealed class BazaarAgentBepInExLoggerTests
{
    [Fact]
    public void Adapter_renders_structured_error_with_compatible_prefix_and_bounded_fields()
    {
        using var source = new ManualLogSource("test");
        LogEventArgs? captured = null;
        source.LogEvent += (_, args) => captured = args;
        var adapter = new BazaarAgentBepInExLogger(source);
        var exception = new InvalidOperationException("坏\nline");

        adapter.Emit(
            BazaarAgentLogEvents.ActionFailed(
                "01JABCDEFGHJKMNPQRSTVWXYZ",
                BazaarAgentActionKind.MoveItem,
                BazaarAgentLogReasonCode.ActionDispatchException,
                exception
            )
        );

        Assert.NotNull(captured);
        Assert.Equal(LogLevel.Error, captured!.Level);
        var text = Assert.IsType<string>(captured.Data);
        Assert.StartsWith("[BazaarAgent] event=agent.action.failed", text);
        Assert.Contains("request_id=01JABCDE", text);
        Assert.DoesNotContain("FGHJKMNPQRSTVWXYZ", text);
        Assert.Contains("action_kind=move_item", text);
        Assert.Contains("reason_code=action_dispatch_exception", text);
        Assert.Contains("exception_type=System.InvalidOperationException", text);
        Assert.Contains("坏", text);
        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void Adapter_preserves_cjk_and_bounds_untrusted_text_as_one_line()
    {
        using var source = new ManualLogSource("test");
        LogEventArgs? captured = null;
        source.LogEvent += (_, args) => captured = args;
        var adapter = new BazaarAgentBepInExLogger(source);
        var sceneName =
            "场景 中文 / 日本語\r\n\t\u0001\u2028\u2029" + new string('界', 4096) + " tail";

        adapter.Emit(
            BazaarAgentLogEvents.SceneProbeStateChanged(
                sceneName,
                sceneReady: false,
                appStateNull: true,
                profileLoaded: false
            )
        );

        Assert.NotNull(captured);
        var text = Assert.IsType<string>(captured!.Data);
        Assert.True(text.Length <= BazaarAgentLogRenderer.RecordCharacterBudget);
        Assert.Contains("场景", text);
        Assert.Contains("中文 / 日本語", text);
        Assert.Contains("field_truncated=true", text);
        Assert.Contains("scene_name=\"", text);
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain('\t', text);
        Assert.DoesNotContain('\u0001', text);
        Assert.DoesNotContain('\u2028', text);
        Assert.DoesNotContain('\u2029', text);
    }

    [Fact]
    public void Adapter_projects_a_bounded_outer_exception_and_three_inner_exceptions()
    {
        using var source = new ManualLogSource("test");
        LogEventArgs? captured = null;
        source.LogEvent += (_, args) => captured = args;
        var adapter = new BazaarAgentBepInExLogger(source);
        var exception = new ProjectedException(
            "outer\n" + new string('m', 2000),
            "STACK-HEAD\n" + new string('s', 10000) + "\nSTACK-TAIL",
            new ArgumentException(
                "inner-one",
                new FormatException(
                    "inner-two",
                    new IOException("inner-three", new Exception("inner-four"))
                )
            )
        );

        adapter.Emit(
            BazaarAgentLogEvents.ActionFailed(
                "01JABCDEFGHJKMNPQRSTVWXYZ",
                BazaarAgentActionKind.Wait,
                BazaarAgentLogReasonCode.ActionProcessingException,
                exception
            )
        );

        Assert.NotNull(captured);
        var text = Assert.IsType<string>(captured!.Data);
        Assert.True(text.Length <= BazaarAgentLogRenderer.ExceptionRecordCharacterBudget);
        Assert.Contains("exception_hresult=0x", text);
        Assert.Contains("exception_inner_1_type=System.ArgumentException", text);
        Assert.Contains("exception_inner_2_type=System.FormatException", text);
        Assert.Contains("exception_inner_3_type=System.IO.IOException", text);
        Assert.DoesNotContain("exception_inner_4", text);
        Assert.DoesNotContain("inner-four", text);
        Assert.Contains("STACK-HEAD", text);
        Assert.Contains("STACK-TAIL", text);
        Assert.Contains("exception_truncated=true", text);
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain('\n', text);
    }

    [Fact]
    public void Adapter_redacts_secret_shaped_exception_text_and_raw_locations()
    {
        using var source = new ManualLogSource("test");
        LogEventArgs? captured = null;
        source.LogEvent += (_, args) => captured = args;
        var adapter = new BazaarAgentBepInExLogger(source);
        var exception = new ProjectedException(
            "Authorization: Bearer bearer-secret\n"
                + "token=token-secret account_id=account-secret\n"
                + "https://example.com/fail?token=url-secret#fragment\n"
                + "/Users/alice/private.txt\n"
                + "request_body={\"password\":\"body-secret\"}",
            "at /Users/alice/Games/TheBazaar/private.cs:42"
        );

        adapter.Emit(
            BazaarAgentLogEvents.ActionFailed(
                "01JABCDEFGHJKMNPQRSTVWXYZ",
                BazaarAgentActionKind.Wait,
                BazaarAgentLogReasonCode.ActionProcessingException,
                exception
            )
        );

        Assert.NotNull(captured);
        var text = Assert.IsType<string>(captured!.Data);
        foreach (
            var secret in new[]
            {
                "bearer-secret",
                "token-secret",
                "account-secret",
                "body-secret",
                "url-secret",
                "fragment",
                "/Users/alice",
            }
        )
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted>", text);
        Assert.Contains("https://example.com/fail", text);
        Assert.Contains("<absolute-path>", text);
    }

    [Theory]
    [InlineData(BazaarAgentLogSeverity.Debug, LogLevel.Debug)]
    [InlineData(BazaarAgentLogSeverity.Info, LogLevel.Info)]
    [InlineData(BazaarAgentLogSeverity.Warning, LogLevel.Warning)]
    [InlineData(BazaarAgentLogSeverity.Error, LogLevel.Error)]
    public void Adapter_maps_every_contract_severity(
        BazaarAgentLogSeverity severity,
        LogLevel expectedLevel
    )
    {
        using var source = new ManualLogSource("test");
        LogEventArgs? captured = null;
        source.LogEvent += (_, args) => captured = args;
        var adapter = new BazaarAgentBepInExLogger(source);
        var logEvent = severity switch
        {
            BazaarAgentLogSeverity.Debug => BazaarAgentLogEvents.ListenerRestartStarted(
                47900,
                47901
            ),
            BazaarAgentLogSeverity.Info => BazaarAgentLogEvents.HostInitialized(),
            BazaarAgentLogSeverity.Warning => BazaarAgentLogEvents.ContextDegraded(
                BazaarAgentLogReasonCode.ContextBuildException
            ),
            BazaarAgentLogSeverity.Error => BazaarAgentLogEvents.HostInitializationFailed(),
            _ => throw new ArgumentOutOfRangeException(nameof(severity)),
        };

        adapter.Emit(logEvent);

        Assert.NotNull(captured);
        Assert.Equal(expectedLevel, captured!.Level);
    }

    [Fact]
    public void Adapter_suppresses_matching_warning_keys_and_flushes_a_summary()
    {
        using var source = new ManualLogSource("test");
        var captured = new List<LogEventArgs>();
        source.LogEvent += (_, args) => captured.Add(args);
        var adapter = new BazaarAgentBepInExLogger(source, () => DateTimeOffset.UnixEpoch);

        adapter.Emit(
            BazaarAgentLogEvents.ListenerDegraded(
                47900,
                new InvalidOperationException("first bind failure")
            )
        );
        adapter.Emit(
            BazaarAgentLogEvents.ListenerDegraded(
                47900,
                new InvalidOperationException("different exception, same warning key")
            )
        );

        Assert.Single(captured);
        adapter.Dispose();
        Assert.Equal(2, captured.Count);
        var summary = Assert.IsType<string>(captured[1].Data);
        Assert.Equal(LogLevel.Info, captured[1].Level);
        Assert.Contains("event=agent.storm.suppressed", summary);
        Assert.Contains("source_event=agent.listener.degraded", summary);
        Assert.Contains("suppressed_count=1", summary);
        Assert.Contains("flush_reason=shutdown", summary);
    }

    [Fact]
    public void Adapter_dispose_flushes_disk_sinks_after_the_storm_summary()
    {
        using var source = new ManualLogSource("test");
        var captured = new List<LogEventArgs>();
        source.LogEvent += (_, args) => captured.Add(args);
        var countWhenFlushed = -1;
        var adapter = new BazaarAgentBepInExLogger(
            source,
            () => DateTimeOffset.UnixEpoch,
            () => countWhenFlushed = captured.Count
        );
        adapter.Emit(
            BazaarAgentLogEvents.ListenerDegraded(
                47900,
                new InvalidOperationException("first bind failure")
            )
        );
        adapter.Emit(
            BazaarAgentLogEvents.ListenerDegraded(
                47900,
                new InvalidOperationException("second bind failure")
            )
        );

        adapter.Dispose();

        Assert.Equal(2, captured.Count);
        Assert.Equal(captured.Count, countWhenFlushed);
    }

    [Fact]
    public void Adapter_error_key_uses_full_correlation_and_exception_fingerprint()
    {
        using var source = new ManualLogSource("test");
        var captured = new List<LogEventArgs>();
        source.LogEvent += (_, args) => captured.Add(args);
        var adapter = new BazaarAgentBepInExLogger(source, () => DateTimeOffset.UnixEpoch);
        var exception = new InvalidOperationException("same failure");

        adapter.Emit(
            BazaarAgentLogEvents.ActionFailed(
                "12345678-first",
                BazaarAgentActionKind.Wait,
                BazaarAgentLogReasonCode.ActionProcessingException,
                exception
            )
        );
        adapter.Emit(
            BazaarAgentLogEvents.ActionFailed(
                "12345678-second",
                BazaarAgentActionKind.Wait,
                BazaarAgentLogReasonCode.ActionProcessingException,
                exception
            )
        );
        adapter.Emit(
            BazaarAgentLogEvents.ActionFailed(
                "12345678-first",
                BazaarAgentActionKind.Wait,
                BazaarAgentLogReasonCode.ActionProcessingException,
                exception
            )
        );

        Assert.Equal(2, captured.Count);
        Assert.All(
            captured,
            item => Assert.Contains("request_id=12345678", Assert.IsType<string>(item.Data))
        );
        adapter.Dispose();
        Assert.Equal(3, captured.Count);
        Assert.Contains("suppressed_count=1", Assert.IsType<string>(captured[2].Data));
    }

#if !DEBUG
    [Fact]
    public void Debug_factory_is_not_evaluated_by_a_release_consumer()
    {
        var logger = new CapturingLogger();
        var factoryCalls = 0;

        logger.TryEmitDebug(() =>
        {
            factoryCalls++;
            return BazaarAgentLogEvents.ListenerRestartStarted(47900, 47901);
        });

        Assert.Equal(0, factoryCalls);
        Assert.Equal(0, logger.EmitCalls);
    }

    private sealed class CapturingLogger : IBazaarAgentLogger
    {
        internal int EmitCalls { get; private set; }

        public void Emit(BazaarAgentLogEvent logEvent) => EmitCalls++;
    }
#endif

    private sealed class ProjectedException(
        string message,
        string stackTrace,
        Exception? inner = null
    ) : Exception(message, inner)
    {
        public override string? StackTrace => stackTrace;
    }
}
