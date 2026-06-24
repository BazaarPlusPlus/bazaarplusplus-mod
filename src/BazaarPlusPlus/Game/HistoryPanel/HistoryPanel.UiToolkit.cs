#nullable enable
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private HistoryPanelUiToolkitView? _uiView;
    private Rect _previewContainerBounds;
    private bool _hasPreviewContainerBounds;
    private bool _previewContainerBoundsChanged;

    private void EnsureUi()
    {
        if (_uiView == null)
        {
            _uiView = new HistoryPanelUiToolkitView(
                transform,
                () => SetHistoryVisible(false),
                () => TryReplaySelectedBattle(false),
                () => TryReplaySelectedBattle(true),
                TryDeleteSelectedRun,
                TryCheckServerHealth,
                SubmitAccountLinkCode,
                ToggleAccountLinkExpanded,
                SelectRun,
                SelectBattle,
                SetSectionMode,
                SetGhostBattleFilter,
                SetRunHero,
                ToggleGhostDayMin10
            );
            _uiView.PreviewContainerBoundsChanged += OnPreviewContainerBoundsChanged;

            // First panel open: warm the FFmpeg locator cache off the UI thread so the record
            // button's per-refresh availability gate never incurs the ~2s liveness probe.
            _coordinator?.PrewarmRecordingAvailability();
        }
        _uiView.EnsureCreated();
    }

    private void OnPreviewContainerBoundsChanged(Rect bounds)
    {
        _previewContainerBoundsChanged =
            !_hasPreviewContainerBounds || !RectApproximately(_previewContainerBounds, bounds);
        _previewContainerBounds = bounds;
        _hasPreviewContainerBounds = true;

        if (_battleBoardPreview == null)
            return;

        if (ApplyPreviewContainerBounds(bounds) && IsVisible)
            RefreshSelectedBattlePreview();
    }

    private void DisposeUi()
    {
        _uiView?.Dispose();
        _uiView = null;
    }

    private void SetUiVisible(bool visible)
    {
        _uiView?.SetVisible(visible);
    }

    private void RefreshUi()
    {
        _uiView?.Refresh(BuildUiModel());
    }

    private void SetPreviewStatus(string? message, bool visible)
    {
        _uiView?.SetPreviewStatus(message, visible);
    }

    private static bool RectApproximately(Rect left, Rect right) =>
        Mathf.Approximately(left.x, right.x)
        && Mathf.Approximately(left.y, right.y)
        && Mathf.Approximately(left.width, right.width)
        && Mathf.Approximately(left.height, right.height);

    private HistoryPanelUiToolkitModel BuildUiModel()
    {
        var canReplaySelectedBattle = CanReplaySelectedBattle(out var replayUnavailableReason);
        var canRecordSelectedBattle = CanRecordSelectedBattle(out _);
        var canDeleteSelectedRun = CanDeleteSelectedRun(out _);
        var filteredRuns = FilteredRuns;
        var visibleRuns = filteredRuns.ToList();
        var visibleBattles =
            _sectionMode == HistorySectionMode.Ghost
                ? FilteredGhostBattles.ToList()
                : _battles.ToList();

        var selectedBattle = ActiveSelectedBattle;
        var hasSelectedBattle = selectedBattle != null;
        var selectedRun = SelectedRun;
        var now = Time.unscaledTime;
        var databaseChip =
            _coordinator?.ResolveDatabaseChip()
            ?? HistoryPanelDecisions.ResolveDatabaseChip(false, false);
        var buttons = HistoryPanelButtonModel.Build(
            _replayActionInProgress,
            canReplaySelectedBattle,
            replayUnavailableReason,
            _coordinator?.GetReplayActionLabel(selectedBattle) ?? HistoryPanelText.Replay(),
            _runtime?.IsInGameRun == true,
            canRecordSelectedBattle,
            _sectionMode == HistorySectionMode.Runs
                && selectedRun != null
                && _coordinator?.IsDeleteRunConfirmationActive(selectedRun.RunId, now) == true,
            canDeleteSelectedRun
        );

        var detailResultText = hasSelectedBattle
            ? HistoryPanelFormatter.FormatBattleResult(selectedBattle!)
            : string.Empty;
        var detailResultSeverity = ResolveBattleResultSeverity(selectedBattle);
        var detailDayText = hasSelectedBattle
            ? HistoryPanelFormatter.FormatDayOnly(selectedBattle!.Day)
            : string.Empty;
        var detailOpponentName = hasSelectedBattle
            ? (selectedBattle!.OpponentName ?? HistoryPanelText.UnknownOpponent())
            : string.Empty;
        var detailMetaText = hasSelectedBattle
            ? HistoryPanelFormatter.FormatTimestamp(selectedBattle!.RecordedAtUtc)
            : string.Empty;
        var detailSnapshotText = hasSelectedBattle
            ? HistoryPanelFormatter.FormatSnapshotSummary(selectedBattle!.SnapshotCounts)
            : string.Empty;
        var detailPlaceholderText = hasSelectedBattle
            ? string.Empty
            : HistoryPanelText.SelectBattleForFooter();

        var ghostOpponentEliminatedNoticeText = HistoryPanelFormatter.IsGhostOpponentEliminated(
            selectedBattle
        )
            ? HistoryPanelText.GhostOpponentEliminatedNotice()
            : string.Empty;
        var serverHealthDisplay = _state.ServerHealthProbeInProgress
            ? HistoryPanelServerHealthFormatter.Checking()
            : HistoryPanelServerHealthFormatter.Idle();
        var isBazaarDbLinked = _state.LocalLinkedHint && !_state.AccountLinkExpanded;
        var hasAccount = !string.IsNullOrWhiteSpace(_state.CachedAccountId);

        var statusSeverity = _state.StatusSeverity;

        return new HistoryPanelUiToolkitModel
        {
            Title = HistoryPanelText.Title(),
            Subtitle = HistoryPanelText.Subtitle(),
            Supporters = _supporters,
            CountChipText =
                _sectionMode == HistorySectionMode.Ghost
                    ? HistoryPanelText.CountGhost(FilteredGhostBattles.Count)
                    : HistoryPanelText.CountRuns(filteredRuns.Count),
            BattleChipText =
                _sectionMode == HistorySectionMode.Ghost
                    ? HistoryPanelText.CountBattles(FilteredGhostBattles.Count)
                    : HistoryPanelText.CountBattles(_battles.Count),
            DatabaseChipText = databaseChip.Text,
            DatabaseChipSeverity = databaseChip.Severity,
            ServerHealthButtonText = serverHealthDisplay.ButtonText,
            ServerHealthButtonEnabled = serverHealthDisplay.ButtonEnabled,
            AccountCardVisible = _dependencies?.IsBazaarDbDataSharingEnabled?.Invoke() ?? false,
            IsBazaarDbLinked = isBazaarDbLinked,
            AccountTitleText = HistoryPanelText.AccountLink.Title(),
            AccountWhyText = HistoryPanelText.AccountLink.Why(),
            AccountHintText = HistoryPanelText.AccountLink.Hint(),
            AccountLinkedBadgeText = HistoryPanelText.AccountLink.Linked(),
            AccountSignedOutText = HistoryPanelText.AccountLink.SignedOut(),
            AccountSignedOutVisible = !hasAccount,
            AccountLinkButtonText = _state.AccountLinkInProgress
                ? HistoryPanelText.AccountLink.Linking()
                : HistoryPanelText.AccountLink.Button(),
            AccountLinkButtonEnabled = !_state.AccountLinkInProgress && hasAccount,
            AccountLinkInputEnabled = !_state.AccountLinkInProgress && hasAccount,
            AccountLinkBannerText = _state.AccountLinkBannerMessage,
            AccountLinkBannerSeverity = _state.AccountLinkBannerSeverity,
            AccountLinkFormVisible = hasAccount && !isBazaarDbLinked,
            AccountRelinkButtonText = HistoryPanelText.AccountLink.Relink(),
            SectionMode = _sectionMode,
            GhostBattleFilter = _ghostBattleFilter,
            SelectedRunHero = _state.SelectedRunHero,
            GhostDayMin10 = _state.GhostDayMin10,
            StatusMessage = _statusMessage,
            StatusSeverity = statusSeverity,
            Runs = visibleRuns,
            VisibleBattles = visibleBattles,
            SelectedRunIndex = _selectedRunIndex,
            SelectedBattleIndex =
                _sectionMode == HistorySectionMode.Ghost
                    ? _selectedGhostBattleIndex
                    : _selectedBattleIndex,
            RunsBattleSubtitle =
                selectedRun == null
                    ? HistoryPanelText.SelectRunSubtitle()
                    : $"{selectedRun.Hero} | {HistoryPanelFormatter.FormatDayOnly(selectedRun.FinalDay)}",
            ReplayButtonText = buttons.ReplayButtonText,
            ReplayButtonEnabled = buttons.ReplayButtonEnabled,
            RecordAndReplayButtonText = buttons.RecordAndReplayButtonText,
            RecordAndReplayButtonEnabled = buttons.RecordAndReplayButtonEnabled,
            DeleteButtonText = buttons.DeleteButtonText,
            DeleteButtonEnabled = buttons.DeleteButtonEnabled,
            HasSelectedBattle = hasSelectedBattle,
            DetailResultText = detailResultText,
            DetailResultSeverity = detailResultSeverity,
            DetailDayText = detailDayText,
            DetailOpponentName = detailOpponentName,
            DetailMetaText = detailMetaText,
            DetailSnapshotText = detailSnapshotText,
            DetailPlaceholderText = detailPlaceholderText,
            GhostOpponentEliminatedNoticeText = ghostOpponentEliminatedNoticeText,
        };
    }

    private static StatusSeverity ResolveBattleResultSeverity(HistoryBattleRecord? battle)
    {
        if (battle == null)
            return StatusSeverity.Neutral;

        // eliminated first (it also counts as a win)
        if (HistoryPanelFormatter.IsGhostOpponentEliminated(battle))
            return StatusSeverity.Confirm; // -> Eliminated accent pill

        if (HistoryPanelFormatter.IsBattleWin(battle))
            return StatusSeverity.Success;

        if (HistoryPanelFormatter.IsBattleLoss(battle))
            return StatusSeverity.Failure;

        return StatusSeverity.Neutral;
    }
}

