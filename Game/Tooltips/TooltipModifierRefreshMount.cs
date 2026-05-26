#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.Tooltips;

internal sealed class TooltipModifierRefreshMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var controller = host.AddComponent<TooltipModifierRefreshController>();
        controller.Initialize(services.Config, services.EncounterState);
    }

    public void Unmount(GameObject host)
    {
        var controller = host.GetComponent<TooltipModifierRefreshController>();
        if (controller != null)
            Object.DestroyImmediate(controller);
    }
}
