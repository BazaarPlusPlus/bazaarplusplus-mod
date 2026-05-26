#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbScreenshotUploadMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var controller = host.AddComponent<BazaarDbScreenshotUploadController>();
        controller.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        var controller = host.GetComponent<BazaarDbScreenshotUploadController>();
        if (controller != null)
            Object.DestroyImmediate(controller);
    }
}
