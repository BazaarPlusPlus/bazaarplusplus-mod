#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus;
using BazaarPlusPlus.Game.CombatReplay;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelReplayService
{
    private readonly Func<CombatReplayRuntime?> _runtimeAccessor;
    private readonly Func<string?> _replayDirectoryPathAccessor;

    public HistoryPanelReplayService(
        Func<CombatReplayRuntime?> runtimeAccessor,
        Func<string?> replayDirectoryPathAccessor
    )
    {
        _runtimeAccessor = runtimeAccessor ?? throw new ArgumentNullException(nameof(runtimeAccessor));
        _replayDirectoryPathAccessor =
            replayDirectoryPathAccessor
            ?? throw new ArgumentNullException(nameof(replayDirectoryPathAccessor));
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

        return runtime.CanReplaySavedBattle(battle.BattleId, out reason);
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
