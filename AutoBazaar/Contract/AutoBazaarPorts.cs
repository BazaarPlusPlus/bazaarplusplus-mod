#nullable enable
using System;

namespace BazaarPlusPlus.AutoBazaar;

public interface IAutoBazaarOptions
{
    bool Enabled { get; }

    int HttpListenerPort { get; }

    int ActionTimeoutMilliseconds { get; }

    TimeSpan ActionMinDelay { get; }

    string EndpointFilePath { get; }

    string DecisionLogRoot { get; }
}

public interface IAutoBazaarContextReader
{
    AutoBazaarContext Build(double actionCooldownRemainingSeconds);
}

public interface IAutoBazaarActionDispatcher
{
    AutoBazaarDispatchResult Execute(AutoBazaarAction action, AutoBazaarContextSnapshot snapshot);
}

public readonly record struct AutoBazaarDispatchResult(bool Executed, string? Error);

public interface IAutoBazaarLogger
{
    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}

public interface IAutoBazaarClock
{
    double NowSeconds { get; }

    string UtcNowIsoString();
}
