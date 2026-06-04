#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.BazaarAgent;
using BepInEx.Configuration;
using UnityEngine;

namespace BazaarPlusPlus.Game.BazaarAgentHost;

internal sealed class BazaarAgentBepInExOptions : IBazaarAgentOptions
{
    private readonly ConfigEntry<bool> _enabled;
    private readonly ConfigEntry<int> _httpListenerPort;

    public BazaarAgentBepInExOptions(ConfigFile config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        _enabled = config.Bind(
            "AutoBazaar",
            "Enabled",
            false,
            "Runtime switch for the AutoBazaar HTTP endpoint. This only has an effect when the mod was built with EnableAutoBazaarHost=true."
        );
        _httpListenerPort = config.Bind(
            "AutoBazaar",
            "HttpListenerPort",
            47900,
            "Loopback port for the AutoBazaar HTTP listener. Changing this restarts the listener."
        );
    }

    public bool Enabled => _enabled.Value;

    public int HttpListenerPort => _httpListenerPort.Value;

    public int ActionTimeoutMilliseconds => 3000;

    public TimeSpan ActionMinDelay => TimeSpan.FromSeconds(1);

    public string DecisionLogRoot =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BazaarPlusPlus", "AutoBazaar"));

    public string EndpointFilePath => Path.Combine(DecisionLogRoot, "endpoint.json");
}
