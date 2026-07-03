#nullable enable
using System;
using System.Runtime.CompilerServices;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.GameInterop.VoiceSubtitles;

internal static class VoiceSubtitlesInteropLog
{
    private const string Component = "VoiceSubtitles";
    private const int MaxFieldLength = 260;

    internal static void Info(string message) => BppLog.Info(Component, message);

    internal static void Warn(string message) => BppLog.Warn(Component, message);

    internal static string Field(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "<none>";

        return $"'{Shorten(Sanitize(value!), MaxFieldLength)}'";
    }

    internal static string ObjectId(object? value)
    {
        if (value == null)
            return "<null>";

        return $"{value.GetType().Name}#{RuntimeHelpers.GetHashCode(value):X8}";
    }

    private static string Sanitize(string value)
    {
        return value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return $"{value.Substring(0, Math.Max(0, maxLength - 3))}...";
    }
}
