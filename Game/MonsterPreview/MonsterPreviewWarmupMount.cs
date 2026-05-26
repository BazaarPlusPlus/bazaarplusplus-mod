#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterPreviewWarmupMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        host.AddComponent<MonsterPreviewWarmupController>();
    }

    public void Unmount(GameObject host)
    {
        var component = host.GetComponent<MonsterPreviewWarmupController>();
        if (component != null)
            Object.DestroyImmediate(component);
    }
}
