#nullable enable
using System;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanelDockButtonController
    : MonoBehaviour,
        IBppNativeSettingsButtonCloneOwner
{
    private const string LogCategory = "CollectionPanelDockButton";
    private const int ScreenResizeSyncFrameCount = 6;
    private const int LayoutImmediateSyncFrameCount = 2;

    private Button? _anchorButton;
    private Button? _dockButton;
    private RectTransform? _dockButtonRect;
    private readonly BppScreenResizeSyncTracker _screenResizeSync = new(ScreenResizeSyncFrameCount);
    private readonly BppDockLayoutSyncTracker _layoutSync = new(LayoutImmediateSyncFrameCount);
    private readonly BppDockButtonScreenLayout _screenLayout = new();
    private bool _hasAvailableDockLayout;
    private int _screenshotSuppressionCount;
    private string? _lastLayoutLogKey;

    internal RectTransform? DockButtonRect => _dockButtonRect;

    internal void SetLayoutAvailable(bool available)
    {
        _hasAvailableDockLayout = available;
        ApplyScreenshotSuppressionVisibility();
    }

    internal static void Attach(Button anchorButton, BppSettingsDockPlacement placement)
    {
        if (anchorButton == null)
            return;

        var existingController = anchorButton.GetComponent<CollectionPanelDockButtonController>();
        if (existingController != null && existingController._dockButtonRect != null)
        {
            existingController.SyncDockButtonPlacement();
            return;
        }

        var dockButton = BppNativeSettingsButtonClone.FindOrCreate(anchorButton, placement);
        if (dockButton == null)
            return;

        var controller =
            existingController
            ?? anchorButton.gameObject.AddComponent<CollectionPanelDockButtonController>();
        controller.Initialize(anchorButton, placement, dockButton);
    }

    internal static IDisposable? BeginScreenshotSuppression()
    {
        var controllers = FindObjectsOfType<CollectionPanelDockButtonController>(
            includeInactive: true
        );
        if (controllers.Length == 0)
            return null;

        var suppressionActions = new Func<IDisposable?>[controllers.Length];
        for (var index = 0; index < controllers.Length; index++)
            suppressionActions[index] = controllers[index].BeginInstanceScreenshotSuppression;

        return UiSuppressionScope.Begin(suppressionActions);
    }

    private void Initialize(
        Button anchorButton,
        BppSettingsDockPlacement placement,
        RectTransform dockButton
    )
    {
        _anchorButton = anchorButton;
        _dockButtonRect = dockButton;
        _dockButton = dockButton.GetComponent<Button>();
        if (_dockButton == null)
        {
            BppLog.Warn(LogCategory, $"Clone '{placement.Key}' has no neutral Button.");
            return;
        }

        _dockButton.onClick.RemoveAllListeners();
        _dockButton.onClick.AddListener(OnDockButtonClicked);

        SyncDockButtonPlacement();
    }

    private void OnEnable() => SyncDockButtonPlacement();

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

    private void OnRectTransformDimensionsChange() => SyncDockButtonPlacement();

    private void SyncDockButtonPlacement()
    {
        if (_anchorButton == null || _dockButtonRect == null)
            return;

        var available = _screenLayout.TryResolveAndApplyCollection(
            _anchorButton,
            _dockButtonRect,
            BppSettingsDockPlacement.DefaultSiblingGap,
            out var blockerName
        );
        SetLayoutAvailable(available);

        var logKey = $"{available}:{blockerName}";
        if (string.Equals(_lastLayoutLogKey, logKey, StringComparison.Ordinal))
            return;

        _lastLayoutLogKey = logKey;
        if (available)
            BppLog.Debug(LogCategory, "Collection dock layout is available.");
        else
            BppLog.Warn(
                LogCategory,
                $"Collection dock layout is unavailable; blocker='{blockerName ?? "measurement"}'."
            );
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

    private void OnDockButtonClicked()
    {
        CollectionPanel.OpenFromDockButton();
    }

    private sealed class ScreenshotSuppressionLease(CollectionPanelDockButtonController controller)
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
