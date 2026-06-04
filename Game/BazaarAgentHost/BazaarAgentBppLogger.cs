#nullable enable
using System;
using BazaarPlusPlus.BazaarAgent;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.BazaarAgentHost;

internal sealed class BazaarAgentBppLogger : IBazaarAgentLogger
{
    public void Info(string message) => BppLog.Info("BazaarAgent", message);

    public void Warning(string message) => BppLog.Warn("BazaarAgent", message);

    public void Error(string message, Exception? exception = null)
    {
        if (exception is null)
            BppLog.Error("BazaarAgent", message);
        else
            BppLog.Error("BazaarAgent", message, exception);
    }
}
