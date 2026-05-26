#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var controller = host.AddComponent<EndOfRunScreenshotController>();
        controller.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        var controller = host.GetComponent<EndOfRunScreenshotController>();
        if (controller != null)
            Object.DestroyImmediate(controller);
    }
}
