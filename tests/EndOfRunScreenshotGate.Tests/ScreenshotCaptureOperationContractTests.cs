#nullable enable
using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Storage.RunScreenshot;
using BepInEx.Logging;

internal static class ScreenshotCaptureOperationContractTests
{
    internal static void Run(Assembly assembly)
    {
        var operationType = RequireType(
            assembly,
            "BazaarPlusPlus.Game.Screenshots.ScreenshotCaptureOperation"
        );
        var reasonType = RequireType(
            assembly,
            "BazaarPlusPlus.Game.Screenshots.ScreenshotCaptureReasonCode"
        );
        var metadataType = RequireType(
            assembly,
            "BazaarPlusPlus.Game.Screenshots.ScreenshotMetadataPersistenceOutcome"
        );
        var eventSourceType = RequireType(
            assembly,
            "BazaarPlusPlus.Game.Screenshots.ScreenshotCaptureLogEvents"
        );

        AssertEventCatalog(eventSourceType);
        InitializationAndCleanupDiagnosticsAreGoverned(assembly);
        SuccessEmitsOneTerminal(assembly, operationType, metadataType);
        RetryRetainsIdAndEmitsOneSuccess(assembly, operationType, reasonType, metadataType);
        FailureModesEachEmitOneTerminal(assembly, operationType, reasonType);
        MetadataFailureAndTimeoutDegradeVerifiedPng(assembly, operationType, metadataType);
        ContextResetPreservesVerifiedPng(assembly, operationType, metadataType);
        CompletionIsAtomicAcrossThreads(assembly, operationType, reasonType);
    }

    private static void InitializationAndCleanupDiagnosticsAreGoverned(Assembly assembly)
    {
        var diagnosticsType = RequireType(
            assembly,
            "BazaarPlusPlus.Game.Screenshots.ScreenshotCaptureDiagnostics"
        );
        var stageType = RequireType(
            assembly,
            "BazaarPlusPlus.Game.Screenshots.ScreenshotCaptureCleanupStage"
        );
        using var capture = new LogCapture(assembly);

        InvokeStatic(diagnosticsType, "ReportInitializationFailed", (object?)null);
        Assert(
            capture.Count("event=screenshots.capture.initialization_failed") == 1,
            "Missing output path should emit one initialization Error."
        );
        Assert(
            capture.Contains("reason_code=output_path_unavailable"),
            "Initialization failure should use a stable reason."
        );

        InvokeStatic(
            diagnosticsType,
            "ReportCleanupFailed",
            Enum.Parse(stageType, "LateFileDelete"),
            "shot-cleanup-12345678",
            "/Users/private/late.png",
            new IOException("cleanup failed")
        );
#if DEBUG
        Assert(
            capture.Count("event=screenshots.capture.cleanup_failed") == 1,
            "Cleanup failures should be Debug diagnostics."
        );
        Assert(!capture.Contains("/Users/private"), "Cleanup paths must be redacted.");
#else
        Assert(
            capture.Count("event=screenshots.capture.cleanup_failed") == 0,
            "Cleanup fields must not be evaluated or emitted in Release."
        );
#endif
    }

    private static void SuccessEmitsOneTerminal(
        Assembly assembly,
        Type operationType,
        Type metadataType
    )
    {
        using var capture = new LogCapture(assembly);
        var operation = CreateOperation(operationType, "shot-success-12345678", 100);
        Invoke(operationType, operation, "BeginAttempt");
        var saved = InvokeStatic(metadataType, "Saved");

        Assert(
            (bool)
                Invoke(
                    operationType,
                    operation,
                    "TryCompleteArtifact",
                    true,
                    "/Users/private/screenshots/result.png",
                    saved,
                    250L
                )!,
            "A verified PNG with saved metadata should complete."
        );
        Assert(capture.Count("event=screenshots.capture.succeeded") == 1, "Success is terminal.");
        Assert(capture.TerminalCount == 1, "Success should emit exactly one terminal event.");
        Assert(capture.Contains("screenshot_id=shot-suc"), "Screenshot ids render Short.");
        Assert(capture.Contains("attempt_count=1"), "Terminal includes attempt count.");
        Assert(!capture.Contains("/Users/private"), "Terminal paths must be redacted.");
    }

    private static void RetryRetainsIdAndEmitsOneSuccess(
        Assembly assembly,
        Type operationType,
        Type reasonType,
        Type metadataType
    )
    {
        using var capture = new LogCapture(assembly);
        var operation = CreateOperation(operationType, "shot-retry-12345678", 100);
        Invoke(operationType, operation, "BeginAttempt");
        Invoke(
            operationType,
            operation,
            "RecordAttemptFailure",
            Enum.Parse(reasonType, "CaptureSynchronousException"),
            new InvalidOperationException("first attempt"),
            true,
            150L
        );
        Invoke(operationType, operation, "BeginAttempt");
        var saved = InvokeStatic(metadataType, "Saved");
        Invoke(
            operationType,
            operation,
            "TryCompleteArtifact",
            true,
            "/tmp/retry.png",
            saved,
            300L
        );

        Assert(capture.TerminalCount == 1, "Retry success should emit exactly one terminal.");
        Assert(capture.Count("event=screenshots.capture.succeeded") == 1, "Retry can recover.");
        Assert(capture.Contains("attempt_count=2"), "Both attempts share one operation.");
        Assert(capture.WarningOrErrorCount == 0, "A successful retry is not degraded.");
    }

