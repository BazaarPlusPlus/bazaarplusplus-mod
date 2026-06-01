#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.AutoBazaar;
using BepInEx.Configuration;
using UnityEngine;

namespace BazaarPlusPlus.Game.AutoBazaarHost;

internal sealed class AutoBazaarBepInExOptions : IAutoBazaarOptions
{
    private readonly ConfigEntry<bool> _enabled;
    private readonly ConfigEntry<int> _httpListenerPort;

    public AutoBazaarBepInExOptions(ConfigFile config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        _enabled = config.Bind(
            "AutoBazaar",
            "Enabled",
            true,
            "Master switch for the AutoBazaar HTTP endpoint. When true, a loopback HTTP server starts on the configured port. There is no in-game UI for this toggle; edit the cfg file to disable."
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
