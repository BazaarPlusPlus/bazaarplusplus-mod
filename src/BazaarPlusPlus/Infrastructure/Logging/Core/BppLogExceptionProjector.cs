#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BazaarPlusPlus.Infrastructure.Logging;

internal sealed class BppLogExceptionProjector
{
    private const int MessageBudget = 512;
    private const int InnerMessageBudget = 256;
    private const int TypeBudget = 128;
    private const int StackBudget = 5000;

    private readonly BppLogValueFormatter _formatter;

    internal BppLogExceptionProjector(BppLogValueFormatter formatter)
    {
        _formatter = formatter;
    }

    internal ExceptionProjection Project(Exception exception)
    {
        var tokens = new List<string>();
        var truncated = false;
        AppendException(tokens, exception, "exception", MessageBudget, ref truncated);

        var current = SafeInnerException(exception);
        for (var depth = 1; depth <= 3 && current != null; depth++)
        {
            AppendException(
                tokens,
                current,
                "exception_inner_" + depth.ToString(CultureInfo.InvariantCulture),
                InnerMessageBudget,
                ref truncated
            );
            current = SafeInnerException(current);
        }
        if (current != null)
            truncated = true;

        var stack = SafeStackTrace(exception, out var stackUnavailable);
        if (stackUnavailable)
        {
            tokens.Add("exception_stack=<unavailable>");
        }
        else if (stack == null)
        {
            tokens.Add("exception_stack=null");
        }
        else
        {
            var rendered = _formatter.RenderExceptionText(stack, StackBudget, preserveTail: true);
            tokens.Add("exception_stack=" + rendered.Text);
            truncated |= rendered.Truncated;
        }

        return new ExceptionProjection(tokens, truncated);
    }

    private void AppendException(
        List<string> tokens,
        Exception exception,
        string prefix,
        int messageBudget,
        ref bool truncated
    )
    {
        var type = _formatter.RenderExceptionText(
            SafeTypeName(exception),
            TypeBudget,
            preserveTail: false
        );
        tokens.Add(prefix + "_type=" + type.Text);
        truncated |= type.Truncated;
        tokens.Add(prefix + "_hresult=" + SafeHResult(exception));

        var message = SafeMessage(exception, out var unavailable);
        if (unavailable)
        {
            tokens.Add(prefix + "_message=<unavailable>");
            return;
        }

        var rendered = _formatter.RenderExceptionText(
            message ?? string.Empty,
            messageBudget,
            preserveTail: false
        );
        tokens.Add(prefix + "_message=" + rendered.Text);
        truncated |= rendered.Truncated;
    }

    private static string SafeTypeName(Exception exception)
    {
        try
        {
            return exception.GetType().FullName ?? exception.GetType().Name;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string SafeHResult(Exception exception)
    {
        try
        {
            return "0x"
                + unchecked((uint)exception.HResult).ToString("X8", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string? SafeMessage(Exception exception, out bool unavailable)
    {
        try
        {
            unavailable = false;
            return exception.Message;
        }
        catch
        {
            unavailable = true;
            return null;
        }
    }

    private static string? SafeStackTrace(Exception exception, out bool unavailable)
    {
        try
        {
            unavailable = false;
            return exception.StackTrace;
        }
        catch
        {
            unavailable = true;
            return null;
        }
    }

    private static Exception? SafeInnerException(Exception exception)
    {
        try
        {
            return exception.InnerException;
        }
        catch
        {
            return null;
        }
    }
}

internal readonly struct ExceptionProjection
{
    internal ExceptionProjection(IReadOnlyList<string> tokens, bool truncated)
    {
        Tokens = tokens;
        Truncated = truncated;
    }

    internal IReadOnlyList<string> Tokens { get; }

    internal bool Truncated { get; }
}
