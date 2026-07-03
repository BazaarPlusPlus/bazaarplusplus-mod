#nullable enable

using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal sealed class LiveBuildPanelMount : IBppMountable
{
    private readonly Func<OverlayPanelHost?> _overlayHost;

    public LiveBuildPanelMount(Func<OverlayPanelHost?> overlayHost)
    {
        _overlayHost = overlayHost;
    }

    public void Mount(GameObject host, IBppServices services)
    {
        var overlayHost = _overlayHost();
        if (overlayHost == null)
        {
            BppLog.Warn("LiveBuildPanelMount", "Overlay panel host unavailable; skipping mount.");
            return;
        }

        var panel = host.AddComponent<LiveBuildPanel>();
        panel.AttachToOverlayHost(overlayHost);
        BppLog.Info("LiveBuildPanelMount", "LiveBuildPanel mounted.");
    }

    public void Unmount(GameObject host)
    {
        var panel = host.GetComponent<LiveBuildPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
    }
}
