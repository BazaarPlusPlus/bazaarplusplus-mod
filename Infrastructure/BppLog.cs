#pragma warning disable CS0436
using System;

namespace BazaarPlusPlus;

internal static class BppLog
{
    public static string Format(string component, string message)
    {
        return $"[{component}] {message}";
    }

    public static string FormatError(string component, string message, Exception ex)
    {
        return $"{Format(component, message)}{Environment.NewLine}{ex}";
    }

    public static void Debug(string component, string message)
    {
        ModState.Logger?.LogDebug(Format(component, message));
    }

    public static void Info(string component, string message)
    {
        ModState.Logger?.LogInfo(Format(component, message));
    }

    public static void Warn(string component, string message)
    {
        ModState.Logger?.LogWarning(Format(component, message));
    }

    public static void Error(string component, string message)
    {
        ModState.Logger?.LogError(Format(component, message));
    }

    public static void Error(string component, string message, Exception ex)
    {
        ModState.Logger?.LogError(FormatError(component, message, ex));
    }
}
