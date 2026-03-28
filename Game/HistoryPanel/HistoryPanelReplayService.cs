#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelReplayService
{
    private readonly Func<CombatReplayRuntime?> _runtimeAccessor;
    private readonly Func<string?> _replayDirectoryPathAccessor;
    private readonly HistoryPanelRepository? _repository;
    private readonly GhostBattleSyncService? _ghostSyncService;

    public HistoryPanelReplayService(
        Func<CombatReplayRuntime?> runtimeAccessor,
        Func<string?> replayDirectoryPathAccessor,
        HistoryPanelRepository? repository = null,
        GhostBattleSyncService? ghostSyncService = null
    )
    {
        _runtimeAccessor =
            runtimeAccessor ?? throw new ArgumentNullException(nameof(runtimeAccessor));
        _replayDirectoryPathAccessor =
            replayDirectoryPathAccessor
            ?? throw new ArgumentNullException(nameof(replayDirectoryPathAccessor));
        _repository = repository;
        _ghostSyncService = ghostSyncService;
    }

    public bool CanReplayBattle(HistoryBattleRecord? battle, out string reason)
    {
        if (battle == null)
        {
            reason = "Select a battle to replay.";
            return false;
        }

        var runtime = _runtimeAccessor();
        if (runtime == null)
        {
            reason = "Combat replay runtime is unavailable.";
            return false;
        }

        if (battle.Source == HistoryBattleSource.Ghost)
        {
            if (!runtime.CanReplaySavedCombats(out reason))
                return false;

            if (battle.ReplayDownloaded || battle.ReplayAvailable)
            {
                reason = string.Empty;
                return true;
            }

            reason = "Replay payload for the selected ghost battle is unavailable.";
            return false;
        }

        return runtime.CanReplaySavedBattle(battle.BattleId, out reason);
    }

    public string GetReplayActionLabel(HistoryBattleRecord? battle)
    {
        return
            battle?.Source == HistoryBattleSource.Ghost
            && !battle.ReplayDownloaded
            && battle.ReplayAvailable
            ? "Download Replay"
            : "Replay";
    }

    public bool TryReplayBattle(HistoryBattleRecord? battle, out string statusMessage)
    {
        if (battle == null)
        {
            statusMessage = "Select a battle to replay.";
            return false;
        }

        if (!CanReplayBattle(battle, out var reason))
        {
            statusMessage = reason;
            return false;
        }

        if (battle.Source == HistoryBattleSource.Ghost)
            return TryReplayGhostBattle(battle, out statusMessage);

        var runtime = _runtimeAccessor();
        if (runtime == null)
        {
            statusMessage = "Combat replay runtime is unavailable.";
            return false;
        }

        if (!runtime.ReplaySaved(battle.BattleId))
        {
            statusMessage = $"Replay rejected for battle {battle.BattleId}.";
            return false;
        }

        statusMessage = $"Starting replay for {battle.BattleId}.";
        return true;
    }

    private bool TryReplayGhostBattle(HistoryBattleRecord battle, out string statusMessage)
    {
        var runtime = _runtimeAccessor();
        if (runtime == null)
        {
            statusMessage = "Combat replay runtime is unavailable.";
            return false;
        }

        var replayDirectoryPath = _replayDirectoryPathAccessor();
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
        {
            statusMessage = "Combat replay directory path is unavailable.";
            return false;
        }

        if (!battle.ReplayDownloaded)
        {
            if (_ghostSyncService == null)
            {
                statusMessage = "Ghost replay download is unavailable.";
                return false;
            }

            var downloadResult = _ghostSyncService
                .DownloadReplayAsync(battle.BattleId, replayDirectoryPath, default)
                .GetAwaiter()
                .GetResult();
            if (!downloadResult.Succeeded)
            {
                statusMessage =
                    $"Failed to download ghost replay: {downloadResult.Error ?? "unknown_error"}";
                return false;
            }
        }

        if (_repository == null)
        {
            statusMessage = "History repository is unavailable.";
            return false;
        }

        var manifest = _repository.TryLoadGhostManifest(battle.BattleId);
        if (manifest == null)
        {
            statusMessage = $"Ghost manifest for battle {battle.BattleId} is unavailable.";
            return false;
        }

        var payloadStore = new CombatReplayPayloadStore(replayDirectoryPath);
        var payload = payloadStore.Load(battle.BattleId);
        if (payload == null)
        {
            statusMessage = $"Replay payload for battle {battle.BattleId} is unavailable.";
            return false;
        }

        if (!runtime.ReplayImportedBattle(manifest, payload))
        {
            statusMessage = $"Replay rejected for ghost battle {battle.BattleId}.";
            return false;
        }

        statusMessage = battle.ReplayDownloaded
            ? $"Starting replay for {battle.BattleId}."
            : $"Downloaded and starting replay for {battle.BattleId}.";
        return true;
    }

    public void CleanupReplayPayloads(IReadOnlyList<string> battleIds)
    {
        if (battleIds.Count == 0)
            return;

        var replayDirectoryPath = _replayDirectoryPathAccessor();
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return;

        var payloadStore = new CombatReplayPayloadStore(replayDirectoryPath);
        foreach (var battleId in battleIds)
        {
            try
            {
                payloadStore.Delete(battleId);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "HistoryPanel",
                    $"Failed to delete replay payload for battle {battleId}: {ex.Message}"
                );
            }
        }
    }
}
