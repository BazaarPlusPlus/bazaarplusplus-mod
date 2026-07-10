#nullable enable
using System;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanelDockButtonController
    : MonoBehaviour,
        IBppNativeSettingsButtonCloneOwner
{
    private const string LogCategory = "CollectionPanelDockButton";

    private Button? _dockButton;
    private RectTransform? _dockButtonRect;
    private bool _hasAvailableDockLayout;
    private int _screenshotSuppressionCount;

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
            existingController.ApplyScreenshotSuppressionVisibility();
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
        _dockButtonRect = dockButton;
        _dockButton = dockButton.GetComponent<Button>();
        if (_dockButton == null)
        {
            BppLog.Warn(LogCategory, $"Clone '{placement.Key}' has no neutral Button.");
            return;
        }

        _dockButton.onClick.RemoveAllListeners();
        _dockButton.onClick.AddListener(OnDockButtonClicked);

        ApplyScreenshotSuppressionVisibility();
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
