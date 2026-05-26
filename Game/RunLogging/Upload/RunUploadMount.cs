#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunUploadMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var controller = host.AddComponent<RunUploadController>();
        controller.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        var controller = host.GetComponent<RunUploadController>();
        if (controller != null)
            Object.DestroyImmediate(controller);
    }
}