    private static void FailureModesEachEmitOneTerminal(
        Assembly assembly,
        Type operationType,
        Type reasonType
    )
    {
        foreach (
            var reasonName in new[]
            {
                "CaptureSynchronousException",
                "CaptureTaskFaulted",
                "CaptureReturnedNull",
                "ContextExpired",
                "CaptureTimeout",
            }
        )
        {
            using var capture = new LogCapture(assembly);
            var operation = CreateOperation(operationType, "shot-failed-12345678", 100);
            Invoke(operationType, operation, "BeginAttempt");
            Invoke(
                operationType,
                operation,
                "RecordAttemptFailure",
                Enum.Parse(reasonType, reasonName),
                reasonName.Contains("Exception", StringComparison.Ordinal)
                || reasonName.Contains("Faulted", StringComparison.Ordinal)
                    ? new InvalidOperationException("capture failed")
                    : null,
                false,
                300L
            );

            Assert(capture.TerminalCount == 1, $"{reasonName} should emit one terminal.");
            Assert(capture.Count("event=screenshots.capture.failed") == 1, $"{reasonName} fails.");
        }
    }

    private static void MetadataFailureAndTimeoutDegradeVerifiedPng(
        Assembly assembly,
        Type operationType,
        Type metadataType
    )
    {
        foreach (var status in new[] { "Failed", "TimedOut", "Unavailable" })
        {
            using var capture = new LogCapture(assembly);
            var operation = CreateOperation(operationType, "shot-metadata-12345678", 100);
            Invoke(operationType, operation, "BeginAttempt");
            var metadata =
                status == "Failed"
                    ? InvokeStatic(
                        metadataType,
                        status,
                        new InvalidOperationException("metadata failed")
                    )
                    : InvokeStatic(metadataType, status);
            Invoke(
                operationType,
                operation,
                "TryCompleteArtifact",
                true,
                "/tmp/metadata.png",
                metadata,
                400L
            );

            Assert(capture.TerminalCount == 1, $"Metadata {status} should close once.");
            Assert(
                capture.Count("event=screenshots.capture.degraded") == 1,
                $"Metadata {status} should degrade a verified PNG."
            );
            Assert(
                status == "TimedOut"
                    ? capture.Contains("artifact_status=metadata_pending")
                    : capture.Contains("artifact_status=file_only"),
                "Metadata degradation should classify the surviving artifact."
            );
            Assert(
                !(bool)
                    Invoke(
                        operationType,
                        operation,
                        "TryCompleteArtifact",
                        true,
                        "/tmp/late.png",
                        InvokeStatic(metadataType, "Saved"),
                        500L
                    )!,
                "Late metadata completion must lose the terminal race."
            );
            Assert(capture.TerminalCount == 1, "Late completion cannot emit twice.");
        }
    }

    private static void CompletionIsAtomicAcrossThreads(
        Assembly assembly,
        Type operationType,
        Type reasonType
    )
    {
        using var capture = new LogCapture(assembly);
        var operation = CreateOperation(operationType, "shot-race-12345678", 100);
        Invoke(operationType, operation, "BeginAttempt");
        Parallel.For(
            0,
            32,
            _ =>
                Invoke(
                    operationType,
                    operation,
                    "RecordAttemptFailure",
                    Enum.Parse(reasonType, "ContextExpired"),
                    null,
                    false,
                    200L
                )
        );
        Assert(capture.TerminalCount == 1, "Concurrent completion must remain one-shot.");
    }

    private static void ContextResetPreservesVerifiedPng(
        Assembly assembly,
        Type operationType,
        Type metadataType
    )
    {
        using var capture = new LogCapture(assembly);
        var operation = CreateOperation(operationType, "shot-reset-12345678", 100);
        Invoke(operationType, operation, "BeginAttempt");
        Invoke(
            operationType,
            operation,
            "RecordVerifiedArtifact",
            "/Users/private/screenshots/preserved.png"
        );

        Assert(
            (bool)Invoke(operationType, operation, "TryCompleteContextReset", 300L)!,
            "A reset during metadata persistence should close the operation."
        );
        Assert(capture.TerminalCount == 1, "Context reset should emit one terminal.");
        Assert(
            capture.Count("event=screenshots.capture.degraded") == 1,
            "A verified PNG must survive reset as a degraded artifact, not an Error."
        );
        Assert(
            capture.Contains("reason_code=context_expired")
                && capture.Contains("artifact_status=metadata_pending"),
            "Reset should retain the PNG and describe its pending metadata."
        );
        Assert(!capture.Contains("/Users/private"), "The retained PNG path must stay redacted.");
        Assert(
            !(bool)
                Invoke(
                    operationType,
                    operation,
                    "TryCompleteArtifact",
                    true,
                    "/tmp/late.png",
                    InvokeStatic(metadataType, "Saved"),
                    400L
                )!,
            "Late metadata completion cannot add another terminal after reset."
        );

        using var missingCapture = new LogCapture(assembly);
        var missing = CreateOperation(operationType, "shot-reset-missing-12345678", 100);
        Invoke(missing.GetType(), missing, "BeginAttempt");
        Assert(
            (bool)Invoke(missing.GetType(), missing, "TryCompleteContextReset", 300L)!,
            "A reset before any PNG should still close the operation."
        );
        Assert(
            missingCapture.Count("event=screenshots.capture.failed") == 1,
            "Reset without a verified PNG remains a terminal Error."
        );
        Assert(
            missingCapture.Contains("reason_code=context_expired"),
            "Reset without an artifact should use the fixed context-expired reason."
        );
    }

