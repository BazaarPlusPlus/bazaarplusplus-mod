#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BepInEx.Configuration;
using UnityEngine;

namespace BazaarPlusPlus.Game.BazaarAgentHost;

/// <summary>Wraps BazaarAgent's Unity host so registration stays in the composition root.</summary>
internal sealed class BazaarAgentHostMount : IBppMountable
{
    private readonly ConfigFile _configFile;
    private readonly Func<bool> _isReplayStartInProgress;
    private BazaarAgentUnityRuntime? _runtime;

    public BazaarAgentHostMount(ConfigFile configFile, Func<bool> isReplayStartInProgress)
    {
        _configFile = configFile;
        _isReplayStartInProgress = isReplayStartInProgress;
    }

    public void Mount(GameObject host, IBppServices services)
    {
        _runtime = host.AddComponent<BazaarAgentUnityRuntime>();
        _runtime.Initialize(services, _configFile, _isReplayStartInProgress);
    }

    public void Unmount(GameObject host)
    {
        if (_runtime != null)
            UnityEngine.Object.DestroyImmediate(_runtime);
        _runtime = null;
    }
}
