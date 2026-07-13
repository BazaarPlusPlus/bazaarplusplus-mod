#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal sealed record CapturedHistoryLogEvent(
    string Severity,
    BppLogEventDefinition Definition,
    BppLogFieldValue[] Values,
    Exception? Exception
);

internal static class BppLog
{
    private static readonly List<CapturedHistoryLogEvent> Captured = [];

    internal static IReadOnlyList<CapturedHistoryLogEvent> Events => Captured;

    internal static void Reset() => Captured.Clear();

    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEventDefinition definition,
        Func<BppLogFieldValue[]> valuesFactory
    ) => Captured.Add(new("Debug", definition, valuesFactory(), null));

    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEventDefinition definition,
        Exception exception,
        Func<BppLogFieldValue[]> valuesFactory
    ) => Captured.Add(new("Debug", definition, valuesFactory(), exception));

    public static void InfoEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Captured.Add(new("Info", definition, values, null));

    public static void WarnEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Captured.Add(new("Warning", definition, values, null));

    public static void WarnEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => Captured.Add(new("Warning", definition, values, exception));

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Captured.Add(new("Error", definition, values, null));

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => Captured.Add(new("Error", definition, values, exception));
}
