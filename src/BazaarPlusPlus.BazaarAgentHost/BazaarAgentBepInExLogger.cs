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
internal sealed class BazaarAgentBepInExLogger : IBazaarAgentLogger
{
    private const string Tag = "BazaarAgent";
    private readonly ManualLogSource _log;

    public BazaarAgentBepInExLogger(ManualLogSource log) =>
        _log = log ?? throw new ArgumentNullException(nameof(log));

    public void Info(string message) => _log.LogInfo($"[{Tag}] {message}");

    public void Warning(string message) => _log.LogWarning($"[{Tag}] {message}");

    public void Error(string message, Exception? exception = null) =>
        _log.LogError(exception is null ? $"[{Tag}] {message}" : $"[{Tag}] {message}\n{exception}");
}
