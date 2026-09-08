#nullable enable
using System.Globalization;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar
{
    private static readonly Color Ink = new(0.96f, 0.89f, 0.73f);
    private static readonly Color MutedInk = new(0.78f, 0.70f, 0.57f);
    private NativeGameTypography.OwnedTextPreparation? _uiTypography;
    private NativeGameTypography.OwnedTextPreparation? _counterTypography;
    private CombatStatusBarNativeSkin? _nativeSkin;
    private Task<CombatStatusBarNativeSkin?>? _nativeSkinLoad;
    private float _nativeSkinRetryAt;
    private bool _nativeSkinFailureReported;
    private GameObject? _canvasObject;
    private TextMeshProUGUI? _timeValue;
    private TextMeshProUGUI? _speedValue;
    private Button? _pauseButton;
    private CombatPlaybackIcon? _pauseIcon;
    private string? _renderedTimeText;
    private float _renderedSpeed = float.NaN;
    private bool? _renderedPause;
    private bool? _renderedInteractable;

    private void EnsureUi()
    {
        if (_canvasObject != null)
            return;
        if (
            NativeGameTypography.PrepareOwnedText(out _uiTypography)
                != NativeGameTypography.Outcome.Ready
            || _uiTypography == null
            || NativeGameTypography.PrepareOwnedText(
                NativeGameTypography.OwnedTextRole.Heading,
                out _counterTypography
            ) != NativeGameTypography.Outcome.Ready
            || _counterTypography == null
        )
            return;
        if (!EnsureNativeSkin())
            return;

        _canvasObject = new GameObject(
            "CombatStatusBarCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        _canvasObject.transform.SetParent(transform, false);
        var canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = _canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.55f;

        var barRoot = CreateRect("CombatControls", _canvasObject.transform, 0f, 0f, 220f, 46f);
        barRoot.anchorMin = barRoot.anchorMax = new Vector2(0.5f, 0f);
        barRoot.pivot = new Vector2(0.5f, 0f);
        barRoot.anchoredPosition = new Vector2(0f, 8f);
        CreatePlaque(barRoot);
    }

    private void CreatePlaque(RectTransform root)
    {
        _nativeSkin!.ApplyPlaque(root);
        CreateDivider(root, -54f);
        CreateDivider(root, 54f);
        _timeValue = CreateText("Elapsed", root, 0f, 0f, 104f, 36f, 22, counter: true);
        _pauseButton = CreateControlButton("Pause", root, 80f);
        _pauseIcon = CreateRect("PauseIcon", _pauseButton.transform, 0f, 0f, 12f, 14f)
            .gameObject.AddComponent<CombatPlaybackIcon>();
        _pauseIcon.raycastTarget = false;
        _pauseIcon.color = Ink;
        _pauseButton.onClick.AddListener(() => ToggleCombatPause());

        var speedButton = CreateControlButton("CycleSpeed", root, -80f);
        _speedValue = CreateText("Multiplier", speedButton.transform, 0f, 0f, 42f, 28f, 14);
        speedButton.onClick.AddListener(() => CycleCombatSpeed());
        BppLog.InfoEvent(CombatStatusBarLogEvents.NativeSkinReady);
    }

    private bool EnsureNativeSkin()
    {
        if (_nativeSkin?.IsValid == true)
            return true;
        if (_nativeSkinLoad == null)
        {
            if (Time.realtimeSinceStartup < _nativeSkinRetryAt)
                return false;
            if (!CombatStatusBarNativeSkin.TryBeginLoad(out _nativeSkinLoad))
                return false;
        }
        if (_nativeSkinLoad == null || !_nativeSkinLoad.IsCompleted)
            return false;

        var error = _nativeSkinLoad.Exception;
        _nativeSkin =
            _nativeSkinLoad.Status == TaskStatus.RanToCompletion ? _nativeSkinLoad.Result : null;
        _nativeSkinLoad = null;
        if (_nativeSkin?.IsValid == true)
        {
            _nativeSkinFailureReported = false;
            return true;
        }

        _nativeSkinRetryAt = Time.realtimeSinceStartup + 30f;
        if (!_nativeSkinFailureReported)
        {
            _nativeSkinFailureReported = true;
            if (error == null)
                BppLog.WarnEvent(CombatStatusBarLogEvents.NativeSkinUnavailable);
            else
                BppLog.WarnEvent(CombatStatusBarLogEvents.NativeSkinUnavailable, error);
        }
        return false;
    }

    private static void CreateDivider(Transform parent, float x)
    {
        var image = CreateRect("Divider", parent, x, 0f, 0.5f, 20f)
            .gameObject.AddComponent<Image>();
        image.color = new Color(0.69f, 0.52f, 0.255f, 0.32f);
        image.raycastTarget = false;
    }

    private static Button CreateControlButton(string name, Transform parent, float x)
    {
        var rect = CreateRect(name, parent, x, 0f, 44f, 36f);
        var feedback = rect.gameObject.AddComponent<Image>();
        feedback.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = feedback;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = Color.clear,
            highlightedColor = new Color(0.92f, 0.67f, 0.26f, 0.12f),
            pressedColor = new Color(0.92f, 0.67f, 0.26f, 0.24f),
            selectedColor = new Color(0.92f, 0.67f, 0.26f, 0.12f),
            disabledColor = Color.clear,
            colorMultiplier = 1f,
            fadeDuration = 0.08f,
        };
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        return button;
    }

    private void RefreshUi()
    {
        if (_canvasObject == null)
            return;
        var visible = ShouldDraw();
        SetUiVisible(visible);
        if (!visible)
            return;

        var time = GetDisplayedTimeText();
        if (_timeValue != null && time != _renderedTimeText)
        {
            _timeValue.text = time;
            _renderedTimeText = time;
        }
        if (_timeValue != null)
            _timeValue.color = Color.Lerp(MutedInk, Ink, _visualBlend);
        if (_renderedSpeed != CombatSpeedMultiplier)
        {
            _renderedSpeed = CombatSpeedMultiplier;
            if (_speedValue != null)
                _speedValue.text =
                    CombatSpeedMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + "x";
        }
        var interactable = CanToggleCombatPause();
        if (_pauseButton != null && _renderedInteractable != interactable)
        {
            _renderedInteractable = interactable;
            _pauseButton.interactable = interactable;
            _renderedPause = null;
        }
        if (_pauseIcon != null && _renderedPause != IsCombatPaused)
        {
            _renderedPause = IsCombatPaused;
            _pauseIcon.ShowPlay = IsCombatPaused;
            _pauseIcon.SetVerticesDirty();
            _pauseIcon.color = IsCombatPaused ? new Color(1f, 0.78f, 0.35f) : Ink;
            if (!interactable)
                _pauseIcon.color *= new Color(1f, 1f, 1f, 0.4f);
        }
    }

    private void SetUiVisible(bool visible)
    {
        if (_canvasObject != null && _canvasObject.activeSelf != visible)
            _canvasObject.SetActive(visible);
    }

    private void DisposeUi()
    {
        if (_canvasObject != null)
        {
            _canvasObject.SetActive(false);
            Destroy(_canvasObject);
        }
        _canvasObject = null;
        _uiTypography = null;
        _counterTypography = null;
        _timeValue = null;
        _speedValue = null;
        _pauseButton = null;
        _pauseIcon = null;
        _renderedTimeText = null;
        _renderedSpeed = float.NaN;
        _renderedPause = null;
        _renderedInteractable = null;
    }

    private static RectTransform CreateRect(
        string name,
        Transform parent,
        float x,
        float y,
        float width,
        float height
    )
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
        return rect;
    }

    private TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        float x,
        float y,
        float width,
        float height,
        int size,
        bool counter = false
    )
    {
        var text = CreateRect(name, parent, x, y, width, height)
            .gameObject.AddComponent<TextMeshProUGUI>();
        var typography = counter ? _counterTypography : _uiTypography;
        if (typography?.Apply(text) != NativeGameTypography.Outcome.Applied)
            throw new InvalidOperationException("Native game typography became unavailable.");
        text.fontSize = size;
        text.fontStyle = FontStyles.Normal;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = false;
        text.raycastTarget = false;
        text.color = Ink;
        return text;
    }
}
