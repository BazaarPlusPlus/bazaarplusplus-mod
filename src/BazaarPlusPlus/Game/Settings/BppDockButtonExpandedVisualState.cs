#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class BppDockButtonExpandedVisualState : MonoBehaviour
{
    private Button? _button;
    private Image? _targetImage;
    private BppDockButtonVisualState _nativeState;
    private bool _isInitialized;
    private bool _isExpanded;

    internal BppDockButtonVisualState? CapturedNativeState => _isInitialized ? _nativeState : null;

    internal void Initialize(Button button, BppDockButtonVisualState nativeState)
    {
        _button = button;
        if (!_isInitialized)
        {
            _nativeState = nativeState;
            _targetImage = nativeState.TargetGraphic as Image ?? button.targetGraphic as Image;
            _isInitialized = true;
        }

        ApplyBaseline();
    }

    internal void SetExpanded(bool isExpanded)
    {
        if (!_isInitialized || _isExpanded == isExpanded)
            return;

        _isExpanded = isExpanded;
        ClearNativeSelection();
        ApplyBaseline();
    }

    private void OnEnable()
    {
        if (_isInitialized)
            ApplyBaseline();
    }

    private void ApplyBaseline()
    {
        if (_button == null)
            return;

        var baselineSprite = _nativeState.ResolveBaselineSprite(_isExpanded);
        if (_targetImage != null)
            _targetImage.sprite = baselineSprite;

        switch (_nativeState.Transition)
        {
            case Selectable.Transition.ColorTint:
                _button.colors = _nativeState.ResolveBaselineColors(_isExpanded);
                break;
            case Selectable.Transition.SpriteSwap:
                break;
            case Selectable.Transition.Animation:
                _button.animationTriggers = _nativeState.ResolveBaselineAnimationTriggers(
                    _isExpanded
                );
                break;
        }
    }

    private void ClearNativeSelection()
    {
        var eventSystem = EventSystem.current;
        if (
            _button != null
            && eventSystem != null
            && eventSystem.currentSelectedGameObject == _button.gameObject
        )
            eventSystem.SetSelectedGameObject(null);
    }
}