internal sealed class HistoryPanelUiToolkitModel
{
    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public IReadOnlyList<BPPSupporterSample> Supporters { get; set; } =
        new List<BPPSupporterSample>();

    public string CountChipText { get; set; } = string.Empty;

    public string BattleChipText { get; set; } = string.Empty;

    public string DatabaseChipText { get; set; } = string.Empty;

    public string ServerHealthButtonText { get; set; } = string.Empty;

    public bool ServerHealthButtonEnabled { get; set; }

    public bool AccountCardVisible { get; set; }

    public bool IsBazaarDbLinked { get; set; }

    public string AccountTitleText { get; set; } = string.Empty;

    public string AccountWhyText { get; set; } = string.Empty;

    public string AccountHintText { get; set; } = string.Empty;

    public string AccountLinkedBadgeText { get; set; } = string.Empty;

    public string AccountSignedOutText { get; set; } = string.Empty;

    public bool AccountSignedOutVisible { get; set; }

    public string AccountLinkButtonText { get; set; } = string.Empty;

    public bool AccountLinkButtonEnabled { get; set; }

    public bool AccountLinkInputEnabled { get; set; }

    public string? AccountLinkBannerText { get; set; }

    public StatusSeverity AccountLinkBannerSeverity { get; set; }

    public bool AccountLinkFormVisible { get; set; }

