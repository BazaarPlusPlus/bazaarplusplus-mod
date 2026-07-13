#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal sealed record CapturedCollectionLogEvent(
    string Severity,
    BppLogEventDefinition Definition,
    BppLogFieldValue[] Values,
    Exception? Exception
);

internal static class BppLog
{
    private static readonly List<CapturedCollectionLogEvent> Captured = [];
    private static readonly List<(
        BppLogEventDefinition Definition,
        BppLogFieldValue[] Values
    )> Recovered = [];

    internal static IReadOnlyList<CapturedCollectionLogEvent> Events => Captured;
    internal static IReadOnlyList<(
        BppLogEventDefinition Definition,
        BppLogFieldValue[] Values
    )> Recoveries => Recovered;

    internal static void Reset()
    {
        Captured.Clear();
        Recovered.Clear();
    }

    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEventDefinition definition,
        Func<BppLogFieldValue[]> valuesFactory
    ) => Add("Debug", definition, valuesFactory(), null);

    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEventDefinition definition,
        Exception exception,
        Func<BppLogFieldValue[]> valuesFactory
    ) => Add("Debug", definition, valuesFactory(), exception);

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

    public static void RecoverStorm(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => Recovered.Add((definition, values));

    private static void Add(
        string severity,
        BppLogEventDefinition definition,
        BppLogFieldValue[] values,
        Exception? exception
    ) => Captured.Add(new CapturedCollectionLogEvent(severity, definition, values, exception));
}
