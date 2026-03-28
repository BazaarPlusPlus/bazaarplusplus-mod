#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelDataService
{
    private readonly HistoryPanelRepository? _repository;
    private readonly GhostBattleSyncService? _ghostSyncService;

    public HistoryPanelDataService(
        HistoryPanelRepository? repository,
        GhostBattleSyncService? ghostSyncService = null
    )
    {
        _repository = repository;
        _ghostSyncService = ghostSyncService;
    }

    public bool IsAvailable => _repository != null;

    public bool DatabaseExists => _repository?.DatabaseExists ?? false;

    public bool CanSyncGhostBattles => _ghostSyncService != null;

    public bool TryLoadRecentRuns(
        int limit,
        out IReadOnlyList<HistoryRunRecord> runs,
        out string statusMessage,
        out Exception? error
    )
    {
        runs = Array.Empty<HistoryRunRecord>();
        error = null;

        if (_repository == null)
        {
            statusMessage = "Run log database path is unavailable.";
            return false;
        }

        try
        {
            runs = _repository.ListRecentRuns(limit);
            statusMessage = _repository.DatabaseExists
                ? $"Loaded {runs.Count} runs from sqlite."
                : "Database file does not exist yet.";
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = $"History load failed: {ex.Message}";
            error = ex;
            return false;
        }
    }

    public bool TryLoadBattles(
        string? runId,
        out IReadOnlyList<HistoryBattleRecord> battles,
        out Exception? error
    )
    {
        battles = Array.Empty<HistoryBattleRecord>();
        error = null;

        if (_repository == null || string.IsNullOrWhiteSpace(runId))
            return true;

        try
        {
            battles = _repository.ListBattlesByRun(runId);
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    public bool TryDeleteRun(
        string runId,
        out IReadOnlyList<string> battleIds,
        out Exception? error
    )
    {
        battleIds = Array.Empty<string>();
        error = null;

        if (_repository == null)
        {
            error = new InvalidOperationException("Run log repository is unavailable.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(runId))
            return true;

        try
        {
            battleIds = _repository.ListBattleIdsByRun(runId);
            _repository.DeleteRun(runId);
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    public bool TryLoadGhostBattles(
        int limit,
        out IReadOnlyList<HistoryBattleRecord> battles,
        out string statusMessage,
        out Exception? error
    )
    {
        battles = Array.Empty<HistoryBattleRecord>();
        error = null;

        if (_repository == null)
        {
            statusMessage = "Run log database path is unavailable.";
            return false;
        }

        try
        {
            battles = _repository.ListRecentGhostBattles(limit);
            statusMessage = $"Loaded {battles.Count} ghost battles from sqlite.";
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = $"Ghost history load failed: {ex.Message}";
            error = ex;
            return false;
        }
    }

    public bool TrySyncGhostBattles(out string statusMessage, out Exception? error)
    {
        error = null;
        if (_ghostSyncService == null)
        {
            statusMessage = "Ghost sync is unavailable.";
            return false;
        }

        try
        {
            var result = _ghostSyncService.SyncRecentBattlesAsync(default).GetAwaiter().GetResult();
            if (!result.Succeeded)
            {
                statusMessage = $"Ghost sync failed: {result.Error ?? "unknown_error"}";
                return false;
            }

            statusMessage = $"Synced {result.ImportedCount} ghost battles.";
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            statusMessage = $"Ghost sync failed: {ex.Message}";
            return false;
        }
    }
}
