#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.Identity;
using TheBazaar;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelDataService
{
    private readonly HistoryPanelRepository? _repository;
    private readonly GhostBattleSyncService? _ghostSyncService;
    private readonly Func<string?> _currentPlayerAccountIdAccessor;

    public HistoryPanelDataService(
        HistoryPanelRepository? repository,
        GhostBattleSyncService? ghostSyncService = null,
        Func<string?>? currentPlayerAccountIdAccessor = null
    )
    {
        _repository = repository;
        _ghostSyncService = ghostSyncService;
        _currentPlayerAccountIdAccessor =
            currentPlayerAccountIdAccessor ?? PlayerAccountIdResolver.ResolveCurrent;
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
            statusMessage = HistoryPanelText.RunLogDatabasePathUnavailable();
            return false;
        }

        try
        {
            runs = _repository.ListRecentRuns(limit);
            statusMessage = _repository.DatabaseExists
                ? HistoryPanelText.LoadedRuns(runs.Count)
                : HistoryPanelText.DatabaseFileMissing();
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = HistoryPanelText.HistoryLoadFailed(ex.Message);
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
            error = new InvalidOperationException(HistoryPanelText.RunLogRepositoryUnavailable());
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
            statusMessage = HistoryPanelText.RunLogDatabasePathUnavailable();
            return false;
        }

        try
        {
            var localPlayerAccountId = _currentPlayerAccountIdAccessor();
            if (string.IsNullOrWhiteSpace(localPlayerAccountId))
            {
                statusMessage = HistoryPanelText.CurrentPlayerAccountUnavailable();
                return false;
            }

            battles = _repository.ListRecentGhostBattles(localPlayerAccountId, limit);
            statusMessage = HistoryPanelText.LoadedGhostBattles(battles.Count);
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = HistoryPanelText.GhostHistoryLoadFailed(ex.Message);
            error = ex;
            return false;
        }
    }

    public async Task<HistoryPanelGhostSyncAttemptResult> SyncGhostBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        if (_ghostSyncService == null)
            return HistoryPanelGhostSyncAttemptResult.Failure(
                HistoryPanelText.GhostSyncUnavailable()
            );

        try
        {
            var result = await _ghostSyncService.SyncRecentBattlesAsync(cancellationToken);
            if (!result.Succeeded)
                return HistoryPanelGhostSyncAttemptResult.Failure(
                    HistoryPanelText.GhostSyncFailed(result.Error ?? HistoryPanelText.Unknown())
                );

            return HistoryPanelGhostSyncAttemptResult.Success(
                HistoryPanelText.GhostSyncSucceeded(result.ImportedCount)
            );
        }
        catch (Exception ex)
        {
            return HistoryPanelGhostSyncAttemptResult.Failure(
                HistoryPanelText.GhostSyncFailed(ex.Message),
                ex
            );
        }
    }
}

internal readonly struct HistoryPanelGhostSyncAttemptResult
{
    private HistoryPanelGhostSyncAttemptResult(
        bool succeeded,
        string statusMessage,
        Exception? error
    )
    {
        Succeeded = succeeded;
        StatusMessage = statusMessage;
        Error = error;
    }

    public bool Succeeded { get; }

    public string StatusMessage { get; }

    public Exception? Error { get; }

    public static HistoryPanelGhostSyncAttemptResult Success(string statusMessage) =>
        new(true, statusMessage, null);

    public static HistoryPanelGhostSyncAttemptResult Failure(
        string statusMessage,
        Exception? error = null
    ) => new(false, statusMessage, error);
}
