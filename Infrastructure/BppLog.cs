#pragma warning disable CS0436
using System;
using BepInEx.Logging;

namespace BazaarPlusPlus;

internal static class BppLog
{
    private const string Prefix = "[BPP]";
    private static readonly object SyncRoot = new object();

    private static LogLevel _lastLevel;
    private static string _lastMessage;
    private static int _repeatCount;

    public static string Format(string component, string message)
    {
        return $"{Prefix}[{component}] {message}";
    }

    public static string FormatError(string component, string message, Exception ex)
    {
        return $"{Format(component, message)}{Environment.NewLine}{ex}";
    }

    public static void Debug(string component, string message)
    {
        Write(LogLevel.Debug, Format(component, message));
    }

    public static void Info(string component, string message)
    {
        Write(LogLevel.Info, Format(component, message));
    }

    public static void Warn(string component, string message)
    {
        Write(LogLevel.Warning, Format(component, message));
    }

    public static void Error(string component, string message)
    {
        Write(LogLevel.Error, Format(component, message));
    }

    public static void Error(string component, string message, Exception ex)
    {
        Write(LogLevel.Error, FormatError(component, message, ex));
    }

    public static void Flush()
    {
        var logger = ModState.Logger;
        if (logger == null)
            return;

        lock (SyncRoot)
        {
            FlushRepeatedMessage(logger);
            _repeatCount = 0;
            _lastMessage = null;
            _lastLevel = LogLevel.None;
        }
    }

    private static void Write(LogLevel level, string message)
    {
        var logger = ModState.Logger;
        if (logger == null)
            return;

        lock (SyncRoot)
        {
            if (_repeatCount > 0 && _lastLevel == level && string.Equals(_lastMessage, message, StringComparison.Ordinal))
            {
                _repeatCount++;
                return;
            }

            FlushRepeatedMessage(logger);
            Log(logger, level, message);
            _lastLevel = level;
            _lastMessage = message;
            _repeatCount = 1;
        }
    }

    private static void FlushRepeatedMessage(ManualLogSource logger)
    {
        if (_repeatCount <= 1 || string.IsNullOrEmpty(_lastMessage))
            return;

        Log(logger, _lastLevel, $"{_lastMessage} x{_repeatCount}");
    }

    private static void Log(ManualLogSource logger, LogLevel level, string message)
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
}
