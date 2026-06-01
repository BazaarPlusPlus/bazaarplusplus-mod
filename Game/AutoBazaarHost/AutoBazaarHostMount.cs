#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BepInEx.Configuration;
using UnityEngine;

namespace BazaarPlusPlus.Game.AutoBazaarHost;

/// <summary>Wraps AutoBazaar's Unity host so registration stays in the composition root.</summary>
internal sealed class AutoBazaarHostMount : IBppMountable
{
    private readonly ConfigFile _configFile;
    private AutoBazaarUnityRuntime? _runtime;

    public AutoBazaarHostMount(ConfigFile configFile)
    {
        _configFile = configFile;
    }

    public void Mount(GameObject host, IBppServices services)
    {
        _runtime = host.AddComponent<AutoBazaarUnityRuntime>();
        _runtime.Initialize(services, _configFile);
    }

    public void Unmount(GameObject host)
    {
        if (_runtime != null)
            UnityEngine.Object.DestroyImmediate(_runtime);
        _runtime = null;
    }
}
