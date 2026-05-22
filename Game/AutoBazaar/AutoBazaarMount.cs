#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.AutoBazaar;

/// <summary>Wraps <see cref="AutoBazaarRuntime"/> as an <see cref="IBppMountable"/>
/// so it can be registered from the composition root instead of hand-wired in
/// <c>Plugin.AttachRuntimeComponents</c>. Holds the component reference so
/// unmount targets the exact instance it mounted.</summary>
internal sealed class AutoBazaarMount : IBppMountable
{
    private AutoBazaarRuntime? _runtime;

    public void Mount(GameObject host, IBppServices services)
    {
        _runtime = host.AddComponent<AutoBazaarRuntime>();
        _runtime.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        if (_runtime != null) UnityEngine.Object.DestroyImmediate(_runtime);
        _runtime = null;
    }
}
