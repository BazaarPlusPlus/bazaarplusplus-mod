using System;
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal static class BppLog
{
    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEventDefinition definition,
        Func<BppLogFieldValue[]> valuesFactory
    ) { }

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) { }

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) { }
}
