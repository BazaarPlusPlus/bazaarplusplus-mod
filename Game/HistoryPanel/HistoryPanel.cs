#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Preview;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Coroutine = UnityEngine.Coroutine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel : MonoBehaviour
{
    private const string ToggleHistoryPanelBindingPath = "<Keyboard>/f8";
    private static readonly HashSet<string> UiDiagnosticScenes = new(StringComparer.Ordinal)
    {
        "CollectionUIScene",
        "CollectionWheelScene",
        "ChestSelectScene",
    };

    internal static HistoryPanel? Instance { get; private set; }

    private readonly HistoryPanelState _state = new();
    private HistoryPanelDependencies? _dependencies;
    private HistoryPanelCoordinator? _coordinator;
    private HistoryPanelDataService _dataService = null!;
    private HistoryPanelReplayService _replayService = null!;
    private HistoryPanelPreviewSource? _previewSource;
    private BattleBoardPreview? _battleBoardPreview;
    private IHistoryPanelRuntime? _runtime;
    private Coroutine? _previewCoroutine;
    private string _lastSceneToken = string.Empty;
    private bool _initialized;
    private bool _uiFontPrewarmedForScene;

    public static bool IsVisible { get; private set; }

    private HistoryRunRecord? SelectedRun => _state.GetSelectedRun();

    private HistoryBattleRecord? SelectedBattle => _state.GetSelectedBattle();

    private HistoryBattleRecord? SelectedGhostBattle =>
        _state.GetSelectedGhostBattle(FilteredGhostBattles);

    private HistoryBattleRecord? ActiveSelectedBattle =>
        _sectionMode == HistorySectionMode.Ghost ? SelectedGhostBattle : SelectedBattle;

    private IReadOnlyList<HistoryBattleRecord> FilteredGhostBattles => GetFilteredGhostBattles();

    private List<HistoryRunRecord> _runs => _state.Runs;

    private List<HistoryBattleRecord> _battles => _state.Battles;

    private List<HistoryBattleRecord> _ghostBattles => _state.GhostBattles;

    private List<HistoryBattleRecord> _filteredGhostBattles => _state.FilteredGhostBattles;

    private int _selectedRunIndex
    {
        get => _state.SelectedRunIndex;
        set => _state.SelectedRunIndex = value;
    }

    private int _selectedBattleIndex
    {
        get => _state.SelectedBattleIndex;
        set => _state.SelectedBattleIndex = value;
    }

    private int _selectedGhostBattleIndex
    {
        get => _state.SelectedGhostBattleIndex;
        set => _state.SelectedGhostBattleIndex = value;
    }

    private GhostBattleFilter _ghostBattleFilter
    {
        get => _state.GhostBattleFilter;
        set => _state.GhostBattleFilter = value;
    }

    private string? _statusMessage
    {
        get => _state.StatusMessage;
        set
        {
            _state.StatusMessage = value;
            _state.DeleteRunConfirmationStatusActive = false;
        }
    }

    private PreviewSelectionMode _previewSelectionMode
    {
        get => _state.PreviewSelectionMode;
        set => _state.PreviewSelectionMode = value;
    }

    private HistorySectionMode _sectionMode
    {
        get => _state.SectionMode;
        set => _state.SectionMode = value;
    }

    private bool _replayActionInProgress
    {
        get => _state.ReplayActionInProgress;
        set => _state.ReplayActionInProgress = value;
    }

    private void Awake()
    {
        EnsureInitialized("Awake");
    }

    internal void Configure(HistoryPanelDependencies dependencies)
    {
        EnsureInitialized("Configure");
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _runtime = dependencies.Runtime;
        _dataService = dependencies.DataService;
        _replayService = dependencies.ReplayService;
        _previewSource = new HistoryPanelPreviewSource(_runtime);
        _coordinator = new HistoryPanelCoordinator(
            _state,
            dependencies,
            RefreshUi,
            RefreshSelectedBattlePreview,
            SetHistoryVisible
        );
    }

    private void OnDisable()
    {
        IsVisible = false;
        _coordinator?.OnPanelHidden();
        StopPreviewRender();
        _battleBoardPreview?.Hide();
        SetUiVisible(false);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;

        _coordinator?.Dispose();
        DisposePreviewRenderer();
        _dependencies = null;
        DisposeUi();
    }

    private void Update()
    {
        DetectSceneChange();

        if (IsVisible && TheBazaar.Data.IsInCombat)
        {
            SetHistoryVisible(false);
            return;
        }

        if (IsVisible)
            _coordinator?.Tick(Time.unscaledTime);

        if (BppHotkeyService.WasPressedThisFrame(ToggleHistoryPanelBindingPath))
        {
            ToggleFromHotkey();
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (!IsVisible)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
            SetHistoryVisible(false);
    }

    private void SetHistoryVisible(bool visible)
    {
        if (visible)
            EnsureUi();

        IsVisible = visible;
        if (visible)
            _coordinator?.OnPanelShown();
        else
        {
            _coordinator?.OnPanelHidden();
            DisposePreviewRenderer();
        }

        SetUiVisible(visible);
        RefreshUi();
    }

    internal void ToggleFromHotkey()
    {
        EnsureInitialized("ToggleFromHotkey");

        if (!CanOpenHistoryReview())
            return;

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

    internal static void RefreshLocalization()
    {
        if (Instance == null || !IsVisible)
            return;

        Instance.RefreshLocalizationInternal();
    }

    internal void OpenFromUiEntry()
    {
        OpenFromDockEntryInternal();
    }

    private void OpenFromDockEntryInternal()
    {
        EnsureInitialized("OpenFromDockEntry");

        if (!CanOpenHistoryReview())
        {
            BppLog.Warn(
                "HistoryPanel",
                "Ignored History Review open request because combat is active."
            );
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

    private void RefreshLocalizationInternal()
    {
        RefreshUi();
    }

    private bool CanOpenHistoryReview()
    {
        return HistoryPanelAccessPolicy.CanOpen(TheBazaar.Data.IsInCombat);
    }

    private void RefreshSelectedBattlePreview()
    {
        StopPreviewRender();

        if (!IsVisible)
        {
            _battleBoardPreview?.Hide();
            return;
        }

        EnsurePreviewRenderer();
        if (_battleBoardPreview == null || _previewSource == null)
            return;

        var previewData = _previewSource.Build(
            _previewSelectionMode,
            _sectionMode,
            ActiveSelectedBattle,
            SelectedRun,
            _battles
        );
        _previewCoroutine = StartCoroutine(
            _battleBoardPreview.Render(previewData.Items, previewData.Signature, OnPreviewPhase)
        );
    }

    private void OnPreviewPhase(BattleBoardRenderPhase phase)
    {
        switch (phase)
        {
            case BattleBoardRenderPhase.Empty:
                SetPreviewStatus(HistoryPanelText.NoLocallyRenderableCards(), true);
                break;
            case BattleBoardRenderPhase.InitFailed:
                SetPreviewStatus(HistoryPanelText.PreviewRendererInitFailed(), true);
                break;
            case BattleBoardRenderPhase.Loading:
                SetPreviewStatus(HistoryPanelText.LoadingPreview(), true);
                break;
            case BattleBoardRenderPhase.Done:
                SetPreviewStatus(null, false);
                break;
        }
    }

    private void StopPreviewRender()
    {
        _battleBoardPreview?.CancelPending();

        if (_previewCoroutine == null)
            return;

        StopCoroutine(_previewCoroutine);
        _previewCoroutine = null;
    }

    private void EnsurePreviewRenderer()
    {
        _battleBoardPreview ??= new BattleBoardPreview();
        if (_hasPreviewContainerBounds)
            ApplyPreviewContainerBounds(_previewContainerBounds);
    }

    // Translates a screen-space UI Toolkit container Rect into the three BattleBoardPreview
    // knobs: position (bottom-left of the overlay clip), clip size, and an auto-fit card
    // scale that matches the legacy behaviour of fitting the 2400x600 native board into the
    // available area. Returns true if the card scale changed so the caller knows to re-render.
    //
    // X/Y/W/H/ScaleMul come from PreviewTunerDebug fields so a live IMGUI window (F9) can
    // drive them. X/Y are pixel offsets from the container's bottom-left corner; W/H are
    // pixel deltas applied to the container's width and height. Both are expressed in
    // 1920×1080 reference-resolution pixels and multiplied by PanelScale at runtime so the
    // baked values stay visually consistent across screen resolutions. PanelScale matches
    // the UI Toolkit panel's actual scale (PanelScaleMode.ScaleWithScreenSize with
    // referenceResolution=1920×1080 and default screenMatchMode = width-matched → match=0),
    // so it's just Screen.width / 1920. The board inside the clip is always centered, so
    // moving the clip moves the cards, and resizing the clip also shifts the cards' visual
    // center. Bake the tuned values straight into the field defaults in
    // HistoryPanel.PreviewTunerDebug.cs (they'll keep being multiplied by PanelScale here)
    // and delete the tuner file.
    private bool ApplyPreviewContainerBounds(Rect bounds)
    {
        if (_battleBoardPreview == null)
            return false;

        var panelScale = ComputePreviewPanelScale();
        var position = new Vector2(
            bounds.x + _debugX * panelScale,
            bounds.y + _debugY * panelScale
        );
        var clipSize = new Vector2(
            Mathf.Max(1f, bounds.width + _debugWidthDelta * panelScale),
            Mathf.Max(1f, bounds.height + _debugHeightDelta * panelScale)
        );
        var autoFitScale = Mathf.Min(
            clipSize.x / HistoryPanelPreviewTextureGeometry.NativeBoardWidth,
            clipSize.y / HistoryPanelPreviewTextureGeometry.NativeBoardHeight
        );
        var cardScale = autoFitScale * _debugCardScaleMul;

        _battleBoardPreview.SetPosition(position);
        _battleBoardPreview.SetClipSize(clipSize);
        return _battleBoardPreview.SetCardScale(cardScale);
    }

    // Scale factor that maps 1920×1080 reference-resolution pixels (the space the tuner
    // sliders work in) to actual screen pixels. Matches the UI Toolkit PanelSettings'
    // ScaleWithScreenSize + width-matched semantics — see HistoryPanelUiToolkitView.cs.
    private static float ComputePreviewPanelScale()
    {
        return Mathf.Max(0.01f, Screen.width / 1920f);
    }

    private void DisposePreviewRenderer()
    {
        StopPreviewRender();
        _battleBoardPreview?.Dispose();
        _battleBoardPreview = null;
    }

    private void EnsureInitialized(string source)
    {
        if (_initialized)
            return;

        _initialized = true;
        Instance = this;
        _lastSceneToken = GetSceneToken(SceneManager.GetActiveScene());
        PrewarmUiFontState($"init:{source}");
    }

    private void DetectSceneChange()
    {
        var currentSceneToken = GetSceneToken(SceneManager.GetActiveScene());
        if (string.Equals(currentSceneToken, _lastSceneToken, StringComparison.Ordinal))
            return;

        _lastSceneToken = currentSceneToken;
        _uiFontPrewarmedForScene = false;
        PrewarmUiFontState("scene-change");
        LogEventSystemDiagnostics(SceneManager.GetActiveScene());
        if (IsVisible && TheBazaar.Data.IsInCombat)
            SetHistoryVisible(false);

        DisposePreviewRenderer();
    }

    private IReadOnlyList<HistoryBattleRecord> GetFilteredGhostBattles()
    {
        return _coordinator?.GetFilteredGhostBattles() ?? _filteredGhostBattles;
    }

    private static string GetSceneToken(Scene scene)
    {
        return $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";
    }

    private void PrewarmUiFontState(string reason)
    {
        if (_uiFontPrewarmedForScene)
            return;

        BppLog.Info(
            "HistoryPanel",
            $"[UiToolkit] PrewarmUiFontState noop reason={reason} scene='{_lastSceneToken}'."
        );
        _uiFontPrewarmedForScene = true;
    }

    private static void LogEventSystemDiagnostics(Scene scene)
    {
        if (!UiDiagnosticScenes.Contains(scene.name))
            return;

        try
        {
            var eventSystems = Resources.FindObjectsOfTypeAll<EventSystem>();
            if (eventSystems == null || eventSystems.Length == 0)
            {
                BppLog.Warn(
                    "HistoryPanel",
                    $"[Diag][EventSystem] scene='{GetSceneToken(scene)}' found no EventSystem instances."
                );
                return;
            }

            var summaries = eventSystems.Select(
                (eventSystem, index) => DescribeEventSystem(eventSystem, index)
            );
            var currentSummary = DescribeEventSystem(EventSystem.current, null);
            BppLog.Info(
                "HistoryPanel",
                $"[Diag][EventSystem] scene='{GetSceneToken(scene)}' count={eventSystems.Length} current={currentSummary} entries={string.Join(" || ", summaries)}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("HistoryPanel", "[Diag][EventSystem] Enumeration failed", ex);
        }
    }

    private static string DescribeEventSystem(EventSystem? eventSystem, int? index)
    {
        var prefix = index.HasValue ? $"#{index.Value}:" : string.Empty;
        if (eventSystem == null)
            return $"{prefix}<null>";

        var modules = eventSystem
            .GetComponents<BaseInputModule>()
            .Select(module =>
                $"{module.GetType().Name}(enabled={module.enabled},active={module.isActiveAndEnabled})"
            );

        return $"{prefix}{eventSystem.GetType().Name}(name='{eventSystem.name}',activeSelf={eventSystem.gameObject.activeSelf},activeInHierarchy={eventSystem.gameObject.activeInHierarchy},enabled={eventSystem.enabled},isCurrent={ReferenceEquals(EventSystem.current, eventSystem)},scene='{eventSystem.gameObject.scene.name}',modules=[{string.Join(", ", modules)}])";
    }
}
