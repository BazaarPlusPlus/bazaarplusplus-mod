#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.Paths;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelMount : IBppMountable
{
    private readonly Func<CombatReplayRuntime?> _combatReplayRuntime;
    private readonly Func<ModOnlineClient?> _onlineClient;
    private readonly Func<BazaarDbLinkClient?> _accountLinkClient;
    private readonly Func<OverlayPanelHost?> _overlayHost;
    private readonly INativeCardPreviewHost _nativeCardPreviewHost;
    private IDisposable? _localeChangedSubscription;

    public HistoryPanelMount(
        Func<CombatReplayRuntime?> combatReplayRuntime,
        Func<ModOnlineClient?> onlineClient,
        Func<BazaarDbLinkClient?> accountLinkClient,
        Func<OverlayPanelHost?> overlayHost,
        INativeCardPreviewHost nativeCardPreviewHost
    )
    {
        _combatReplayRuntime = combatReplayRuntime;
        _onlineClient = onlineClient;
        _accountLinkClient = accountLinkClient;
        _overlayHost = overlayHost;
        _nativeCardPreviewHost =
            nativeCardPreviewHost ?? throw new ArgumentNullException(nameof(nativeCardPreviewHost));
    }

    public void Mount(GameObject host, IBppServices services)
    {
        var combatReplayRuntime = _combatReplayRuntime();
        if (combatReplayRuntime == null)
        {
            LogMissingDependency(HistoryPanelMountDependency.CombatReplayRuntime);
            return;
        }

        var overlayHost = _overlayHost();
        if (overlayHost == null)
        {
            LogMissingDependency(HistoryPanelMountDependency.OverlayPanelHost);
            return;
        }

        var onlineClient = _onlineClient();
        if (onlineClient == null)
        {
            LogMissingDependency(HistoryPanelMountDependency.OnlineClient);
            return;
        }

        var panel = host.AddComponent<HistoryPanel>();
        var runState = new HistoryPanelRunState(services.RunContext);

        // CombatReplayRuntime accessor is not a path and is not on services.Paths — pass it
        // straight through to Factory. Paths are startup-stable strings.
        panel.Configure(
            HistoryPanelFactory.Create(
                runState,
                onlineClient,
                () => combatReplayRuntime,
                PathConstants.RunLogDatabase(services.Paths.RequireDataRoot()),
                PathConstants.CombatReplays(services.Paths.RequireDataRoot()),
                PathConstants.CombatReplayVideos(services.Paths.RequireDataRoot()),
                services.Paths.PluginsDirectoryPath ?? string.Empty,
                _accountLinkClient(),
                () =>
                    HistoryPanelDecisions.IsAccountLinkCardAvailable(
                        services.Config.BazaarDbUploadEnabled?.Value ?? false,
                        services.GameBuild.Channel
                    )
            ),
            _nativeCardPreviewHost
        );
        // Register with the host only once fully configured; an unconfigured panel (skip paths
        // above) must stay invisible to overlay lifecycle routing.
        panel.AttachToOverlayHost(overlayHost);

        _localeChangedSubscription = services.EventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
            HistoryPanel.RefreshLocalization()
        );
    }

    private static void LogMissingDependency(HistoryPanelMountDependency dependency)
    {
        BppLog.ErrorEvent(
            HistoryPanelLogEvents.MountFailed,
            HistoryPanelLogEvents.MountDependency.Bind(dependency),
            HistoryPanelLogEvents.MountReasonCode.Bind(
                HistoryPanelMountReasonCode.DependencyUnavailable
            )
        );
    }

    public void Unmount(GameObject host)
    {
        _localeChangedSubscription?.Dispose();
        _localeChangedSubscription = null;

        var panel = host.GetComponent<HistoryPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
    }
}
