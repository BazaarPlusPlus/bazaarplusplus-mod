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
            BppLog.ErrorEvent(
                LiveBuildPanelLogEvents.MountFailed,
                LiveBuildPanelLogEvents.MountFailedReasonCode.Bind(
                    LiveBuildMountFailureReasonCode.OverlayHostUnavailable
                )
            );
            return;
        }

        var panel = host.AddComponent<LiveBuildPanel>();
        panel.AttachToOverlayHost(overlayHost);
    }

    public void Unmount(GameObject host)
    {
        var panel = host.GetComponent<LiveBuildPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
    }
}
