#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal sealed record CapturedBppLogEvent(
    string Severity,
    BppLogEventDefinition Definition,
    BppLogFieldValue[] Values,
    Exception? Exception
);

internal static class BppLog
{
    private static readonly object Gate = new();
    private static readonly List<CapturedBppLogEvent> Captured = [];

    internal static IReadOnlyList<CapturedBppLogEvent> Events
    {
        get
        {
            lock (Gate)
                return Captured.ToArray();
        }
    }

    internal static void Reset()
    {
        lock (Gate)
            Captured.Clear();
    }

    public static void InfoEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Add("Info", definition, values, null);

    public static void WarnEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Add("Warning", definition, values, null);

    public static void WarnEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => Add("Warning", definition, values, exception);

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Add("Error", definition, values, null);

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => Add("Error", definition, values, exception);

    private static void Add(
        string severity,
        BppLogEventDefinition definition,
        BppLogFieldValue[] values,
        Exception? exception
    )
    {
        lock (Gate)
            Captured.Add(new CapturedBppLogEvent(severity, definition, values, exception));
    }
}
