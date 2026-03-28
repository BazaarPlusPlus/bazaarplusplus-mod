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
        if (index < 0 || index >= _battles.Count)
            return;

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
        return _replayService.CanReplayBattle(SelectedBattle, out reason);
    }

    private bool CanDeleteSelectedRun(out string reason)
    {
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
        var battle = SelectedBattle;
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
}
