#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.Identity;
using BazaarPlusPlus.Game.Online;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelFactory
{
    public static HistoryPanelDependencies Create(IHistoryPanelRuntime runtime)
    {
        if (runtime == null)
            throw new ArgumentNullException(nameof(runtime));

        HistoryPanelRepository? repository = null;
        if (!string.IsNullOrWhiteSpace(runtime.RunLogDatabasePath))
            repository = new HistoryPanelRepository(runtime.RunLogDatabasePath);

        var installationStore = CreateInstallationStore();
        var ghostSyncService = CreateGhostSyncService(runtime, repository, installationStore);
        var dataService = new HistoryPanelDataService(
            repository,
            ghostSyncService,
            () => PlayerAccountIdResolver.ResolveCurrent(installationStore)
        );
        var replayService = new HistoryPanelReplayService(
            runtime.CombatReplayRuntimeAccessor,
            () => runtime.CombatReplayDirectoryPath,
            ghostSyncService
        );
        return new HistoryPanelDependencies(runtime, dataService, replayService, ghostSyncService);
    }

    private static GhostBattleSyncService? CreateGhostSyncService(
        IHistoryPanelRuntime runtime,
        HistoryPanelRepository? repository,
        InstallationRecordStore? installationStore
    )
    {
        if (repository == null || installationStore == null)
            return null;

        var routes = V3Routes.TryCreate(V3UploadDefaults.ApiBaseUrl);
        if (routes == null)
            return null;

        return new GhostBattleSyncService(
            repository,
            installationStore,
            routes,
            timeout: TimeSpan.FromSeconds(10)
        );
    }

    private static InstallationRecordStore? CreateInstallationStore()
    {
        var installationRecordPath = BppRuntimeHost.Paths.InstallationRecordPath;
        var installationPrivateKeyPath = BppRuntimeHost.Paths.InstallationPrivateKeyPath;
        if (
            string.IsNullOrWhiteSpace(installationRecordPath)
            || string.IsNullOrWhiteSpace(installationPrivateKeyPath)
        )
        {
            return null;
        }

        return new InstallationRecordStore(installationRecordPath, installationPrivateKeyPath);
    }
}
