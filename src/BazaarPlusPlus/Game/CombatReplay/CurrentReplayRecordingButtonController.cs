#nullable enable
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.Settings;
using TheBazaar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CurrentReplayRecordingButtonController : MonoBehaviour
{
    private const string CloneName = "BPP_CurrentReplayRecordingButton";
    private const float DockButtonGap = BppSettingsDockPlacement.DefaultSiblingGap;
    private Button? _settingsButton;
    private Button? _nativeReplayButton;
    private Button? _button;
    private RectTransform? _cloneRect;
    private GameObject? _clone;
    private Image? _icon;
    private BppDockButtonSpriteId? _lastSpriteId;
    private readonly BppDockButtonScreenLayout _screenLayout = new();
    private readonly CurrentReplayRecordingUiLogState _uiLogState = new();
    private bool _layoutAvailable;
    private CurrentReplayRecordingUiLayoutReasonCode _layoutReasonCode;

    internal static CurrentReplayRecordingButtonController? Attach(Button settingsButton)
    {
        if (settingsButton == null || settingsButton.transform.parent is not RectTransform host)
            return null;

        var existing = settingsButton.GetComponent<CurrentReplayRecordingButtonController>();
        if (existing != null)
        {
            existing._settingsButton = settingsButton;
            existing.SyncLayout();
            existing.Refresh();
            return existing;
        }

        var existingClone = host.Find(CloneName);
        var clone =
            existingClone == null
                ? Instantiate(settingsButton.gameObject, host, worldPositionStays: false)
                : existingClone.gameObject;
        clone.name = CloneName;
        clone.SetActive(false);
        var controller =
            settingsButton.gameObject.AddComponent<CurrentReplayRecordingButtonController>();
        controller.Initialize(settingsButton, clone);
        return controller;
    }

    internal static void BindNativeReplay(Button nativeReplayButton)
    {
        foreach (
            var controller in FindObjectsOfType<CurrentReplayRecordingButtonController>(
                includeInactive: true
            )
        )
        {
            controller._nativeReplayButton = nativeReplayButton;
            controller.Refresh();
        }
    }

    private void Initialize(Button settingsButton, GameObject clone)
    {
        _settingsButton = settingsButton;
        _clone = clone;
        _cloneRect = clone.transform as RectTransform;
        _icon = StripNativeBehavior(clone);
        _button = clone.GetComponent<Button>() ?? clone.AddComponent<Button>();
        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener(OnClicked);
        _button.navigation = new Navigation { mode = Navigation.Mode.None };
        var frame = clone.GetComponent<Image>() ?? clone.AddComponent<Image>();
        frame.raycastTarget = true;
        _button.targetGraphic = frame;

        var layout = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var relay = clone.GetComponent<CurrentReplayRecordingButtonHoverRelay>();
        if (relay == null)
            relay = clone.AddComponent<CurrentReplayRecordingButtonHoverRelay>();
        relay.Bind(this);
        ApplyIcon(CurrentReplayRecordingPhase.Ready);

        CombatReplayRuntime.Instance?.PrepareCurrentReplayRecordingAvailability();
        SyncLayout();
        Refresh();
    }

    private void LateUpdate()
    {
        SyncLayout();
        Refresh();
    }

    private void OnDisable()
    {
        Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
    }

    private static Image? StripNativeBehavior(GameObject clone)
    {
        var nativeIcon = BppDockButtonVisuals.ResolveNativeIconImage(clone);
        foreach (var custom in clone.GetComponentsInChildren<ButtonCustom>(true))
            DestroyImmediate(custom);
        foreach (var native in clone.GetComponentsInChildren<BazaarButtonController>(true))
            DestroyImmediate(native);
        foreach (var owner in clone.GetComponentsInChildren<MonoBehaviour>(true))
            if (owner is IBppNativeSettingsButtonCloneOwner)
                DestroyImmediate(owner);
        foreach (var nested in clone.GetComponentsInChildren<Button>(true))
            if (nested.gameObject != clone)
                DestroyImmediate(nested);

        return nativeIcon;
    }

    private void SyncLayout()
    {
        if (_settingsButton == null || _cloneRect == null)
        {
            _layoutAvailable = false;
            return;
        }

        var anchorButton = ResolveDockAnchorButton(_settingsButton);
        _layoutAvailable = _screenLayout.TryResolveAndApplyCollection(
            anchorButton,
            _cloneRect,
            DockButtonGap,
            out var blockerName
        );
        _layoutReasonCode = CurrentReplayRecordingUiLogState.ResolveLayoutReason(
            _layoutAvailable,
            blockerName
        );
    }

    private static Button ResolveDockAnchorButton(Button settingsButton)
    {
        var collectionRect = settingsButton
            .GetComponent<CollectionPanelDockButtonController>()
            ?.DockButtonRect;
        if (
            collectionRect != null
            && collectionRect.gameObject.activeInHierarchy
            && collectionRect.GetComponent<Button>() is { } collectionButton
        )
        {
            return collectionButton;
        }

        return settingsButton;
    }

    private void Refresh()
    {
        if (_clone == null || _button == null)
            return;
        var runtime = CombatReplayRuntime.Instance;
        var snapshot = runtime?.GetCurrentReplayRecordingSnapshot() ?? default;
        var visible = snapshot.Visible && _layoutAvailable;
        if (_clone.activeSelf != visible)
            _clone.SetActive(visible);
        _uiLogState.Observe(
            snapshot,
            _layoutAvailable,
            _layoutReasonCode,
            _clone.activeSelf,
            _nativeReplayButton != null,
            _icon != null && _icon.sprite != null
        );
        if (!visible)
            return;

        _button.interactable = snapshot.CanStart || snapshot.CanReveal;
        ApplyIcon(snapshot.Phase);
    }

    private void OnClicked()
    {
        var runtime = CombatReplayRuntime.Instance;
        var nativeReplayButton = _nativeReplayButton;
        if (runtime == null || nativeReplayButton == null)
            return;

        var snapshot = runtime.GetCurrentReplayRecordingSnapshot();
        if (snapshot.CanReveal)
            runtime.TryRevealCurrentReplayVideo(out _);
        else if (snapshot.CanStart)
            runtime.TryStartCurrentReplayRecording(nativeReplayButton.onClick.Invoke, out _);
        Refresh();
    }

    internal void ShowTooltip()
    {
        var snapshot = CombatReplayRuntime.Instance?.GetCurrentReplayRecordingSnapshot() ?? default;
        if (!snapshot.Visible || _cloneRect == null)
            return;
        // AuxiliaryTooltipController adds this vector to the target transform's world position.
        // Passing an absolute position here double-counts the button position and sends the
        // tooltip to a screen edge after its bounds clamp.
        var offset = _cloneRect.TransformVector(
            Vector3.up * Mathf.Max(_cloneRect.rect.height, 64f)
        );
        Data.TooltipParentComponent?.ShowAuxiliaryTooltipController(
            _cloneRect,
            offset,
            CurrentReplayRecordingText.Tooltip(snapshot)
        );
    }

    internal void HideTooltip()
    {
        Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
    }

    private void ApplyIcon(CurrentReplayRecordingPhase phase)
    {
        if (_icon == null)
            return;

        var spriteId = SpriteId(phase);
        if (_lastSpriteId == spriteId)
            return;

        var sprite = BppDockButtonSpriteProvider.Get(spriteId);
        if (sprite == null)
            return;

        BppDockButtonVisuals.ApplyIcon(_icon, sprite);
        _lastSpriteId = spriteId;
    }

    private static BppDockButtonSpriteId SpriteId(CurrentReplayRecordingPhase phase) =>
        phase switch
        {
            CurrentReplayRecordingPhase.Armed or CurrentReplayRecordingPhase.Recording =>
                BppDockButtonSpriteId.ReplayRecording,
            CurrentReplayRecordingPhase.Succeeded or CurrentReplayRecordingPhase.Degraded =>
                BppDockButtonSpriteId.ReplayView,
            CurrentReplayRecordingPhase.Failed or CurrentReplayRecordingPhase.Unavailable =>
                BppDockButtonSpriteId.ReplayRetry,
            _ => BppDockButtonSpriteId.ReplayExport,
        };
}

internal sealed class CurrentReplayRecordingButtonHoverRelay
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
{
    private CurrentReplayRecordingButtonController? _owner;

    internal void Bind(CurrentReplayRecordingButtonController owner) => _owner = owner;

    public void OnPointerEnter(PointerEventData eventData) => _owner?.ShowTooltip();

    public void OnPointerExit(PointerEventData eventData) => _owner?.HideTooltip();
}
