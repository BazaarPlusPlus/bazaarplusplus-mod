using System;

namespace BazaarPlusPlus.Infrastructure;

internal static class BppLog
{
    public static void Info(string component, string message) { }

    public static void Warn(string component, string message) { }

    public static void Error(string component, string message, Exception ex) { }
}