    public string AccountRelinkButtonText { get; set; } = string.Empty;

    public HistorySectionMode SectionMode { get; set; }

    public GhostBattleFilter GhostBattleFilter { get; set; }

    public string? SelectedRunHero { get; set; }

    public bool GhostDayMin10 { get; set; }

    public string? StatusMessage { get; set; }

    public StatusSeverity StatusSeverity { get; set; }

    public StatusSeverity DatabaseChipSeverity { get; set; }

    public List<HistoryRunRecord> Runs { get; set; } = new();

    public List<HistoryBattleRecord> VisibleBattles { get; set; } = new();

    public int SelectedRunIndex { get; set; }

    public int SelectedBattleIndex { get; set; }

    public string RunsBattleSubtitle { get; set; } = string.Empty;

    public string ReplayButtonText { get; set; } = string.Empty;

    public bool ReplayButtonEnabled { get; set; }

    public string RecordAndReplayButtonText { get; set; } = string.Empty;

    public bool RecordAndReplayButtonEnabled { get; set; }

    public string DeleteButtonText { get; set; } = string.Empty;

    public bool DeleteButtonEnabled { get; set; }

    public bool HasSelectedBattle { get; set; }

    public string DetailResultText { get; set; } = string.Empty;

    public StatusSeverity DetailResultSeverity { get; set; }

    public string DetailDayText { get; set; } = string.Empty;

    public string DetailOpponentName { get; set; } = string.Empty;

    public string DetailMetaText { get; set; } = string.Empty;

    public string DetailSnapshotText { get; set; } = string.Empty;

    public string DetailPlaceholderText { get; set; } = string.Empty;

    public string GhostOpponentEliminatedNoticeText { get; set; } = string.Empty;
}
