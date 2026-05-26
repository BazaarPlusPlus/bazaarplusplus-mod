#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterPreviewItemBoardMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        var runtime = host.AddComponent<MonsterPreviewItemBoardRuntime>();
        runtime.Initialize(services);
    }

    public void Unmount(GameObject host)
    {
        var runtime = host.GetComponent<MonsterPreviewItemBoardRuntime>();
        if (runtime != null)
            Object.DestroyImmediate(runtime);
    }
}
