#nullable enable
using System;
using BazaarPlusPlus.BazaarAgent;
using BepInEx.Logging;

namespace BazaarPlusPlus.BazaarAgentHost;

/// <summary>
/// <see cref="IBazaarAgentLogger"/> implementation backed by the host plugin's own BepInEx
/// <see cref="ManualLogSource"/>. The host is a separate assembly and cannot reach
/// BazaarPlusPlus's internal <c>BppLog</c>, so it logs through its own source. Lines carry the
/// <c>[BazaarAgent]</c> tag to match the prior LogOutput.log prefix.
/// </summary>
internal sealed class BazaarAgentBepInExLogger : IBazaarAgentLogger, IDisposable
{
    [ThreadStatic]
    private static bool _insideSink;

    private readonly ManualLogSource _log;
    private readonly BazaarAgentLogRenderer _renderer = new();
    private readonly BazaarAgentLogStormGuard _stormGuard;

    public BazaarAgentBepInExLogger(ManualLogSource log)
        : this(log, clock: null) { }

    internal BazaarAgentBepInExLogger(ManualLogSource log, Func<DateTimeOffset>? clock)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _stormGuard = new BazaarAgentLogStormGuard(WriteSink, _renderer.Render, clock);
    }

    public void Emit(BazaarAgentLogEvent logEvent)
    {
        if (_insideSink)
            return;
        try
        {
            var text = _renderer.Render(logEvent);
            _stormGuard.Emit(logEvent, text);
        }
        catch
        {
            // Rendering, storm control, and sink failures must never escape into Agent workflows.
        }
    }

    public void Dispose() => _stormGuard.Flush();

    private bool WriteSink(BazaarAgentLogSeverity severity, string text)
    {
        if (_insideSink)
            return false;
        try
        {
            _insideSink = true;
            switch (severity)
            {
                case BazaarAgentLogSeverity.Debug:
                    _log.LogDebug(text);
                    break;
                case BazaarAgentLogSeverity.Info:
                    _log.LogInfo(text);
                    break;
                case BazaarAgentLogSeverity.Warning:
                    _log.LogWarning(text);
                    break;
                case BazaarAgentLogSeverity.Error:
                    _log.LogError(text);
                    break;
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _insideSink = false;
        }
    }
}
