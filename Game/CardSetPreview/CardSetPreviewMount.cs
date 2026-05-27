#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Game.CardSetPreview;

internal sealed class CardSetPreviewMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        host.AddComponent<CardSetPreviewRuntime>();
    }

    public void Unmount(GameObject host)
    {
        var component = host.GetComponent<CardSetPreviewRuntime>();
        if (component != null)
            Object.DestroyImmediate(component);
    }
}
