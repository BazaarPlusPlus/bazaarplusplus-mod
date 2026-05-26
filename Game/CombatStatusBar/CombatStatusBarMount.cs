#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed class CombatStatusBarMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var component = host.AddComponent<CombatStatusBar>();
        component.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        var component = host.GetComponent<CombatStatusBar>();
        if (component != null)
            Object.DestroyImmediate(component);
    }
}
