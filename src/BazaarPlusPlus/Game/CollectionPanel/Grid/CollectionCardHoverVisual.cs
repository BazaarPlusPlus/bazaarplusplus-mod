#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Collection-only presentation layer for the native preview root. The board's CardController
// depends on world-space sockets, colliders, and board interaction state, so the collection uses
// the same visual idea with screen-space pointer coordinates instead. The root is transformed as
// one object so the artwork, frame, and gem decorations move together. The artwork is moved a
// small fixed distance behind the frame to keep the two renderers from becoming coplanar while
// the root is tilted in ScreenSpaceOverlay.
internal sealed class CollectionCardHoverVisual : MonoBehaviour
{
    private RectTransform? _rect;
    private RectTransform? _artworkRect;
    private Vector3 _baseScale = Vector3.one;
    private Quaternion _baseRotation = Quaternion.identity;
    private Vector3 _baseArtworkPosition;
    private float _scaleFactor = 1f;
    private float _tiltX;
    private float _tiltY;
    private float _pointerX;
    private float _pointerY;
    private bool _hovered;
    private bool _initialized;

    public void CaptureBaseTransform()
    {
        if (!TryGetRect())
            return;

        _baseScale = _rect!.localScale;
        _baseRotation = _rect.localRotation;
        _artworkRect = FindArtworkRect();
        if (_artworkRect != null)
        {
            _baseArtworkPosition = _artworkRect.localPosition;
        }
        _initialized = true;
        ApplyVisualState();
    }

    // NativeCardCellFitter rewrites localScale when the viewport or loaded art changes. Capture
    // only that base scale here; the stable rotation must remain independent of hover tilt.
    public void CaptureBaseScale()
    {
        if (!TryGetRect())
            return;

        if (!_initialized)
        {
            CaptureBaseTransform();
            return;
        }

        _baseScale = _rect!.localScale;
        ApplyVisualState();
    }

    public void SetHovered(bool hovered) => _hovered = hovered;

    public void SetPointer(float normalizedX, float normalizedY)
    {
        _pointerX = Mathf.Clamp(normalizedX, -1f, 1f);
        _pointerY = Mathf.Clamp(normalizedY, -1f, 1f);
    }

    public void Tick(float deltaSeconds)
    {
        if (!_initialized || deltaSeconds <= 0f)
            return;

        var smoothing =
            1f - Mathf.Exp(-deltaSeconds / CollectionGridConstants.CardHoverResponseSeconds);
        var targetScale = _hovered ? CollectionGridConstants.CardHoverScale : 1f;
        var targetTiltX = _hovered ? -_pointerY * CollectionGridConstants.CardHoverTiltDegrees : 0f;
        var targetTiltY = _hovered ? _pointerX * CollectionGridConstants.CardHoverTiltDegrees : 0f;

        _scaleFactor = Mathf.Lerp(_scaleFactor, targetScale, smoothing);
        _tiltX = Mathf.Lerp(_tiltX, targetTiltX, smoothing);
        _tiltY = Mathf.Lerp(_tiltY, targetTiltY, smoothing);
        ApplyVisualState();
    }

    public void ResetVisual()
    {
        _hovered = false;
        _pointerX = 0f;
        _pointerY = 0f;
        _scaleFactor = 1f;
        _tiltX = 0f;
        _tiltY = 0f;
        if (_initialized && TryGetRect())
        {
            _rect!.localScale = _baseScale;
            _rect.localRotation = _baseRotation;
        }
        if (_artworkRect != null)
            _artworkRect.localPosition = _baseArtworkPosition;
    }

    private bool TryGetRect()
    {
        _rect ??= GetComponent<RectTransform>();
        return _rect != null;
    }

    private RectTransform? FindArtworkRect()
    {
        foreach (var image in GetComponentsInChildren<RawImage>(includeInactive: true))
        {
            if (image != null)
                return image.rectTransform;
        }

        return null;
    }

    private void ApplyVisualState()
    {
        if (_rect == null)
            return;

        _rect.localScale = _baseScale * _scaleFactor;
        _rect.localRotation = _baseRotation * Quaternion.Euler(_tiltX, _tiltY, 0f);
        if (_artworkRect != null)
        {
            var artworkPosition = _baseArtworkPosition;
            artworkPosition.z += CollectionGridConstants.CardHoverArtworkDepth;
            _artworkRect.localPosition = artworkPosition;
        }
    }
}
