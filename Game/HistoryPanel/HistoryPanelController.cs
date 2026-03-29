#nullable enable
using System;
using BazaarPlusPlus;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private void RefreshData()
    {
        ClearDeleteRunConfirmation();
        _runs.Clear();
        _battles.Clear();
        _ghostBattles.Clear();
        InvalidateFilteredGhostBattles();

        if (_sectionMode == HistorySectionMode.Ghost)
        {
            RefreshGhostData();
            return;
        }

        if (!_dataService.TryLoadRecentRuns(40, out var runs, out var statusMessage, out var error))
        {
            _statusMessage = statusMessage;
            if (error != null)
            {
                BppLog.Error("HistoryPanel", "Failed to load history page data", error);
                RefreshUi();
                RefreshSelectedBattlePreview();
                return;
            }

            RefreshUi();
            return;
        }

        _runs.AddRange(runs);
        _selectedRunIndex = Mathf.Clamp(_selectedRunIndex, 0, Mathf.Max(0, _runs.Count - 1));
        LoadBattlesForSelectedRun();
        _previewSelectionMode = PreviewSelectionMode.Run;
        _statusMessage = statusMessage;

        RefreshUi();
        RefreshSelectedBattlePreview();
    }

    private void RefreshGhostData()
    {
        if (
            !_dataService.TryLoadGhostBattles(
                100,
                out var battles,
                out var statusMessage,
                out var error
            )
        )
        {
            _statusMessage = statusMessage;
            if (error != null)
            {
                BppLog.Error("HistoryPanel", "Failed to load ghost battle data", error);
                RefreshUi();
                RefreshSelectedBattlePreview();
                return;
            }

            RefreshUi();
            return;
        }

        _ghostBattles.AddRange(battles);
        InvalidateFilteredGhostBattles();
        _selectedGhostBattleIndex = Mathf.Clamp(
            _selectedGhostBattleIndex,
            0,
            Mathf.Max(0, FilteredGhostBattles.Count - 1)
        );
        _previewSelectionMode = PreviewSelectionMode.Battle;
        _statusMessage = statusMessage;

        RefreshUi();
        RefreshSelectedBattlePreview();
    }

    private void SetSectionMode(HistorySectionMode mode)
    {
        if (_sectionMode == mode)
            return;

        _sectionMode = mode;
        _previewSelectionMode =
            mode == HistorySectionMode.Ghost
                ? PreviewSelectionMode.Battle
                : PreviewSelectionMode.Run;
        RefreshData();
    }

    private void SetGhostBattleFilter(GhostBattleFilter filter)
    {
        if (_ghostBattleFilter == filter)
            return;

        _ghostBattleFilter = filter;
        InvalidateFilteredGhostBattles();
        _selectedGhostBattleIndex = Mathf.Clamp(
            _selectedGhostBattleIndex,
            0,
            Mathf.Max(0, FilteredGhostBattles.Count - 1)
        );
        _previewSelectionMode = PreviewSelectionMode.Battle;
        RefreshUi();
        RefreshSelectedBattlePreview();
    }

    private void SelectRun(int index)
    {
        if (index < 0 || index >= _runs.Count)
            return;

        if (_selectedRunIndex != index)
            ClearDeleteRunConfirmation();

        _selectedRunIndex = index;
        LoadBattlesForSelectedRun();
        _previewSelectionMode = PreviewSelectionMode.Run;
        RefreshUi();
        RefreshSelectedBattlePreview();
    }

    private void SelectBattle(int index)
    {
        var source = _sectionMode == HistorySectionMode.Ghost ? _ghostBattles : _battles;
        if (index < 0 || index >= source.Count)
            return;

        if (_sectionMode == HistorySectionMode.Ghost)
            _selectedGhostBattleIndex = index;
        else
            _selectedBattleIndex = index;
        _previewSelectionMode = PreviewSelectionMode.Battle;
        RefreshUi();
        RefreshSelectedBattlePreview();
    }

    private void LoadBattlesForSelectedRun()
    {
        _battles.Clear();
        _selectedBattleIndex = 0;

        var run = SelectedRun;
        if (
            _dataService.TryLoadBattles(run?.RunId, out var battles, out var error)
            && battles.Count > 0
        )
            _battles.AddRange(battles);

        if (error != null && run != null)
        {
            _statusMessage = $"Battle load failed: {error.Message}";
            BppLog.Error("HistoryPanel", $"Failed to load battles for run {run.RunId}", error);
        }
    }

    private bool CanReplaySelectedBattle(out string reason)
    {
        return _replayService.CanReplayBattle(ActiveSelectedBattle, out reason);
    }

    private bool CanDeleteSelectedRun(out string reason)
    {
        if (_sectionMode == HistorySectionMode.Ghost)
        {
            reason = "Ghost battles cannot be deleted from this panel yet.";
            return false;
        }

        var run = SelectedRun;
        if (run == null)
        {
            reason = "Select a run to delete.";
            return false;
        }

        if (string.Equals(run.RawStatus, "active", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Active runs cannot be deleted.";
            return false;
        }

        if (
            _runtime?.IsInGameRun == true
            && string.Equals(_runtime.CurrentServerRunId, run.RunId, StringComparison.Ordinal)
        )
        {
            reason = "The currently active gameplay run cannot be deleted.";
            return false;
        }

        if (!_dataService.IsAvailable)
        {
            reason = "Run log repository is unavailable.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void TryReplaySelectedBattle()
    {
        var battle = ActiveSelectedBattle;
        if (battle == null)
            return;

        if (!CanReplaySelectedBattle(out var replayUnavailableReason))
        {
            _statusMessage = replayUnavailableReason;
            RefreshUi();
            return;
        }

        if (!_replayService.TryReplayBattle(battle, out var statusMessage))
        {
            _statusMessage = statusMessage;
            RefreshUi();
            return;
        }

        _statusMessage = statusMessage;
        SetHistoryVisible(false);
    }

    private void TryDeleteSelectedRun()
    {
        var run = SelectedRun;
        if (run == null)
            return;

        if (!CanDeleteSelectedRun(out var reason))
        {
            ClearDeleteRunConfirmation();
            _statusMessage = reason;
            RefreshUi();
            return;
        }

        if (!IsDeleteRunConfirmationActive(run.RunId))
        {
            _deleteRunConfirmationRunId = run.RunId;
            _deleteRunConfirmationUntil = Time.unscaledTime + 5f;
            _statusMessage =
                $"Click Delete Run again within 5s to remove {HistoryPanelFormatter.ShortenRunId(run.RunId)}.";
            RefreshUi();
            return;
        }

        ClearDeleteRunConfirmation();

        if (!_dataService.TryDeleteRun(run.RunId, out var battleIds, out var error))
        {
            _statusMessage = $"Run delete failed: {error?.Message ?? "Unknown error"}";
            BppLog.Error(
                "HistoryPanel",
                $"Failed to delete run {run.RunId}",
                error ?? new InvalidOperationException("Unknown run delete failure.")
            );
            RefreshUi();
            return;
        }

        _replayService.CleanupReplayPayloads(battleIds);
        var deletedMessage =
            battleIds.Count > 0
                ? $"Deleted run {HistoryPanelFormatter.ShortenRunId(run.RunId)} and cleaned {battleIds.Count} linked battle records."
                : $"Deleted run {HistoryPanelFormatter.ShortenRunId(run.RunId)}.";
        RefreshData();
        _statusMessage = deletedMessage;
        RefreshUi();
    }

    private void ClearDeleteRunConfirmation()
    {
        _deleteRunConfirmationRunId = null;
        _deleteRunConfirmationUntil = 0f;
    }

    private bool IsDeleteRunConfirmationActive(string runId)
    {
        return !string.IsNullOrWhiteSpace(runId)
            && string.Equals(_deleteRunConfirmationRunId, runId, StringComparison.Ordinal)
            && Time.unscaledTime < _deleteRunConfirmationUntil;
    }

    private string GetDatabaseChipText()
    {
        if (!_dataService.IsAvailable)
            return "Unavailable";

        return _dataService.DatabaseExists ? "Connected" : "Missing";
    }

    private void TrySyncGhostBattles()
    {
        if (!_dataService.CanSyncGhostBattles)
        {
            _statusMessage = "Ghost sync is unavailable.";
            RefreshUi();
            return;
        }

        if (!_dataService.TrySyncGhostBattles(out var statusMessage, out var error))
        {
            _statusMessage = statusMessage;
            if (error != null)
                BppLog.Error("HistoryPanel", "Failed to sync ghost battles", error);
            RefreshUi();
            return;
        }

        _statusMessage = statusMessage;
        if (_sectionMode == HistorySectionMode.Ghost)
            RefreshData();
        else
            RefreshUi();
    }
}
