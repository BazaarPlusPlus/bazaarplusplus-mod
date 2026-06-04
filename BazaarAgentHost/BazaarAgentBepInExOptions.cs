#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.BazaarAgent;
using BepInEx.Configuration;
using UnityEngine;

namespace BazaarPlusPlus.BazaarAgentHost;

internal sealed class BazaarAgentBepInExOptions : IBazaarAgentOptions
{
    private readonly ConfigEntry<bool> _enabled;
    private readonly ConfigEntry<int> _httpListenerPort;

    public BazaarAgentBepInExOptions(ConfigFile config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        _enabled = config.Bind(
            "BazaarAgent",
            "Enabled",
            false,
            "Runtime switch for the BazaarAgent HTTP endpoint. Only takes effect when the BazaarAgent host plugin is installed."
        );
        _httpListenerPort = config.Bind(
            "BazaarAgent",
            "HttpListenerPort",
            47900,
            "Loopback port for the BazaarAgent HTTP listener. Changing this restarts the listener."
        );
    }

    public bool Enabled => _enabled.Value;

    public int HttpListenerPort => _httpListenerPort.Value;

    public int ActionTimeoutMilliseconds => 3000;

    public TimeSpan ActionMinDelay => TimeSpan.FromSeconds(1);

    public string DecisionLogRoot =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BazaarPlusPlus", "BazaarAgent"));
}
