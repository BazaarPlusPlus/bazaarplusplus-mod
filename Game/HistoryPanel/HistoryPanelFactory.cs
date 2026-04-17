#nullable enable
using System;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.Identity;
using BazaarPlusPlus.Game.Online;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelFactory
{
    public static HistoryPanelDependencies Create(
        IHistoryPanelRuntime runtime,
        ModOnlineClient onlineClient,
        AuthStore authStore
    )
    {
        if (runtime == null)
            throw new ArgumentNullException(nameof(runtime));
        if (onlineClient == null)
            throw new ArgumentNullException(nameof(onlineClient));
        if (authStore == null)
            throw new ArgumentNullException(nameof(authStore));

        HistoryPanelRepository? repository = null;
        if (!string.IsNullOrWhiteSpace(runtime.RunLogDatabasePath))
            repository = new HistoryPanelRepository(runtime.RunLogDatabasePath);

        var ghostSyncService = CreateGhostSyncService(repository, onlineClient, authStore);
        var dataService = new HistoryPanelDataService(repository, ghostSyncService);
        var replayService = new HistoryPanelReplayService(
            runtime.CombatReplayRuntimeAccessor,
            () => runtime.CombatReplayDirectoryPath,
            ghostSyncService
        );
        return new HistoryPanelDependencies(runtime, dataService, replayService, ghostSyncService);
    }

    private static GhostBattleSyncService? CreateGhostSyncService(
        HistoryPanelRepository? repository,
        ModOnlineClient onlineClient,
        AuthStore authStore
    )
    {
        if (repository == null)
            return null;

        return new GhostBattleSyncService(repository, onlineClient, authStore);
    }
}
