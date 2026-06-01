#nullable enable
using System;
using BazaarPlusPlus.AutoBazaar;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.AutoBazaarHost;

internal sealed class AutoBazaarBppLogger : IAutoBazaarLogger
{
    public void Info(string message) => BppLog.Info("AutoBazaar", message);

    public void Warning(string message) => BppLog.Warn("AutoBazaar", message);

    public void Error(string message, Exception? exception = null)
    {
        if (exception is null)
            BppLog.Error("AutoBazaar", message);
        else
            BppLog.Error("AutoBazaar", message, exception);
    }
}
