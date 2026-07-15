#nullable enable
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.GameInterop.Fonts;
using TheBazaar;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CurrentReplayRecordingButtonController : MonoBehaviour
{
    private const string CloneName = "BPP_CurrentReplayRecordingButton";
    private const string GlyphName = "BppCurrentReplayRecordingGlyph";
    private const float DockButtonGap = BppSettingsDockPlacement.DefaultSiblingGap;
    private Button? _settingsButton;
    private Button? _nativeReplayButton;
    private Button? _button;
    private RectTransform? _cloneRect;
    private GameObject? _clone;
    private TextMeshProUGUI? _glyph;
    private string? _lastGlyph;
    private readonly BppDockButtonScreenLayout _screenLayout = new();
    private bool _layoutAvailable;

    internal static CurrentReplayRecordingButtonController? Attach(Button settingsButton)
    {
        if (settingsButton == null || settingsButton.transform.parent is not RectTransform host)
            return null;

        var existing = host.Find(CloneName)?.GetComponent<CurrentReplayRecordingButtonController>();
        if (existing != null)
        {
            existing._settingsButton = settingsButton;
            existing.SyncLayout();
            existing.Refresh();
            return existing;
        }

        var clone = Instantiate(settingsButton.gameObject, host, worldPositionStays: false);
        clone.name = CloneName;
        clone.SetActive(false);
        var controller = clone.AddComponent<CurrentReplayRecordingButtonController>();
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
        StripNativeBehavior(clone);
        _button = clone.GetComponent<Button>() ?? clone.AddComponent<Button>();
        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener(OnClicked);
        _button.navigation = new Navigation { mode = Navigation.Mode.None };

        var layout = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var relay = clone.GetComponent<CurrentReplayRecordingButtonHoverRelay>();
        if (relay == null)
            relay = clone.AddComponent<CurrentReplayRecordingButtonHoverRelay>();
        relay.Bind(this);
        CreateGlyph(settingsButton);

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

    private static void StripNativeBehavior(GameObject clone)
    {
        var nativeIcon = clone
            .GetComponentInChildren<BazaarButtonController>(includeInactive: true)
            ?.ButtonIcon;
        if (nativeIcon != null)
            nativeIcon.gameObject.SetActive(false);

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
    }

    private void CreateGlyph(Button settingsButton)
    {
        if (_clone == null)
            return;
        var existing = _clone.transform.Find(GlyphName);
        var glyphObject =
            existing == null
                ? new GameObject(GlyphName, typeof(RectTransform), typeof(CanvasRenderer))
                : existing.gameObject;
        glyphObject.transform.SetParent(_clone.transform, worldPositionStays: false);
        var rect = glyphObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        _glyph =
            glyphObject.GetComponent<TextMeshProUGUI>()
            ?? glyphObject.AddComponent<TextMeshProUGUI>();
        if (
            NativeGameTypography.PrepareOwnedText(out var typography)
                == NativeGameTypography.Outcome.Ready
            && typography != null
        )
        {
            typography.Apply(_glyph);
        }
        else
        {
            _glyph.font = settingsButton.GetComponentInChildren<TextMeshProUGUI>(true)?.font;
        }
        _glyph.fontSize = 34f;
        _glyph.fontStyle = FontStyles.Bold;
        _glyph.alignment = TextAlignmentOptions.Center;
        _glyph.color = Color.white;
        _glyph.raycastTarget = false;
        _glyph.textWrappingMode = TextWrappingModes.NoWrap;
        _glyph.overflowMode = TextOverflowModes.Overflow;
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
            out _
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
        if (!visible)
            return;

        _button.interactable = snapshot.CanStart || snapshot.CanReveal;
        var glyph = Glyph(snapshot.Phase);
        if (_glyph != null && !string.Equals(_lastGlyph, glyph, System.StringComparison.Ordinal))
        {
            _glyph.text = glyph;
            _lastGlyph = glyph;
        }
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
        var offset = _cloneRect.position + Vector3.up * Mathf.Max(_cloneRect.rect.height, 64f);
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

    private static string Glyph(CurrentReplayRecordingPhase phase) =>
        phase switch
        {
            CurrentReplayRecordingPhase.Ready => "↗",
            CurrentReplayRecordingPhase.Armed or CurrentReplayRecordingPhase.Recording => "●",
            CurrentReplayRecordingPhase.Succeeded or CurrentReplayRecordingPhase.Degraded => "▣",
            CurrentReplayRecordingPhase.Failed => "↻",
            CurrentReplayRecordingPhase.Unavailable => "×",
            _ => "…",
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
