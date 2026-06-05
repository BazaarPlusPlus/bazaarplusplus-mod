#nullable enable
using System;

namespace BazaarPlusPlus.BazaarAgent;

public static class BazaarAgentRuntimeDefaults
{
    public const int HttpListenerPort = 47900;
    public const int ActionTimeoutMilliseconds = 3000;
    public static readonly TimeSpan ActionMinDelay = TimeSpan.FromSeconds(1);
}

public interface IBazaarAgentOptions
{
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
