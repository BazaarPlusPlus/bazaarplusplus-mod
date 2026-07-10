#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed partial class BppSettingsDockController
    : MonoBehaviour,
        IBppNativeSettingsButtonCloneOwner
{
    private const string LogCategory = "BppSettingsDock";
    private const string HeaderObjectName = "BPP_SettingsDockHeader";
    private const float PanelWidth = 456f;
    private const float PanelExpandedScale = 1.5f;
    private const float PanelPadding = 18f;
    private const float PanelTopPadding = 16f;
    private const float PanelBottomPadding = 28f;
    private const float HeaderHeight = 24f;
    private const float HeaderSpacing = 16f;
    private const float RowHeight = 48f;
    private const float RowSpacing = 12f;
    private const float RowInnerPadding = 16f;
    private const float StatusWidth = 80f;
    private const int ScreenResizeSyncFrameCount = 6;
    private const int LayoutImmediateSyncFrameCount = 2;

    private readonly List<DockSettingRowView> _rows = [];

    private Button? _anchorButton;
    private Button? _dockButton;
    private RectTransform? _dockButtonRect;
    private RectTransform? _panelRoot;
    private TextMeshProUGUI? _headerLabel;
    private TMP_FontAsset? _uiFont;
    private Material? _uiFontMaterial;
    private readonly BppScreenResizeSyncTracker _screenResizeSync = new(ScreenResizeSyncFrameCount);
    private readonly BppDockLayoutSyncTracker _layoutSync = new(LayoutImmediateSyncFrameCount);
    private readonly BppDockButtonScreenLayout _screenLayout = new();
    private bool _isExpanded;
    private bool _hasAvailableDockLayout;
    private int _screenshotSuppressionCount;
    private BppSettingsDockPlacement _placement;
    private string? _lastAvoidanceLogKey;
    private static bool _fontResolutionLogged;

    internal static void Attach(Button anchorButton, BppSettingsDockPlacement placement)
    {
        if (anchorButton == null)
            return;

        var existingController = anchorButton.GetComponent<BppSettingsDockController>();
        if (existingController != null && existingController._dockButtonRect != null)
        {
            existingController.SyncDockButtonPlacement();
            existingController.ApplyScreenshotSuppressionVisibility();
            return;
        }

        var dockButton = BppNativeSettingsButtonClone.FindOrCreate(anchorButton, placement);
        if (dockButton == null)
            return;

        var controller =
            existingController ?? anchorButton.gameObject.AddComponent<BppSettingsDockController>();
        controller.Initialize(anchorButton, placement, dockButton);
    }

    internal static IDisposable? BeginScreenshotSuppression()
    {
        var controllers = UnityEngine.Object.FindObjectsOfType<BppSettingsDockController>(
            includeInactive: true
        );
        if (controllers.Length == 0)
            return null;

        var suppressionActions = new Func<IDisposable?>[controllers.Length];
        for (var index = 0; index < controllers.Length; index++)
            suppressionActions[index] = controllers[index].BeginInstanceScreenshotSuppression;

        return UiSuppressionScope.Begin(suppressionActions);
    }

    internal static void RefreshAll()
    {
        foreach (
            var controller in UnityEngine.Object.FindObjectsOfType<BppSettingsDockController>(
                includeInactive: true
            )
        )
            controller.RefreshView();
    }

    private void Initialize(
        Button anchorButton,
        BppSettingsDockPlacement placement,
        RectTransform dockButton
    )
    {
        _anchorButton = anchorButton;
        _placement = placement;
        _dockButtonRect = dockButton;
        _dockButton = dockButton.GetComponent<Button>();
        if (_dockButton == null)
            return;

        ResolveTextStyle();
        _dockButton.onClick.RemoveAllListeners();
        _dockButton.onClick.AddListener(OnDockButtonClicked);

        SyncDockButtonPlacement();
        ApplyScreenshotSuppressionVisibility();

        if (!TryEnsurePanel())
            return;

        RefreshView();
        SetExpanded(false);
    }

    private void OnEnable()
    {
        ApplyScreenshotSuppressionVisibility();
        RefreshView();
        SyncDockButtonPlacement();
    }

    private void OnDisable()
    {
        SetDockLayoutAvailable(false);
        SetExpanded(false);
    }

    private void LateUpdate()
    {
        var shouldSync = _layoutSync.ShouldSync(
            SceneManager.GetActiveScene().name,
            Time.realtimeSinceStartup
        );
        shouldSync |= _screenResizeSync.ShouldSync(Screen.width, Screen.height);
        if (shouldSync)
            SyncDockButtonPlacement();
    }

    private IDisposable BeginInstanceScreenshotSuppression()
    {
        _screenshotSuppressionCount++;
        ApplyScreenshotSuppressionVisibility();
        return new ScreenshotSuppressionLease(this);
    }

    private void EndInstanceScreenshotSuppression()
    {
        if (_screenshotSuppressionCount > 0)
            _screenshotSuppressionCount--;

        ApplyScreenshotSuppressionVisibility();
    }

    private void ApplyScreenshotSuppressionVisibility()
    {
        var shouldBeVisible = _screenshotSuppressionCount == 0 && _hasAvailableDockLayout;
        if (_dockButtonRect != null && _dockButtonRect.gameObject.activeSelf != shouldBeVisible)
            _dockButtonRect.gameObject.SetActive(shouldBeVisible);
    }

    private bool TryEnsurePanel()
    {
        if (_dockButtonRect == null)
            return false;

        var existingPanel = _dockButtonRect.Find(_placement.PanelObjectName) as RectTransform;
        if (existingPanel != null)
        {
            _panelRoot = existingPanel;
            ConfigurePanelRect(existingPanel, _placement);
            ConfigurePanelVisual(existingPanel.gameObject);
            _headerLabel = existingPanel.Find(HeaderObjectName)?.GetComponent<TextMeshProUGUI>();
            if (_headerLabel == null)
            {
                BppLog.Warn(LogCategory, "BPP settings panel header was missing.");
                return false;
            }

            EnsureRows();
            ConfigureHeaderRect(_headerLabel.rectTransform);
            RefreshRowLayouts();
            return true;
        }

        var panelObject = new GameObject(
            _placement.PanelObjectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline)
        );
        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(_dockButtonRect, worldPositionStays: false);
        ConfigurePanelRect(panelRect, _placement);
        ConfigurePanelVisual(panelObject);

        _headerLabel = CreateText(
            HeaderObjectName,
            panelRect,
            21f,
            TextAlignmentOptions.Left,
            new Color(0.97f, 0.83f, 0.49f, 1f)
        );
        if (_headerLabel == null)
        {
            BppLog.Warn(LogCategory, "Failed to create BPP settings dock header.");
            return false;
        }

        _panelRoot = panelRect;
        EnsureRows();
        ConfigureHeaderRect(_headerLabel.rectTransform);
        RefreshRowLayouts();
        return true;
    }

    private void EnsureRows()
    {
        if (_panelRoot == null)
            return;

        while (_rows.Count < BppSettingsDockCatalog.Definitions.Count)
        {
            var rowIndex = _rows.Count;
            _rows.Add(CreateRow(BppSettingsDockCatalog.Definitions[rowIndex], rowIndex));
        }
    }

    private void RefreshRowLayouts()
    {
        for (var index = 0; index < _rows.Count; index++)
            ConfigureRowRect(_rows[index].RectTransform, index);
    }

    private DockSettingRowView CreateRow(BppSettingsDockDefinition definition, int index)
    {
        if (_panelRoot == null)
            throw new InvalidOperationException("Panel root was unavailable.");

        var rowObject = new GameObject(
            $"BPP_SettingsDockRow_{definition.Key}",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(Outline)
        );
        var rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.SetParent(_panelRoot, worldPositionStays: false);
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        ConfigureRowRect(rowRect, index);

        var background = rowObject.GetComponent<Image>();
        background.raycastTarget = true;

        var outline = rowObject.GetComponent<Outline>();
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        var button = rowObject.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = background;
        button.onClick.AddListener(() => ActivateDefinition(definition));

        var label = CreateText(
            "Label",
            rowRect,
            19f,
            TextAlignmentOptions.Left,
            new Color(0.93f, 0.93f, 0.95f, 1f)
        );
        if (label == null)
            throw new InvalidOperationException(
                $"Failed to create row label for {definition.Key}."
            );

        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.offsetMin = new Vector2(RowInnerPadding, 0f);
        labelRect.offsetMax = new Vector2(-(StatusWidth + RowInnerPadding + 8f), 0f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;

        var status = CreateText("Status", rowRect, 17f, TextAlignmentOptions.Center, Color.white);
        if (status == null)
            throw new InvalidOperationException(
                $"Failed to create row status label for {definition.Key}."
            );

        var statusRect = status.rectTransform;
        statusRect.anchorMin = new Vector2(1f, 0.5f);
        statusRect.anchorMax = new Vector2(1f, 0.5f);
        statusRect.pivot = new Vector2(1f, 0.5f);
        statusRect.sizeDelta = new Vector2(StatusWidth, RowHeight);
        statusRect.anchoredPosition = new Vector2(-RowInnerPadding, 0f);
        status.textWrappingMode = TextWrappingModes.NoWrap;

        return new DockSettingRowView(definition, rowRect, background, outline, label, status);
    }

    private void OnDockButtonClicked()
    {
        SetExpanded(!_isExpanded);
    }

    private void OnRectTransformDimensionsChange()
    {
        SyncDockButtonPlacement();
    }

    private void SetExpanded(bool expanded)
    {
        _isExpanded = expanded;
        if (_dockButton != null)
            _dockButton.GetComponent<BppDockButtonNativeVisualState>()?.ResetToNormal();

        if (_panelRoot != null)
        {
            if (expanded)
                RefreshView();

            _panelRoot.gameObject.SetActive(expanded);
        }
    }

    private void ActivateDefinition(BppSettingsDockDefinition definition)
    {
        definition.Activate();
        if (definition.CollapseAfterActivate)
            SetExpanded(false);

        RefreshAll();
    }

    private void RefreshView()
    {
        if (_headerLabel != null)
        {
            var headerText = ResolveHeader(PlayerPreferences.Data.LanguageCode);
            _headerLabel.text = headerText;
            ApplyTextStyle(_headerLabel, headerText);
        }

        foreach (var row in _rows)
            ApplyRowState(row);
    }

    private void ApplyRowState(DockSettingRowView row)
    {
        var enabled = row.Definition.IsActive();
        row.Label.text = row.Definition.ResolveLabel(PlayerPreferences.Data.LanguageCode);
        row.Status.text = row.Definition.ResolveStatus(PlayerPreferences.Data.LanguageCode);
        ApplyTextStyle(row.Label, row.Label.text);
        ApplyTextStyle(row.Status, row.Status.text);
        row.Background.color = enabled
            ? new Color(0.23f, 0.35f, 0.22f, 0.94f)
            : new Color(0.19f, 0.19f, 0.22f, 0.92f);
        row.Outline.effectColor = enabled
            ? new Color(0.78f, 0.86f, 0.46f, 0.70f)
            : new Color(0f, 0f, 0f, 0.45f);
        row.Status.color = enabled
            ? new Color(0.90f, 0.97f, 0.78f, 1f)
            : new Color(0.75f, 0.78f, 0.82f, 0.98f);
    }

    private void SyncDockButtonPlacement()
    {
        if (_anchorButton == null || _dockButtonRect == null)
            return;

        var placement = _placement;
        if (_panelRoot != null)
            ConfigurePanelRect(_panelRoot, placement);

        var collectionController =
            _anchorButton.GetComponent<CollectionPanelDockButtonController>();
        var collectionRect = collectionController?.DockButtonRect;
        if (collectionRect == null)
            return;

        var plan = _screenLayout.ResolveAndApply(
            _anchorButton,
            collectionRect,
            _dockButtonRect,
            placement.SiblingGap
        );
        SetDockLayoutAvailable(plan.CanApply);
        if (!plan.CanApply)
            SetExpanded(false);

        LogDockButtonLayout(plan);
    }

    private void SetDockLayoutAvailable(bool available)
    {
        _hasAvailableDockLayout = available;
        ApplyScreenshotSuppressionVisibility();
        _anchorButton
            ?.GetComponent<CollectionPanelDockButtonController>()
            ?.SetLayoutAvailable(available);
    }

    private void LogDockButtonLayout(BppDockButtonLayoutPlan plan)
    {
        if (plan.CanApply && !plan.WasAdjusted)
        {
            _lastAvoidanceLogKey = null;
            return;
        }

        var key =
            $"{plan.CanApply}:{plan.SettingsSlot}:{plan.BlockerName}:{Mathf.RoundToInt(plan.SettingsBounds.CenterX)}:{Mathf.RoundToInt(plan.SettingsBounds.CenterY)}";
        if (string.Equals(_lastAvoidanceLogKey, key, StringComparison.Ordinal))
            return;

        _lastAvoidanceLogKey = key;
        if (!plan.CanApply)
        {
            BppLog.Warn(
                LogCategory,
                $"No visible dock slot is available for '{_placement.Key}'; retrying without applying an invalid desired position."
            );
            return;
        }

        BppLog.Info(
            LogCategory,
            $"Dock button '{_placement.Key}' moved above Collection after '{plan.BlockerName ?? "viewport"}' blocked gear-left: "
                + $"screen=({plan.SettingsBounds.CenterX:0.##}, {plan.SettingsBounds.CenterY:0.##})."
        );
    }

    private sealed class DockSettingRowView
    {
        internal DockSettingRowView(
            BppSettingsDockDefinition definition,
            RectTransform rectTransform,
            Image background,
            Outline outline,
            TextMeshProUGUI label,
            TextMeshProUGUI status
        )
        {
            Definition = definition;
            RectTransform = rectTransform;
            Background = background;
            Outline = outline;
            Label = label;
            Status = status;
        }

        internal BppSettingsDockDefinition Definition { get; }

        internal RectTransform RectTransform { get; }

        internal Image Background { get; }

        internal Outline Outline { get; }

        internal TextMeshProUGUI Label { get; }

        internal TextMeshProUGUI Status { get; }
    }

    private sealed class ScreenshotSuppressionLease(BppSettingsDockController controller)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            controller.EndInstanceScreenshotSuppression();
        }
    }
}
