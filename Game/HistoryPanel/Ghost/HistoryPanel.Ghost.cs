#nullable enable
using System;
using BazaarPlusPlus;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.ModApi;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private GhostBattleSyncService? TryCreateGhostSyncService(HistoryPanelRepository? repository)
    {
        if (repository == null || BppRuntimeHost.Config.EnableRunUploadConfig?.Value != true)
            return null;

        var identityPath = BppRuntimeHost.Paths.RunUploadInstallIdentityPath;
        var clientStatePath = BppRuntimeHost.Paths.RunUploadClientStatePath;
        var privateKeyPath = BppRuntimeHost.Paths.RunUploadPrivateKeyPath;
        var context = ModApiBootstrapContext.TryCreate(
            _runtime?.RunLogDatabasePath,
            _runtime?.CombatReplayDirectoryPath,
            identityPath,
            clientStatePath,
            privateKeyPath,
            ModApiDefaults.ApiBaseUrl
        );
        if (context == null)
            return null;

        return new GhostBattleSyncService(
            repository,
            context.CreateIdentityStore(),
            context.CreateClientStateStore(),
            context.CreateKeyStore(),
            context.Routes,
            timeout: TimeSpan.FromSeconds(10)
        );
    }
}
