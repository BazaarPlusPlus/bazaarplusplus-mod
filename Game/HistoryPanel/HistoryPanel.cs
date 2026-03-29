#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Coroutine = UnityEngine.Coroutine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel : MonoBehaviour
{
    private enum PreviewSelectionMode
    {
        Run,
        Battle,
    }

    private enum HistorySectionMode
    {
        Runs,
        Ghost,
    }

    private enum GhostBattleFilter
    {
        All,
        IWon,
        ILost,
    }

    internal static HistoryPanel? Instance { get; private set; }

    private readonly List<HistoryRunRecord> _runs = new List<HistoryRunRecord>();
    private readonly List<HistoryBattleRecord> _battles = new List<HistoryBattleRecord>();
    private readonly List<HistoryBattleRecord> _ghostBattles = new List<HistoryBattleRecord>();
    private readonly List<HistoryBattleRecord> _filteredGhostBattles =
        new List<HistoryBattleRecord>();
    private int _selectedRunIndex;
    private int _selectedBattleIndex;
    private int _selectedGhostBattleIndex;
    private GhostBattleFilter _ghostBattleFilter = GhostBattleFilter.All;
    private HistoryPanelDataService _dataService = null!;
    private HistoryPanelReplayService _replayService = null!;
    private GhostBattleSyncService? _ghostSyncService;
    private HistoryPanelPreviewRenderer? _previewRenderer;
    private IHistoryPanelRuntime? _runtime;
    private Coroutine? _previewCoroutine;
    private string? _statusMessage;
    private float _previewDebugOverlayUntil;
    private string? _deleteRunConfirmationRunId;
    private float _deleteRunConfirmationUntil;
    private PreviewSelectionMode _previewSelectionMode = PreviewSelectionMode.Run;
    private HistorySectionMode _sectionMode = HistorySectionMode.Runs;
    private string _lastSceneToken = string.Empty;
    private bool _filteredGhostBattlesDirty = true;
    private bool _initialized;

    public static bool IsVisible { get; private set; }

    private HistoryRunRecord? SelectedRun =>
        _runs.Count == 0 ? null : _runs[Mathf.Clamp(_selectedRunIndex, 0, _runs.Count - 1)];

    private HistoryBattleRecord? SelectedBattle =>
        _battles.Count == 0
            ? null
            : _battles[Mathf.Clamp(_selectedBattleIndex, 0, _battles.Count - 1)];

    private HistoryBattleRecord? SelectedGhostBattle =>
        FilteredGhostBattles.Count == 0
            ? null
            : FilteredGhostBattles[
                Mathf.Clamp(_selectedGhostBattleIndex, 0, FilteredGhostBattles.Count - 1)
            ];

    private HistoryBattleRecord? ActiveSelectedBattle =>
        _sectionMode == HistorySectionMode.Ghost ? SelectedGhostBattle : SelectedBattle;

    private IReadOnlyList<HistoryBattleRecord> FilteredGhostBattles =>
        GetFilteredGhostBattles();

    private void Awake()
    {
        EnsureInitialized("Awake");
    }

    internal void Configure(IHistoryPanelRuntime runtime)
    {
        EnsureInitialized("Configure");
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        HistoryPanelRepository? repository = null;
        if (!string.IsNullOrWhiteSpace(_runtime.RunLogDatabasePath))
            repository = new HistoryPanelRepository(_runtime.RunLogDatabasePath);

        _ghostSyncService = TryCreateGhostSyncService(repository);
        _dataService = new HistoryPanelDataService(repository, _ghostSyncService);
        _replayService = new HistoryPanelReplayService(
            _runtime.CombatReplayRuntimeAccessor,
            () => _runtime.CombatReplayDirectoryPath,
            repository,
            _ghostSyncService
        );
    }

    private void OnDisable()
    {
        IsVisible = false;
        ClearDeleteRunConfirmation();
        StopPreviewRender();
        _previewRenderer?.Hide();
        SetUiVisible(false);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;

        StopPreviewRender();
        _previewRenderer?.Dispose();
        _ghostSyncService?.Dispose();
        _ghostSyncService = null;
        DisposeUi();
    }

    private void Update()
    {
        DetectSceneChange();

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (!IsVisible)
            return;

        if (_previewDebugText != null)
            _previewDebugText.gameObject.SetActive(Time.unscaledTime < _previewDebugOverlayUntil);

        if (HistoryPanelPreviewSettings.DynamicPreviewEnabled)
            _previewRenderer?.RenderLiveFrame(_previewSurface);

        if (TryHandlePreviewDebugHotkeys(keyboard))
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
            SetHistoryVisible(false);
    }

    private void SetHistoryVisible(bool visible)
    {
        IsVisible = visible;
        if (visible)
            RefreshData();
        else
        {
            ClearDeleteRunConfirmation();
            StopPreviewRender();
            _previewRenderer?.Dispose();
        }

        SetUiVisible(visible);
        RefreshUi();
    }

    internal void ToggleFromHotkey()
    {
        EnsureInitialized("ToggleFromHotkey");

        try
        {
            SetHistoryVisible(!IsVisible);
        }
        catch (Exception ex)
        {
            BppLog.Error("HistoryPanel", "ToggleFromHotkey failed", ex);
        }
    }

    internal static void OpenFromDockEntry()
    {
        if (Instance == null)
        {
            BppLog.Warn("HistoryPanel", "Dock entry requested while HistoryPanel is unavailable.");
            return;
        }

        Instance.OpenFromDockEntryInternal();
    }

    private void RefreshSelectedBattlePreview()
    {
        StopPreviewRender();

        if (!IsVisible)
        {
            _previewRenderer?.Hide();
            return;
        }

        if (_previewRenderer == null || _previewSurface == null || _previewStatusText == null)
            return;

        var previewRequest = BuildPreviewRequest();
        _previewCoroutine = StartCoroutine(
            _previewRenderer.RenderPreview(
                previewRequest.RenderId,
                previewRequest.PreviewData,
                _previewSurface,
                _previewStatusText
            )
        );
    }

    private void StopPreviewRender()
    {
        _previewRenderer?.CancelPending();

        if (_previewCoroutine == null)
            return;

        StopCoroutine(_previewCoroutine);
        _previewCoroutine = null;
    }

    private void EnsureInitialized(string source)
    {
        if (_initialized)
            return;

        _initialized = true;
        Instance = this;
        _lastSceneToken = GetSceneToken(SceneManager.GetActiveScene());
        _previewRenderer ??= new HistoryPanelPreviewRenderer();
        EnsureUi();
        SetUiVisible(false);
    }

    private void DetectSceneChange()
    {
        var currentSceneToken = GetSceneToken(SceneManager.GetActiveScene());
        if (string.Equals(currentSceneToken, _lastSceneToken, StringComparison.Ordinal))
            return;

        _lastSceneToken = currentSceneToken;
        if (IsVisible && _runtime?.IsInGameRun == true)
            SetHistoryVisible(false);

        StopPreviewRender();
        _previewRenderer?.Dispose();
    }

    private void OpenFromDockEntryInternal()
    {
        EnsureInitialized("OpenFromDockEntry");

        if (_runtime?.IsInGameRun == true)
        {
            BppLog.Warn("HistoryPanel", "Ignored dock open request while in game run.");
            return;
        }

        try
        {
            SetHistoryVisible(true);
        }
        catch (Exception ex)
        {
            BppLog.Error("HistoryPanel", "OpenFromDockEntry failed", ex);
        }
    }

    private IReadOnlyList<HistoryBattleRecord> GetFilteredGhostBattles()
    {
        if (!_filteredGhostBattlesDirty)
            return _filteredGhostBattles;

        _filteredGhostBattles.Clear();
        foreach (var battle in _ghostBattles)
        {
            if (MatchesGhostFilter(battle))
                _filteredGhostBattles.Add(battle);
        }

        _filteredGhostBattlesDirty = false;
        return _filteredGhostBattles;
    }

    private void InvalidateFilteredGhostBattles()
    {
        _filteredGhostBattlesDirty = true;
    }

    private static string GetSceneToken(Scene scene)
    {
        return $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";
    }

    private void ToggleDynamicPreviewFromUi()
    {
        var enabled = HistoryPanelPreviewSettings.ToggleDynamicPreviewEnabled();
        _statusMessage = enabled ? "Dynamic preview enabled." : "Dynamic preview disabled.";
        RefreshUi();

        if (!IsVisible)
            return;

        if (enabled)
        {
            _previewRenderer?.RenderLiveFrame(_previewSurface);
            return;
        }

        RefreshSelectedBattlePreview();
    }

    private bool TryHandlePreviewDebugHotkeys(Keyboard keyboard)
    {
        if (_previewRenderer == null)
            return false;

        var ctrlPressed = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        if (!ctrlPressed)
            return false;

        var positionStep =
            keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 1f : 0.25f;
        var fovStep = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 5f : 1f;
        var scaleStep =
            keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 0.1f : 0.025f;
        var altPressed = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;

        var handled = false;
        if (keyboard.leftArrowKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeBoardHorizontalOffset(-positionStep);
        else if (keyboard.rightArrowKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeBoardHorizontalOffset(positionStep);
        else if (keyboard.upArrowKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCameraDepth(-positionStep);
        else if (keyboard.downArrowKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCameraDepth(positionStep);
        else if (keyboard.pageUpKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCameraVerticalCenter(positionStep);
        else if (keyboard.pageDownKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCameraVerticalCenter(-positionStep);
        else if (keyboard.homeKey.wasPressedThisFrame || keyboard.qKey.wasPressedThisFrame)
            handled = altPressed
                ? _previewRenderer.NudgeCardHeightScale(scaleStep)
                : _previewRenderer.NudgeCardWidthScale(scaleStep);
        else if (keyboard.endKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame)
            handled = altPressed
                ? _previewRenderer.NudgeCardHeightScale(-scaleStep)
                : _previewRenderer.NudgeCardWidthScale(-scaleStep);
        else if (keyboard.leftBracketKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCardSpacingX(-scaleStep);
        else if (keyboard.rightBracketKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeCardSpacingX(scaleStep);
        else if (keyboard.equalsKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeFieldOfView(fovStep);
        else if (keyboard.minusKey.wasPressedThisFrame)
            handled = _previewRenderer.NudgeFieldOfView(-fovStep);
        else if (keyboard.backspaceKey.wasPressedThisFrame)
            handled = _previewRenderer.ResetDebugTuning();

        if (!handled)
            return false;

        _statusMessage =
            "Preview tune: "
            + _previewRenderer.GetDebugSummary()
            + " | Ctrl+Left/Right board spacing, Ctrl+[ / ] card spacing, Ctrl+Up/Down zoom, Ctrl+PgUp/PgDn vertical, Ctrl+Q/E or Home/End card width, Ctrl+Alt+Q/E or Home/End card height, Ctrl+-/= FOV, Ctrl+Backspace reset.";
        ShowPreviewDebugOverlay(_previewRenderer.GetDebugSummary());
        RefreshUi();
        RefreshSelectedBattlePreview();
        return true;
    }

    private void ShowPreviewDebugOverlay(string summary)
    {
        if (_previewDebugText == null)
            return;

        _previewDebugText.text = summary;
        _previewDebugText.gameObject.SetActive(true);
        _previewDebugOverlayUntil = Time.unscaledTime + 6f;
    }

    private PreviewRequest BuildPreviewRequest()
    {
        if (_previewSelectionMode == PreviewSelectionMode.Battle && ActiveSelectedBattle != null)
        {
            return new PreviewRequest(
                $"battle:{ActiveSelectedBattle.BattleId}",
                ActiveSelectedBattle.PreviewData.OpponentHandOnly()
            );
        }

        var runPreviewBattle = GetRunPreviewBattle();
        if (runPreviewBattle != null)
        {
            return new PreviewRequest(
                $"run:{SelectedRun?.RunId}:{runPreviewBattle.BattleId}",
                runPreviewBattle.PreviewData.PlayerHandOnly()
            );
        }

        return new PreviewRequest(null, null);
    }

    private HistoryBattleRecord? GetRunPreviewBattle()
    {
        if (_battles.Count == 0)
            return null;

        return _battles
            .OrderByDescending(battle => battle.Day ?? int.MinValue)
            .ThenByDescending(battle => battle.Hour ?? int.MinValue)
            .ThenByDescending(battle => battle.RecordedAtUtc)
            .FirstOrDefault();
    }

    private bool MatchesGhostFilter(HistoryBattleRecord battle)
    {
        if (battle == null)
            return false;

        return _ghostBattleFilter switch
        {
            GhostBattleFilter.IWon => IsGhostOutcomeIWon(battle),
            GhostBattleFilter.ILost => IsGhostOutcomeILost(battle),
            _ => true,
        };
    }

    private static bool IsGhostOutcomeIWon(HistoryBattleRecord battle)
    {
        var result = battle.Result?.Trim();
        if (
            string.Equals(result, "Loss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result, "Lost", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        return string.Equals(
            battle.WinnerCombatantId,
            "Opponent",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsGhostOutcomeILost(HistoryBattleRecord battle)
    {
        var result = battle.Result?.Trim();
        if (
            string.Equals(result, "Win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result, "Won", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        return string.Equals(
            battle.WinnerCombatantId,
            "Player",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private readonly struct PreviewRequest
    {
        public PreviewRequest(string? renderId, HistoryBattlePreviewData? previewData)
        {
            RenderId = renderId;
            PreviewData = previewData;
        }

        public string? RenderId { get; }

        public HistoryBattlePreviewData? PreviewData { get; }
    }
}
