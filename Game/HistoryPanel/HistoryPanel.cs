#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using UnityEngine;
using UnityEngine.InputSystem;
using Coroutine = UnityEngine.Coroutine;

namespace BazaarPlusPlus;

internal sealed partial class HistoryPanel : MonoBehaviour
{
    private enum PreviewSelectionMode
    {
        Run,
        Battle,
    }

    internal static HistoryPanel? Instance { get; private set; }

    private readonly List<HistoryRunRecord> _runs = new List<HistoryRunRecord>();
    private readonly List<HistoryBattleRecord> _battles = new List<HistoryBattleRecord>();
    private int _selectedRunIndex;
    private int _selectedBattleIndex;
    private HistoryPanelRepository? _repository;
    private HistoryPanelPreviewRenderer? _previewRenderer;
    private Coroutine? _previewCoroutine;
    private string? _statusMessage;
    private float _previewDebugOverlayUntil;
    private PreviewSelectionMode _previewSelectionMode = PreviewSelectionMode.Run;
    private int _lastSceneHandle;

    public static bool IsVisible { get; private set; }

    private HistoryRunRecord? SelectedRun =>
        _runs.Count == 0 ? null : _runs[Mathf.Clamp(_selectedRunIndex, 0, _runs.Count - 1)];

    private HistoryBattleRecord? SelectedBattle =>
        _battles.Count == 0
            ? null
            : _battles[Mathf.Clamp(_selectedBattleIndex, 0, _battles.Count - 1)];

    private void Awake()
    {
        Instance = this;
        _lastSceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        var databasePath = BppRuntimeHost.Paths.RunLogDatabasePath;
        if (!string.IsNullOrWhiteSpace(databasePath))
            _repository = new HistoryPanelRepository(databasePath);
        _previewRenderer = new HistoryPanelPreviewRenderer();

        EnsureUi();
        SetUiVisible(false);
        RefreshData();
    }

    private void OnDisable()
    {
        IsVisible = false;
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
        DisposeUi();
    }

