#pragma warning disable CS0436
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BazaarPlusPlus.Infrastructure.Logging;
using BepInEx;
using BepInEx.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal static class BppLog
{
    private const string Prefix = "[BPP]";

    private static ManualLogSource? _logger;
    private static readonly BppLogEmitter StructuredEmitter = new();
    private static readonly LogRepeatSuppressor Suppressor = new LogRepeatSuppressor(
        writeSink: WriteToLogger,
        formatSummary: (text, _) => Format("Logger", text)
    );

    public static void Install(ManualLogSource logger)
    {
        if (logger == null)
            return;

        try
        {
            Volatile.Write(ref _logger, logger);
            StructuredEmitter.Install(
                new BppLogPipeline(
                    new BppLogEventRenderer(CreateRedactionRoots()),
                    WriteStructuredToLogger,
                    () => DateTimeOffset.UtcNow
                )
            );
        }
        catch
        {
            // Logging installation must never prevent plugin startup.
        }
    }

    public static string Format(string component, string message) =>
        $"{Prefix}[{component}] {message}";

    public static string FormatError(string component, string message, Exception ex) =>
        $"{Format(component, message)}{Environment.NewLine}{SafeExceptionText(ex)}";

    [Conditional("DEBUG")]
    public static void Debug(string component, string message)
    {
        Emit(LogLevel.Debug, Format(component, message));
    }

    [Conditional("DEBUG")]
    public static void Debug(
        BppLogEventDefinition definition,
        Func<BppLogFieldValue[]> valuesFactory
    ) => StructuredEmitter.Debug(definition, valuesFactory);

    public static void Info(string component, string message) =>
        Emit(LogLevel.Info, Format(component, message));

    public static void Warn(string component, string message) =>
        Emit(LogLevel.Warning, Format(component, message));

    public static void Error(string component, string message) =>
        Emit(LogLevel.Error, Format(component, message));

    public static void Error(string component, string message, Exception ex) =>
        Emit(LogLevel.Error, FormatError(component, message, ex));

    public static void Info(BppLogEventDefinition definition, params BppLogFieldValue[] values) =>
        StructuredEmitter.Emit(BppLogSeverity.Info, definition, values);

    public static void Warn(BppLogEventDefinition definition, params BppLogFieldValue[] values) =>
        StructuredEmitter.Emit(BppLogSeverity.Warning, definition, values);

    public static void Warn(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => StructuredEmitter.Emit(BppLogSeverity.Warning, definition, values, exception);

    public static void Error(BppLogEventDefinition definition, params BppLogFieldValue[] values) =>
        StructuredEmitter.Emit(BppLogSeverity.Error, definition, values);

    public static void Error(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => StructuredEmitter.Emit(BppLogSeverity.Error, definition, values, exception);

    public static void RecoverStorm(BppLogEventDefinition definition) =>
        StructuredEmitter.RecoverStorm(definition);

    public static void Flush()
    {
        try
        {
            Suppressor.Flush();
        }
        catch
        {
            // The BepInEx listener may already be unavailable during shutdown.
        }
        StructuredEmitter.Flush();
    }

    private static void Emit(LogLevel level, string message)
    {
        try
        {
            if (Volatile.Read(ref _logger) == null)
                return;
            Suppressor.Write((int)level, message);
        }
        catch
        {
            // Legacy calls remain best effort during the expand phase.
        }
    }

    private static void WriteToLogger(int level, string message)
    {
        var logger = Volatile.Read(ref _logger);
        if (logger == null)
            return;

        try
        {
            var bepLevel = (LogLevel)level;
            WriteToLoggerCore(logger, bepLevel, message);
        }
        catch
        {
            // Never recursively report a listener failure.
        }
    }

    private static void WriteStructuredToLogger(BppLogSeverity severity, string message)
    {
        var logger = Volatile.Read(ref _logger);
        if (logger == null)
            throw new InvalidOperationException("The BepInEx logger is not installed.");

        var level = severity switch
        {
            BppLogSeverity.Debug => LogLevel.Debug,
            BppLogSeverity.Info => LogLevel.Info,
            BppLogSeverity.Warning => LogLevel.Warning,
            BppLogSeverity.Error => LogLevel.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(severity)),
        };
        WriteToLoggerCore(logger, level, message);
    }

    private static void WriteToLoggerCore(ManualLogSource logger, LogLevel level, string message)
    {
        switch (level)
        {
            case LogLevel.Debug:
                logger.LogDebug(message);
                return;
            case LogLevel.Info:
                logger.LogInfo(message);
                return;
            case LogLevel.Warning:
                logger.LogWarning(message);
                return;
            case LogLevel.Error:
                logger.LogError(message);
                return;
            default:
                logger.Log(level, message);
                return;
        }
    }

    private static BppLogRedactionRoots CreateRedactionRoots()
    {
        var gameRoot = SafePath(() => Paths.GameRootPath);
        var dataRoot = SafeCombine(gameRoot, "BazaarPlusPlusV4");
        var pluginRoot = SafePath(() => Paths.PluginPath);
        var homeRoot = SafePath(() =>
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        );
        return new BppLogRedactionRoots(gameRoot, dataRoot, pluginRoot, homeRoot);
    }

    private static string? SafePath(Func<string> pathFactory)
    {
        try
        {
            var path = pathFactory();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeCombine(string? root, string child)
    {
        try
        {
            return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, child);
        }
        catch
        {
            return null;
        }
    }

    private static string SafeExceptionText(Exception? exception)
    {
        if (exception == null)
            return "<exception-unavailable>";

        try
        {
            return exception.ToString();
        }
        catch
        {
            try
            {
                return exception.GetType().FullName ?? "<exception-unavailable>";
            }
            catch
            {
                return "<exception-unavailable>";
            }
        }
    }
}
