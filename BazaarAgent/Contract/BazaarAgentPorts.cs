#nullable enable
using System;

namespace BazaarPlusPlus.BazaarAgent;

public interface IBazaarAgentOptions
{
    bool Enabled { get; }

    int HttpListenerPort { get; }

    int ActionTimeoutMilliseconds { get; }

    TimeSpan ActionMinDelay { get; }

    string EndpointFilePath { get; }

    string DecisionLogRoot { get; }
}

public interface IBazaarAgentContextReader
{
    BazaarAgentContext Build(double actionCooldownRemainingSeconds);
}

public interface IBazaarAgentActionDispatcher
{
    BazaarAgentDispatchResult Execute(
        BazaarAgentAction action,
        BazaarAgentContextSnapshot snapshot
    );
}

public readonly record struct BazaarAgentDispatchResult(bool Executed, string? Error);

public interface IBazaarAgentLogger
{
    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}

public interface IBazaarAgentClock
{
    double NowSeconds { get; }

    string UtcNowIsoString();
}
