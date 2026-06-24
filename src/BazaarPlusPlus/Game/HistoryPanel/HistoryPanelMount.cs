#nullable enable
using System;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Clients;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelMount : IBppMountable
{
    private readonly Func<CombatReplayRuntime?> _combatReplayRuntime;
    private readonly Func<ModOnlineClient?> _onlineClient;
    private readonly Func<BazaarDbLinkClient?> _accountLinkClient;
    private IDisposable? _localeChangedSubscription;

    public HistoryPanelMount(
        Func<CombatReplayRuntime?> combatReplayRuntime,
        Func<ModOnlineClient?> onlineClient,
        Func<BazaarDbLinkClient?> accountLinkClient
    )
    {
        _combatReplayRuntime = combatReplayRuntime;
        _onlineClient = onlineClient;
        _accountLinkClient = accountLinkClient;
    }

    public void Mount(GameObject host, IBppServices services)
    {
        var combatReplayRuntime = _combatReplayRuntime();
        if (combatReplayRuntime == null)
        {
            BppLog.Warn("HistoryPanelMount", "CombatReplayRuntime unavailable; skipping mount.");
            return;
        }

        var panel = host.AddComponent<HistoryPanel>();
        var runtime = new HistoryPanelRuntime(
            services.RunContext,
            services.Paths.RunLogDatabasePath,
            services.Paths.CombatReplayDirectoryPath,
            services.Paths.CombatReplayVideoDirectoryPath,
            services.Paths.PluginsDirectoryPath,
            () => combatReplayRuntime
        );

        var onlineClient = _onlineClient();
        if (onlineClient == null)
        {
            BppLog.Warn(
                "HistoryPanelMount",
                "Online client unavailable; HistoryPanel left unconfigured."
            );
            return;
        }

        panel.Configure(
            HistoryPanelFactory.Create(
                runtime,
                onlineClient,
                _accountLinkClient(),
                () => services.Config.BazaarDbUploadEnabled?.Value ?? false
            )
        );

        _localeChangedSubscription = services.EventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
            HistoryPanel.RefreshLocalization()
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
