#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLoggingMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var controller = host.AddComponent<RunLoggingController>();
        controller.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        var controller = host.GetComponent<RunLoggingController>();
        if (controller != null)
            Object.DestroyImmediate(controller);
    }
}
