#nullable enable
using BazaarPlusPlus.GameInterop.Fonts;
using TheBazaar;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CurrentReplayRecordingButtonController : MonoBehaviour
{
    private const string CloneName = "BppCurrentReplayRecordingButton";
    private const string GlyphName = "BppCurrentReplayRecordingGlyph";
    private Button? _nativeReplayButton;
    private Button? _button;
    private RectTransform? _nativeRect;
    private RectTransform? _cloneRect;
    private GameObject? _clone;
    private TextMeshProUGUI? _glyph;
    private string? _lastGlyph;

    internal void Bind(Button nativeReplayButton, Transform container)
    {
        _nativeReplayButton = nativeReplayButton;
        _nativeRect = nativeReplayButton.transform as RectTransform;
        if (_clone == null)
            CreateClone(nativeReplayButton, container);
        CombatReplayRuntime.Instance?.PrepareCurrentReplayRecordingAvailability();
        Refresh();
    }

    private void LateUpdate()
    {
        RefreshLayout();
        Refresh();
    }

    private void OnDisable()
    {
        Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
    }

    private void CreateClone(Button nativeReplayButton, Transform container)
    {
        var existing = container.Find(CloneName);
        _clone =
            existing == null
                ? Instantiate(nativeReplayButton.gameObject, container, worldPositionStays: false)
                : existing.gameObject;
        _clone.name = CloneName;

        foreach (var tooltip in _clone.GetComponentsInChildren<RecapReplayButtonController>(true))
            DestroyImmediate(tooltip);
        foreach (var custom in _clone.GetComponentsInChildren<ButtonCustom>(true))
            DestroyImmediate(custom);
        foreach (var native in _clone.GetComponentsInChildren<BazaarButtonController>(true))
            DestroyImmediate(native);
        foreach (var nested in _clone.GetComponentsInChildren<Button>(true))
        {
            if (nested.gameObject != _clone)
                DestroyImmediate(nested);
        }

        _button = _clone.GetComponent<Button>() ?? _clone.AddComponent<Button>();
        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener(OnClicked);
        _button.navigation = new Navigation { mode = Navigation.Mode.None };

        _cloneRect = _clone.transform as RectTransform;
        var layout = _clone.GetComponent<LayoutElement>() ?? _clone.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var relay = _clone.GetComponent<CurrentReplayRecordingButtonHoverRelay>();
        if (relay == null)
            relay = _clone.AddComponent<CurrentReplayRecordingButtonHoverRelay>();
        relay.Bind(this);
        CreateGlyph(nativeReplayButton);
    }

    private void CreateGlyph(Button nativeReplayButton)
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
            _glyph.font = nativeReplayButton.GetComponentInChildren<TextMeshProUGUI>(true)?.font;
        }
        _glyph.fontSize = 34f;
        _glyph.fontStyle = FontStyles.Bold;
        _glyph.alignment = TextAlignmentOptions.Center;
        _glyph.color = Color.white;
        _glyph.raycastTarget = false;
        _glyph.textWrappingMode = TextWrappingModes.NoWrap;
        _glyph.overflowMode = TextOverflowModes.Overflow;
    }

    private void RefreshLayout()
    {
        if (_nativeRect == null || _cloneRect == null)
            return;

        var size = _nativeRect.rect.size;
        if (size.x > 0.01f && size.y > 0.01f)
            _cloneRect.sizeDelta = size;
        _cloneRect.anchorMin = _nativeRect.anchorMin;
        _cloneRect.anchorMax = _nativeRect.anchorMax;
        _cloneRect.pivot = _nativeRect.pivot;
        _cloneRect.localRotation = Quaternion.identity;
        _cloneRect.anchoredPosition =
            _nativeRect.anchoredPosition + Vector2.up * (Mathf.Max(size.y, 64f) + 12f);
        _cloneRect.localScale =
            _nativeRect.localScale.sqrMagnitude > 0.001f ? _nativeRect.localScale : Vector3.one;
    }

    private void Refresh()
    {
        if (_clone == null || _button == null)
            return;
        var runtime = CombatReplayRuntime.Instance;
        var snapshot = runtime?.GetCurrentReplayRecordingSnapshot() ?? default;
        if (_clone.activeSelf != snapshot.Visible)
            _clone.SetActive(snapshot.Visible);
        if (!snapshot.Visible)
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