    private static object CreateOperation(Type type, string screenshotId, long startedAtMs)
    {
        return Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                [
                    screenshotId,
                    "run-private-12345678",
                    RunScreenshotCaptureSource.EndOfRunAuto,
                    startedAtMs,
                ],
                culture: null
            ) ?? throw new InvalidOperationException("Could not construct screenshot operation.");
    }

    private static void AssertEventCatalog(Type eventSourceType)
    {
        var definitions = eventSourceType
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType.Name == "BppLogEventDefinition")
            .Select(field => field.GetValue(null)!)
            .ToArray();
        var actual = definitions.ToDictionary(
            definition => (string)GetProperty(definition, "EventId")!,
            DescribeFields,
            StringComparer.Ordinal
        );
        Assert(
            actual.Count == 5
                && actual
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .SequenceEqual(
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["screenshots.capture.initialization_failed"] =
                                "reason_code:Public:Low:None",
                            ["screenshots.capture.succeeded"] = TerminalSchema,
                            ["screenshots.capture.degraded"] = TerminalSchema,
                            ["screenshots.capture.failed"] = TerminalSchema,
                            ["screenshots.capture.cleanup_failed"] =
                                "stage:Public:Low:None|screenshot_id:Public:High:Short|file_path:LocalPath:High:None",
                        }.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    ),
            "Screenshot capture event catalog should match S1-S5 exactly."
        );
    }

    private const string TerminalSchema =
        "screenshot_id:Public:High:Short|run_id:Public:High:Short|capture_source:Public:Low:None|reason_code:Public:Low:None|artifact_status:Public:Low:None|attempt_count:Public:Low:None|duration_ms:Public:High:None|file_path:LocalPath:High:None";

    private static string DescribeFields(object definition)
    {
        var fields = (IEnumerable)GetProperty(definition, "Fields")!;
        return string.Join(
            "|",
            fields
                .Cast<object>()
                .Select(field =>
                    string.Join(
                        ":",
                        GetProperty(field, "Name"),
                        GetProperty(field, "Privacy"),
                        GetProperty(field, "Cardinality"),
                        GetProperty(field, "Correlation")
                    )
                )
        );
    }

    private static object? GetProperty(object instance, string name) =>
        instance
            .GetType()
            .GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )!
            .GetValue(instance);

    private static object? Invoke(Type type, object instance, string name, params object?[] args)
    {
        var method =
            type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Missing method {type.FullName}.{name}");
        return method.Invoke(instance, args);
    }

    private static object InvokeStatic(Type type, string name, params object?[] args)
    {
        var method =
            type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing method {type.FullName}.{name}");
        return method.Invoke(null, args)!;
    }

    private static Type RequireType(Assembly assembly, string fullName) =>
        assembly.GetType(fullName, throwOnError: true)!;

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class LogCapture : IDisposable
    {
        private readonly ManualLogSource _source = new("ScreenshotCaptureOperation.Tests");
        private readonly List<LogEventArgs> _events = [];

        internal LogCapture(Assembly assembly)
        {
            _source.LogEvent += OnLogEvent;
            var logType = RequireType(assembly, "BazaarPlusPlus.Infrastructure.BppLog");
            logType
                .GetMethod(
                    "Install",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                )!
                .Invoke(null, [_source]);
        }

        internal int TerminalCount =>
            Count("event=screenshots.capture.succeeded")
            + Count("event=screenshots.capture.degraded")
            + Count("event=screenshots.capture.failed");

        internal int WarningOrErrorCount =>
            _events.Count(entry => entry.Level is LogLevel.Warning or LogLevel.Error);

        internal int Count(string value) =>
            _events.Count(entry =>
                entry.Data?.ToString()?.Contains(value, StringComparison.Ordinal) == true
            );

        internal bool Contains(string value) =>
            _events.Any(entry =>
                entry.Data?.ToString()?.Contains(value, StringComparison.Ordinal) == true
            );

        public void Dispose()
        {
            _source.LogEvent -= OnLogEvent;
            _source.Dispose();
        }

        private void OnLogEvent(object? sender, LogEventArgs args) => _events.Add(args);
    }
}
