#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppDockButtonNativeStateVisual
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
{
    private Button? _button;
    private Image? _targetImage;
    private Sprite? _normalSprite;
    private Sprite? _highlightedSprite;
    private Sprite? _pressedSprite;
    private Sprite? _selectedSprite;
    private Sprite? _disabledSprite;
    private bool _hovered;
    private bool _pressed;
    private bool _selected;

    internal void Initialize(
        Button button,
        Image targetImage,
        Sprite? normalSprite,
        Sprite? highlightedSprite,
        Sprite? pressedSprite,
        Sprite? selectedSprite,
        Sprite? disabledSprite
    )
    {
        _button = button;
        _targetImage = targetImage;
        _normalSprite = normalSprite;
        _highlightedSprite = highlightedSprite;
        _pressedSprite = pressedSprite ?? selectedSprite ?? highlightedSprite;
        _selectedSprite = selectedSprite ?? pressedSprite ?? highlightedSprite;
        _disabledSprite = disabledSprite;
        ApplyCurrentState();
    }

    internal void SetSelected(bool selected)
    {
        _selected = selected;
        ApplyCurrentState();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovered = true;
        ApplyCurrentState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;
        _pressed = false;
        ApplyCurrentState();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pressed = true;
        ApplyCurrentState();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _pressed = false;
        ApplyCurrentState();
    }

    private void OnDisable()
    {
        _hovered = false;
        _pressed = false;
        ApplyCurrentState();
    }

    private void ApplyCurrentState()
    {
        if (_targetImage == null)
            return;

        var sprite = ResolveCurrentSprite();
        if (sprite != null)
            _targetImage.sprite = sprite;
    }

    private Sprite? ResolveCurrentSprite()
    {
        if (_button != null && !_button.interactable)
            return _disabledSprite ?? _normalSprite;

        if (_pressed)
            return _pressedSprite ?? _highlightedSprite ?? _selectedSprite ?? _normalSprite;

        if (_selected)
            return _selectedSprite ?? _pressedSprite ?? _highlightedSprite ?? _normalSprite;

        if (_hovered)
            return _highlightedSprite ?? _selectedSprite ?? _pressedSprite ?? _normalSprite;

        return _normalSprite;
    }
}
