#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Clients;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelCoordinator : IDisposable
{
    private readonly HistoryPanelState _state;
    private readonly IHistoryPanelRuntime _runtime;
    private readonly HistoryPanelDataService _dataService;
    private readonly HistoryPanelReplayService _replayService;
    private readonly IHistoryPanelServerHealthProbe? _serverHealthProbe;
    private readonly BazaarDbLinkClient? _linkClient;
    private readonly BazaarDbAccountLinkStore _accountLinkStore = new();
    private readonly Action _requestUiRefresh;
    private readonly Action _requestPreviewRefresh;
    private readonly Action<bool> _requestVisibilityChange;
    private readonly HistoryPanelSessionScope _session = new();

    public HistoryPanelCoordinator(
        HistoryPanelState state,
        HistoryPanelDependencies dependencies,
        Action requestUiRefresh,
        Action requestPreviewRefresh,
        Action<bool> requestVisibilityChange
    )
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        if (dependencies == null)
            throw new ArgumentNullException(nameof(dependencies));
        _runtime = dependencies.Runtime;
        _dataService = dependencies.DataService;
        _replayService = dependencies.ReplayService;
        _serverHealthProbe = dependencies.ServerHealthProbe;
        _linkClient = dependencies.AccountLinkClient;
        _requestUiRefresh =
            requestUiRefresh ?? throw new ArgumentNullException(nameof(requestUiRefresh));
        _requestPreviewRefresh =
            requestPreviewRefresh ?? throw new ArgumentNullException(nameof(requestPreviewRefresh));
        _requestVisibilityChange =
            requestVisibilityChange
            ?? throw new ArgumentNullException(nameof(requestVisibilityChange));
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    public void OnPanelShown()
    {
        _session.Begin();
        RefreshAccountLinkIdentityFromGame();
        _state.ReplayActionInProgress = false;
        _state.IsVisible = true;
        RefreshSectionOnEntry();
    }

    public void OnPanelHidden()
    {
        _state.IsVisible = false;
        _state.GhostSyncInProgress = false;
        _state.ReplayActionInProgress = false;
        _state.ServerHealthProbeInProgress = false;
        _state.AccountLinkInProgress = false;
        ClearDeleteRunConfirmation();
        _session.End();
    }

    public void Tick(float now)
    {
        if (!_state.DeleteRunConfirmation.HasExpired(now))
            return;

        var shouldClearStatus = _state.ShouldClearStatusWhenDeleteConfirmationExpires();
        ClearDeleteRunConfirmation();
        if (shouldClearStatus)
            SetStatusMessage(null);
        _requestUiRefresh();
    }

    public void RefreshSectionOnEntry()
    {
        RefreshData();

        if (_state.SectionMode == HistorySectionMode.Ghost && _dataService.CanSyncGhostBattles)
            _ = TrySyncGhostBattlesAsync();
    }

    public void RefreshData()
    {
        ClearTransientStatus();
        ClearDeleteRunConfirmation();
        _state.Runs.Clear();
        _state.Battles.Clear();
        _state.GhostBattles.Clear();
        InvalidateFilteredRuns();
        InvalidateFilteredGhostBattles();

        if (_state.SectionMode == HistorySectionMode.Ghost)
        {
            RefreshGhostData();
            return;
        }

        if (!_dataService.TryLoadRecentRuns(40, out var runs, out var statusMessage, out var error))
        {
            SetStatusMessage(statusMessage);
            if (error != null)
            {
                BppLog.Error("HistoryPanel", "Failed to load history page data", error);
                _requestUiRefresh();
                _requestPreviewRefresh();
                return;
            }

            _requestUiRefresh();
            return;
        }

        _state.Runs.AddRange(runs);
        InvalidateFilteredRuns();
        _state.SelectedRunIndex = ClampIndex(_state.SelectedRunIndex, GetFilteredRuns().Count);
        LoadBattlesForSelectedRun();
        _state.PreviewSelectionMode = PreviewSelectionMode.Run;
        SetStatusMessage(statusMessage);

        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void RefreshGhostData()
    {
        ClearTransientStatus();
        _state.GhostBattles.Clear();
        InvalidateFilteredGhostBattles();
        if (
            !_dataService.TryLoadGhostBattles(
                100,
                out var battles,
                out var statusMessage,
                out var error
            )
        )
        {
            SetStatusMessage(statusMessage);
            if (error != null)
            {
                BppLog.Error("HistoryPanel", "Failed to load ghost battle data", error);
                _requestUiRefresh();
                _requestPreviewRefresh();
                return;
            }

            _requestUiRefresh();
            return;
        }

        _state.GhostBattles.AddRange(battles);
        InvalidateFilteredGhostBattles();
        _state.SelectedGhostBattleIndex = ClampIndex(
            _state.SelectedGhostBattleIndex,
            GetFilteredGhostBattles().Count
        );
        _state.PreviewSelectionMode = PreviewSelectionMode.Battle;
        SetStatusMessage(statusMessage);

        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void SetSectionMode(HistorySectionMode mode)
    {
        if (_state.SectionMode == mode)
            return;

        _state.SectionMode = mode;
        _state.PreviewSelectionMode =
            mode == HistorySectionMode.Ghost
                ? PreviewSelectionMode.Battle
                : PreviewSelectionMode.Run;
        RefreshSectionOnEntry();
    }

    public void SetGhostBattleFilter(GhostBattleFilter filter)
    {
        if (_state.GhostBattleFilter == filter)
            return;

        _state.GhostBattleFilter = filter;
        InvalidateFilteredGhostBattles();
        _state.SelectedGhostBattleIndex = ClampIndex(
            _state.SelectedGhostBattleIndex,
            GetFilteredGhostBattles().Count
        );
        _state.PreviewSelectionMode = PreviewSelectionMode.Battle;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void SetRunHeroFilter(string hero)
    {
        var selectedHero = string.IsNullOrEmpty(hero) ? null : hero;
        _state.SelectedRunHero =
            selectedHero != null
            && !string.Equals(
                _state.SelectedRunHero,
                selectedHero,
                StringComparison.OrdinalIgnoreCase
            )
                ? selectedHero
                : null;
        InvalidateFilteredRuns();
        _state.SelectedRunIndex = 0;
        ClearDeleteRunConfirmation();
        LoadBattlesForSelectedRun();
        _state.PreviewSelectionMode = PreviewSelectionMode.Run;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void ToggleGhostDayMin10()
    {
        SetGhostDayMin10(!_state.GhostDayMin10);
    }

    public void SetGhostDayMin10(bool value)
    {
        if (_state.GhostDayMin10 == value)
            return;

        _state.GhostDayMin10 = value;
        InvalidateFilteredGhostBattles();
        _state.SelectedGhostBattleIndex = ClampIndex(
            _state.SelectedGhostBattleIndex,
            GetFilteredGhostBattles().Count
        );
        _state.PreviewSelectionMode = PreviewSelectionMode.Battle;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void SelectRun(int index)
    {
        var filteredRuns = GetFilteredRuns();
        if (index < 0 || index >= filteredRuns.Count)
            return;

        if (_state.SelectedRunIndex != index)
            ClearDeleteRunConfirmation();

        _state.SelectedRunIndex = index;
        LoadBattlesForSelectedRun();
        _state.PreviewSelectionMode = PreviewSelectionMode.Run;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void SelectBattle(int index)
    {
        var source =
            _state.SectionMode == HistorySectionMode.Ghost
                ? GetFilteredGhostBattles()
                : (IReadOnlyList<HistoryBattleRecord>)_state.Battles;
        if (index < 0 || index >= source.Count)
            return;

        if (_state.SectionMode == HistorySectionMode.Ghost)
            _state.SelectedGhostBattleIndex = index;
        else
            _state.SelectedBattleIndex = index;
        _state.PreviewSelectionMode = PreviewSelectionMode.Battle;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public bool CanReplaySelectedBattle(
        HistoryBattleRecord? activeSelectedBattle,
        out string reason
    )
    {
        return _replayService.CanReplayBattle(activeSelectedBattle, out reason);
    }

    public bool CanRecordSelectedBattle(
        HistoryBattleRecord? activeSelectedBattle,
        out string reason
    )
    {
        return _replayService.CanRecordReplay(activeSelectedBattle, out reason);
    }

    public void PrewarmRecordingAvailability()
    {
        _replayService.PrewarmRecordingAvailability();
    }

    public bool CanDeleteSelectedRun(HistoryRunRecord? selectedRun, out string reason)
    {
        return HistoryPanelDecisions.CanDeleteRun(
            _state.SectionMode,
            selectedRun,
            _runtime.IsInGameRun,
            _runtime.CurrentServerRunId,
            _dataService.IsAvailable,
            out reason
        );
    }

    public async Task TryReplaySelectedBattleAsync(
        HistoryBattleRecord? activeSelectedBattle,
        bool recordVideo
    )
    {
        var battle = activeSelectedBattle;
        if (battle == null)
            return;

        if (_state.ReplayActionInProgress)
        {
            SetStatusMessage(HistoryPanelText.ReplayActionAlreadyRunning());
            _requestUiRefresh();
            return;
        }

        if (!CanReplaySelectedBattle(battle, out var replayUnavailableReason))
        {
            SetStatusMessage(replayUnavailableReason);
            _requestUiRefresh();
            return;
        }

        // Recording must be feasible before a record-and-replay request proceeds; otherwise we
        // surface the reason and refuse rather than silently starting a no-video replay.
        if (recordVideo)
        {
            var canRecord = CanRecordSelectedBattle(battle, out var recordUnavailableReason);
            BppLog.Info(
                "HistoryPanel",
                $"Record-and-replay requested battle={battle.BattleId} canRecord={canRecord}"
                    + (canRecord ? string.Empty : $" reason={recordUnavailableReason}")
            );
            if (!canRecord)
            {
                SetStatusMessage(recordUnavailableReason);
                _requestUiRefresh();
                return;
            }
        }

        _state.ReplayActionInProgress = true;
        SetStatusMessage(
            battle.Source == HistoryBattleSource.Ghost && !battle.ReplayDownloaded
                ? HistoryPanelText.DownloadingGhostReplay()
                : HistoryPanelText.StartingReplay(),
            StatusSeverity.Pending
        );
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        HistoryPanelReplayAttemptResult replayResult;
        try
        {
            replayResult = await _replayService.ReplayBattleAsync(
                battle,
                recordVideo,
                _session.Token
            );
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.ReplayActionInProgress = false;
            SetStatusMessage(null);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.ReplayActionInProgress = false;
            SetStatusMessage(HistoryPanelText.ReplayFailed(ex.Message), StatusSeverity.Failure);
            BppLog.Error("HistoryPanel", "Failed to replay selected battle", ex);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;

        _state.ReplayActionInProgress = false;
        SetStatusMessage(
            replayResult.StatusMessage,
            replayResult.Succeeded ? StatusSeverity.Success : StatusSeverity.Failure
        );
        if (!replayResult.Succeeded)
        {
            _requestUiRefresh();
            return;
        }

        _requestVisibilityChange(false);
    }

    public void TryDeleteSelectedRun(HistoryRunRecord? selectedRun)
    {
        var run = selectedRun;
        if (run == null)
            return;

        if (!CanDeleteSelectedRun(run, out var reason))
        {
            ClearDeleteRunConfirmation();
            SetStatusMessage(reason);
            _requestUiRefresh();
            return;
        }

        var now = Time.unscaledTime;
        if (!IsDeleteRunConfirmationActive(run.RunId, now))
        {
            _state.DeleteRunConfirmation = new DeleteConfirmation(run.RunId, now + 5f);
            SetStatusMessage(
                HistoryPanelText.DeleteRunConfirm(HistoryPanelFormatter.ShortenRunId(run.RunId)),
                isDeleteConfirmation: true
            );
            _requestUiRefresh();
            return;
        }

        ClearDeleteRunConfirmation();

        if (!_dataService.TryDeleteRun(run.RunId, out var battleIds, out var error))
        {
            SetStatusMessage(
                HistoryPanelText.RunDeleteFailed(error?.Message ?? HistoryPanelText.Unknown()),
                StatusSeverity.Failure
            );
            BppLog.Error(
                "HistoryPanel",
                $"Failed to delete run {run.RunId}",
                error ?? new InvalidOperationException("Unknown run delete failure.")
            );
            _requestUiRefresh();
            return;
        }

        _replayService.CleanupReplayPayloads(battleIds);
        var deletedMessage = HistoryPanelText.DeletedRun(
            HistoryPanelFormatter.ShortenRunId(run.RunId),
            battleIds.Count
        );
        RefreshData();
        SetStatusMessage(deletedMessage, StatusSeverity.Success);
        _requestUiRefresh();
    }

    public HistoryPanelDatabaseChip ResolveDatabaseChip()
    {
        return HistoryPanelDecisions.ResolveDatabaseChip(
            _dataService.IsAvailable,
            _dataService.DatabaseExists
        );
    }

    public string GetReplayActionLabel(HistoryBattleRecord? battle)
    {
        return _replayService.GetReplayActionLabel(battle);
    }

    public async Task TryCheckServerHealthAsync()
    {
        if (_state.ServerHealthProbeInProgress)
        {
            SetStatusMessage(HistoryPanelText.ServerHealthAlreadyRunning());
            _requestUiRefresh();
            return;
        }

        if (_serverHealthProbe == null)
        {
            var unavailable = HistoryPanelServerHealthFormatter.Unavailable();
            SetStatusMessage(unavailable.StatusMessage);
            _requestUiRefresh();
            return;
        }

        _state.ServerHealthProbeInProgress = true;
        var checking = HistoryPanelServerHealthFormatter.Checking();
        SetStatusMessage(checking.StatusMessage, StatusSeverity.Pending);
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        ModApiHealthProbeResult result;
        try
        {
            result = await _serverHealthProbe.ProbeAsync(_session.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.ServerHealthProbeInProgress = false;
            SetStatusMessage(null);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.ServerHealthProbeInProgress = false;
            SetStatusMessage(
                HistoryPanelText.ServerHealthFailed(0, ex.Message),
                StatusSeverity.Failure
            );
            BppLog.Error("HistoryPanel", "Failed to check server health", ex);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;

        _state.ServerHealthProbeInProgress = false;
        var display = HistoryPanelServerHealthFormatter.FromProbeResult(result);
        SetStatusMessage(
            display.StatusMessage,
            result.Succeeded ? StatusSeverity.Success : StatusSeverity.Failure
        );
        if (result.Succeeded)
        {
            BppLog.Info(
                "HistoryPanel",
                $"Server health check succeeded rttMs={result.RoundTripMilliseconds}"
            );
        }
        else
        {
            BppLog.Warn(
                "HistoryPanel",
                $"Server health check failed rttMs={result.RoundTripMilliseconds} error={result.Error}"
            );
        }
        _requestUiRefresh();
    }

    public async Task TryRedeemBazaarDbAccountAsync(string? code)
    {
        if (_state.AccountLinkInProgress)
        {
            SetAccountLinkBanner(
                HistoryPanelText.AccountLink.AlreadyRunning(),
                StatusSeverity.Neutral
            );
            _requestUiRefresh();
            return;
        }

        var accountId = RefreshAccountLinkIdentityFromGame(clearBanner: false);
        var trimmedCode = code?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.SignedOut(), StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }

        if (string.IsNullOrEmpty(trimmedCode))
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.EmptyCode(), StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }

        if (_linkClient == null)
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }

        _state.AccountLinkInProgress = true;
        SetAccountLinkBanner(HistoryPanelText.AccountLink.Linking(), StatusSeverity.Pending);
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        BazaarDbLinkResult result;
        try
        {
            result = await _linkClient.RedeemAsync(trimmedCode, accountId, _session.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsCurrent(sessionVersion))
                return; // panel closed / re-opened mid-flight: discard silently.

            // Still the active session, so this is the HttpClient self-timeout, not a user cancel
            // (a real session cancel bumps the version above). Surface it as a transport failure
            // instead of silently clearing the banner.
            _state.AccountLinkInProgress = false;
            SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.AccountLinkInProgress = false;
            SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
            BppLog.Error("HistoryPanel", "Failed to redeem BazaarDB link code", ex);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;

        _state.AccountLinkInProgress = false;
        var currentAccountId = NormalizeAccountId(BppClientCacheBridge.TryGetProfileAccountId());
        if (!string.Equals(currentAccountId, accountId, StringComparison.Ordinal))
        {
            RefreshAccountLinkIdentityFromGame();
            _requestUiRefresh();
            return;
        }

        // Only a confirmed 200 link persists the local hint and collapses to the badge. 409 means the
        // game account is already linked to a DIFFERENT BazaarDB user (contract), and every error
        // outcome must leave the form open with a failure banner. See OutcomeConfirmsLink.
        if (OutcomeConfirmsLink(result.Outcome))
        {
            _state.LocalLinkedHint = true;
            _state.AccountLinkExpanded = false;
            _accountLinkStore.SaveHint(accountId, _state.CachedDisplayName);
            BppLog.Info("HistoryPanel", $"BazaarDB link redeemed account={accountId}");
        }

        SetAccountLinkBanner(
            RedeemBannerMessage(result.Outcome, _state.CachedDisplayName),
            RedeemBannerSeverity(result.Outcome)
        );
        _requestUiRefresh();
    }

    // Contract rule, isolated for testability: ONLY a successful 200 redeem confirms the link, so it
    // is the only outcome that may persist the local linked hint. 409/AlreadyLinked (a different
    // BazaarDB user) and every error outcome must return false.
    internal static bool OutcomeConfirmsLink(BazaarDbLinkOutcome outcome) =>
        outcome == BazaarDbLinkOutcome.Linked;

    private static StatusSeverity RedeemBannerSeverity(BazaarDbLinkOutcome outcome) =>
        OutcomeConfirmsLink(outcome) ? StatusSeverity.Success : StatusSeverity.Failure;

    private static string RedeemBannerMessage(BazaarDbLinkOutcome outcome, string? displayName) =>
        outcome switch
        {
            BazaarDbLinkOutcome.Linked => HistoryPanelText.AccountLink.LinkedAs(
                displayName ?? string.Empty
            ),
            BazaarDbLinkOutcome.AlreadyLinked => HistoryPanelText.AccountLink.AlreadyLinked(),
            BazaarDbLinkOutcome.InvalidOrExpired or BazaarDbLinkOutcome.MissingFields =>
                HistoryPanelText.AccountLink.InvalidOrExpired(),
            BazaarDbLinkOutcome.ServerError => HistoryPanelText.AccountLink.ServerBusy(),
            _ => HistoryPanelText.AccountLink.Offline(),
        };

    public void ToggleAccountLinkExpanded()
    {
        var accountId = RefreshAccountLinkIdentityFromGame();
        if (!string.IsNullOrWhiteSpace(accountId))
            _accountLinkStore.Clear(accountId);

        _state.LocalLinkedHint = false;
        _state.AccountLinkExpanded = true;
        SetAccountLinkBanner(null, StatusSeverity.Neutral);
        _requestUiRefresh();
    }

    public async Task TrySyncGhostBattlesAsync()
    {
        if (_state.GhostSyncInProgress)
        {
            SetStatusMessage(HistoryPanelText.GhostSyncAlreadyRunning());
            _requestUiRefresh();
            return;
        }

        if (!_dataService.CanSyncGhostBattles)
        {
            SetStatusMessage(HistoryPanelText.GhostSyncUnavailable());
            _requestUiRefresh();
            return;
        }

        _state.GhostSyncInProgress = true;
        SetStatusMessage(HistoryPanelText.SyncingGhostBattles(), StatusSeverity.Pending);
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        HistoryPanelAttemptResult syncResult;
        try
        {
            syncResult = await _dataService.SyncGhostBattlesAsync(_session.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.GhostSyncInProgress = false;
            SetStatusMessage(null);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.GhostSyncInProgress = false;
            SetStatusMessage(HistoryPanelText.GhostSyncFailed(ex.Message), StatusSeverity.Failure);
            BppLog.Error("HistoryPanel", "Failed to sync ghost battles", ex);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;

        _state.GhostSyncInProgress = false;
        SetStatusMessage(
            syncResult.StatusMessage,
            syncResult.Succeeded ? StatusSeverity.Success : StatusSeverity.Failure
        );
        if (!syncResult.Succeeded)
        {
            if (syncResult.Error != null)
                BppLog.Error("HistoryPanel", "Failed to sync ghost battles", syncResult.Error);
            _requestUiRefresh();
            return;
        }

        if (_state.SectionMode == HistorySectionMode.Ghost)
        {
            RefreshGhostData();
            SetStatusMessage(syncResult.StatusMessage, StatusSeverity.Success);
            _requestUiRefresh();
        }
        else
            _requestUiRefresh();
    }

    public IReadOnlyList<HistoryBattleRecord> GetFilteredGhostBattles()
    {
        if (!_state.FilteredGhostBattlesDirty)
            return _state.FilteredGhostBattles;

        _state.FilteredGhostBattles.Clear();
        foreach (var battle in _state.GhostBattles)
        {
            if (
                HistoryPanelGhostBattleFilter.Matches(
                    _state.GhostBattleFilter,
                    _state.GhostDayMin10,
                    battle
                )
            )
                _state.FilteredGhostBattles.Add(battle);
        }

        _state.FilteredGhostBattlesDirty = false;
        return _state.FilteredGhostBattles;
    }

    public IReadOnlyList<HistoryRunRecord> GetFilteredRuns()
    {
        if (!_state.FilteredRunsDirty)
            return _state.FilteredRuns;

        _state.FilteredRuns.Clear();
        foreach (var run in _state.Runs)
        {
            if (HistoryPanelRunHeroFilter.Matches(_state.SelectedRunHero, run))
                _state.FilteredRuns.Add(run);
        }

        _state.FilteredRunsDirty = false;
        return _state.FilteredRuns;
    }

    public bool IsDeleteRunConfirmationActive(string runId, float now)
    {
        return _state.DeleteRunConfirmation.IsActiveFor(runId, now);
    }

    private void LoadBattlesForSelectedRun()
    {
        _state.Battles.Clear();
        _state.SelectedBattleIndex = 0;

        var run = GetSelectedRun();
        if (
            _dataService.TryLoadBattles(run?.RunId, out var battles, out var error)
            && battles.Count > 0
        )
            _state.Battles.AddRange(battles);

        if (error != null && run != null)
        {
            SetStatusMessage(HistoryPanelText.BattleLoadFailed(error.Message));
            BppLog.Error("HistoryPanel", $"Failed to load battles for run {run.RunId}", error);
        }
    }

    private HistoryRunRecord? GetSelectedRun()
    {
        var filteredRuns = GetFilteredRuns();
        if (filteredRuns.Count == 0)
            return null;

        _state.SelectedRunIndex = ClampIndex(_state.SelectedRunIndex, filteredRuns.Count);
        return _state.GetSelectedRun(filteredRuns);
    }

    private static int ClampIndex(int index, int count)
    {
        if (count <= 0)
            return 0;

        if (index < 0)
            return 0;

        return index >= count ? count - 1 : index;
    }

    private void ClearDeleteRunConfirmation()
    {
        _state.DeleteRunConfirmation = default;
        _state.DeleteRunConfirmationStatusActive = false;
    }

    private void ClearTransientStatus()
    {
        if (
            !_state.ReplayActionInProgress
            && !_state.GhostSyncInProgress
            && !_state.ServerHealthProbeInProgress
        )
            SetStatusMessage(null);
    }

    private string? RefreshAccountLinkIdentityFromGame(bool clearBanner = true)
    {
        var accountId = NormalizeAccountId(BppClientCacheBridge.TryGetProfileAccountId());
        var displayName = NormalizeDisplayName(BppClientCacheBridge.TryGetProfileDisplayUsername());

        _state.CachedAccountId = accountId;
        _state.CachedDisplayName = displayName;
        if (clearBanner)
            SetAccountLinkBanner(null, StatusSeverity.Neutral);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            _state.LocalLinkedHint = false;
            _state.AccountLinkExpanded = true;
            return null;
        }

        if (_accountLinkStore.TryLoadHint(accountId, out var storedDisplayName))
        {
            _state.LocalLinkedHint = true;
            _state.AccountLinkExpanded = false;
            var normalizedStoredDisplayName = NormalizeDisplayName(storedDisplayName);
            if (!string.IsNullOrWhiteSpace(normalizedStoredDisplayName))
                _state.CachedDisplayName = normalizedStoredDisplayName;
            return accountId;
        }

        _state.LocalLinkedHint = false;
        _state.AccountLinkExpanded = true;
        return accountId;
    }

    private void SetAccountLinkBanner(string? message, StatusSeverity severity)
    {
        _state.AccountLinkBannerMessage = message;
        _state.AccountLinkBannerSeverity = string.IsNullOrWhiteSpace(message)
            ? StatusSeverity.Neutral
            : severity;
    }

    private void SetStatusMessage(
        string? statusMessage,
        StatusSeverity severity = StatusSeverity.Neutral,
        bool isDeleteConfirmation = false
    )
    {
        _state.StatusMessage = statusMessage;
        _state.DeleteRunConfirmationStatusActive =
            isDeleteConfirmation && !string.IsNullOrWhiteSpace(statusMessage);
        // Severity travels with the message so the banner colour can't desync from in-flight flags
        // (the source of the phase-1 Pending timing coupling). An empty message clears to Neutral;
        // a delete confirmation always reads as Confirm regardless of the caller's severity.
        _state.StatusSeverity =
            string.IsNullOrWhiteSpace(statusMessage) ? StatusSeverity.Neutral
            : isDeleteConfirmation ? StatusSeverity.Confirm
            : severity;
    }

    private static string? NormalizeAccountId(string? accountId)
    {
        var normalized = accountId?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        var normalized = displayName?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private void InvalidateFilteredGhostBattles()
    {
        _state.FilteredGhostBattlesDirty = true;
    }

    private void InvalidateFilteredRuns()
    {
        _state.FilteredRunsDirty = true;
    }

    // Kept as a thin alias on the coordinator so external test reflection that targets
    // HistoryPanelCoordinator+GhostBattleOutcome / ResolveGhostBattleOutcome continues to compile.
    // The actual matching logic lives in HistoryPanelGhostBattleFilter.
    private static GhostBattleOutcome ResolveGhostBattleOutcome(HistoryBattleRecord battle)
    {
        return HistoryPanelGhostBattleFilter.ResolveOutcomeForCompatibility(battle) switch
        {
            HistoryPanelGhostBattleOutcome.Won => GhostBattleOutcome.Won,
            HistoryPanelGhostBattleOutcome.Lost => GhostBattleOutcome.Lost,
            _ => GhostBattleOutcome.Unknown,
        };
    }

    private enum GhostBattleOutcome
    {
        Unknown,
        Won,
        Lost,
    }
}