    private void Update()
    {
        DetectSceneChange();

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard[KeyBindings.Toggle.HistoryPanel].wasPressedThisFrame)
            SetHistoryVisible(!IsVisible);

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
            StopPreviewRender();
            _previewRenderer?.Dispose();
        }

        SetUiVisible(visible);
        RefreshUi();
    }

    internal void OpenFromUiEntry()
    {
        SetHistoryVisible(true);
    }

    private void RefreshData()
    {
        try
        {
            _runs.Clear();
            _battles.Clear();

            if (_repository == null)
            {
                _statusMessage = "Run log database path is unavailable.";
                RefreshUi();
                return;
            }

            _runs.AddRange(_repository.ListRecentRuns(40));
            _selectedRunIndex = Mathf.Clamp(_selectedRunIndex, 0, Mathf.Max(0, _runs.Count - 1));
            LoadBattlesForSelectedRun();
            _previewSelectionMode = PreviewSelectionMode.Run;
            _statusMessage = _repository.DatabaseExists
                ? $"Loaded {_runs.Count} runs from sqlite."
                : "Database file does not exist yet.";
        }
        catch (Exception ex)
        {
            _runs.Clear();
            _battles.Clear();
            _statusMessage = $"History load failed: {ex.Message}";
            BppLog.Error("HistoryPanel", "Failed to load history page data", ex);
        }

        RefreshUi();
        RefreshSelectedBattlePreview();
    }

    private void SelectRun(int index)
    {
        if (index < 0 || index >= _runs.Count)
            return;

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

        if (_repository == null || SelectedRun == null)
            return;

        try
        {
            _battles.AddRange(_repository.ListBattlesByRun(SelectedRun.RunId));
        }
        catch (Exception ex)
        {
            _statusMessage = $"Battle load failed: {ex.Message}";
            BppLog.Error("HistoryPanel", $"Failed to load battles for run {SelectedRun.RunId}", ex);
        }
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

    private void DetectSceneChange()
    {
        var currentSceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (currentSceneHandle == _lastSceneHandle)
            return;

        _lastSceneHandle = currentSceneHandle;
        StopPreviewRender();
        _previewRenderer?.Dispose();
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
        if (_previewSelectionMode == PreviewSelectionMode.Battle && SelectedBattle != null)
        {
            return new PreviewRequest(
                $"battle:{SelectedBattle.BattleId}",
                SelectedBattle.PreviewData.OpponentHandOnly()
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

    private void TryReplaySelectedBattle()
    {
        var battle = SelectedBattle;
        if (battle == null)
            return;

        var runtime = CombatReplayRuntime.Instance;
        if (runtime == null)
        {
            _statusMessage = "Combat replay runtime is unavailable.";
            RefreshUi();
            return;
        }

        if (!runtime.ReplaySaved(battle.BattleId))
        {
            _statusMessage = $"Replay rejected for battle {battle.BattleId}.";
            RefreshUi();
            return;
        }

        _statusMessage = $"Starting replay for {battle.BattleId}.";
        SetHistoryVisible(false);
    }

    private string GetDatabaseChipText()
    {
        if (_repository == null)
            return "Unavailable";

        return _repository.DatabaseExists ? "Connected" : "Missing";
    }

    private static string ShortenRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return "Unknown Run";

        return runId.Length <= 14 ? runId : runId[..14];
    }

    private static string FormatRunRecord(HistoryRunRecord run)
    {
        return run.Victories.HasValue || run.Losses.HasValue
            ? $"{run.Victories ?? 0}W - {run.Losses ?? 0}L"
            : "-";
    }

    private static string? FormatRunAchievement(HistoryRunRecord run)
    {
        if (!string.Equals(run.RawStatus, "completed", StringComparison.OrdinalIgnoreCase))
            return null;

        var wins = run.Victories ?? 0;
        var losses = run.Losses ?? 0;
        var totalBattles = wins + losses;

        if (wins == 10 && totalBattles == 10)
            return "PERFECT";

        if (wins >= 10 && totalBattles > 10)
            return "GOLD";

        if (wins >= 7)
            return "SILVER";

        if (wins >= 4)
            return "BRONZE";

        return "UNFORTUNE";
    }

    private static string FormatRunStatus(string? rawStatus)
    {
        return rawStatus switch
        {
            "completed" => "Completed",
            "abandoned" => "Abandoned",
            "active" => "Active",
            null => "Unknown",
            _ => char.ToUpperInvariant(rawStatus[0]) + rawStatus[1..],
        };
    }

    private static string FormatBattleResult(HistoryBattleRecord battle)
    {
        if (string.IsNullOrWhiteSpace(battle.Result))
            return "Unknown";

        return string.Equals(battle.Result, "Win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(battle.Result, "Won", StringComparison.OrdinalIgnoreCase)
                ? "Win"
            : string.Equals(battle.Result, "Loss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(battle.Result, "Lost", StringComparison.OrdinalIgnoreCase)
                ? "Loss"
            : battle.Result;
    }

    private static string? FormatOpponentHero(string? rawHero)
    {
        if (string.IsNullOrWhiteSpace(rawHero))
            return null;

        return rawHero;
    }

    private static string FormatDayOnly(int? day)
    {
        return day.HasValue ? $"D{day.Value}" : "D?";
    }

    private static string FormatDayHour(int? day, int? hour)
    {
        var dayText = day.HasValue ? $"D{day.Value}" : "D?";
        var hourText = hour.HasValue ? $"H{hour.Value}" : "H?";
        return $"{dayText} {hourText}";
    }

    private static string? FormatRunDuration(HistoryRunRecord run)
    {
        if (!string.Equals(run.RawStatus, "completed", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!run.EndedAtUtc.HasValue)
            return null;

        var duration = run.EndedAtUtc.Value - run.StartedAtUtc;
        if (duration <= TimeSpan.Zero)
            return null;

        if (duration.TotalHours >= 1d)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";

        if (duration.TotalMinutes >= 1d)
            return $"{Mathf.Max(1, Mathf.RoundToInt((float)duration.TotalMinutes))}m";

        return $"{Mathf.Max(1, duration.Seconds)}s";
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("MM-dd HH:mm");
    }
}
