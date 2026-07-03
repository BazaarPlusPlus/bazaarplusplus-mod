#nullable enable

using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.OverlayPanels;

// Mounted before the Main Overlay Panel mounts (BppComposition order); panel mounts reach the
// host through the Host property via a nullable accessor.
internal sealed class OverlayPanelHostMount : IBppMountable
{
    public OverlayPanelHost? Host { get; private set; }

    public void Mount(GameObject host, IBppServices services)
    {
        Host = host.AddComponent<OverlayPanelHost>();
        BppLog.Info("OverlayPanelHostMount", "OverlayPanelHost mounted.");
    }

    public void Unmount(GameObject host)
    {
        var component = host.GetComponent<OverlayPanelHost>();
        if (component != null)
            UnityEngine.Object.DestroyImmediate(component);
        Host = null;
    }
}
