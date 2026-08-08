#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Collection-only presentation layer for the native preview root. The board's CardController
// depends on world-space sockets, colliders, and board interaction state, so the collection uses
// the same visual idea with screen-space pointer coordinates instead. The whole root stays
// unrotated because the preview's RawImage and dynamically-loaded MeshRenderer frame do not
// share one depth buffer in ScreenSpaceOverlay; rotating the root makes their overlap flicker.
internal sealed class CollectionCardHoverVisual : MonoBehaviour
{
    private RectTransform? _rect;
    private RectTransform? _artworkRect;
    private Vector3 _baseScale = Vector3.one;
    private Quaternion _baseRotation = Quaternion.identity;
    private Vector3 _baseArtworkPosition;
    private Vector3 _baseArtworkScale = Vector3.one;
    private Vector3 _artworkPosition;
    private float _artworkScaleFactor = 1f;
    private float _scaleFactor = 1f;
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
            _baseArtworkScale = _artworkRect.localScale;
            _artworkPosition = _baseArtworkPosition;
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
        var targetArtworkScale = _hovered ? CollectionGridConstants.CardHoverArtworkScale : 1f;
        var targetArtworkPosition = _baseArtworkPosition;
        if (_hovered && _artworkRect != null)
        {
            var offsetX =
                Mathf.Max(1f, _artworkRect.rect.width)
                * CollectionGridConstants.CardHoverArtworkParallax;
            var offsetY =
                Mathf.Max(1f, _artworkRect.rect.height)
                * CollectionGridConstants.CardHoverArtworkParallax;
            targetArtworkPosition += new Vector3(_pointerX * offsetX, -_pointerY * offsetY, 0f);
        }

        _scaleFactor = Mathf.Lerp(_scaleFactor, targetScale, smoothing);
        _artworkScaleFactor = Mathf.Lerp(_artworkScaleFactor, targetArtworkScale, smoothing);
        _artworkPosition = Vector3.Lerp(_artworkPosition, targetArtworkPosition, smoothing);
        ApplyVisualState();
    }

    public void ResetVisual()
    {
        _hovered = false;
        _pointerX = 0f;
        _pointerY = 0f;
        _scaleFactor = 1f;
        _artworkScaleFactor = 1f;
        _artworkPosition = _baseArtworkPosition;
        if (_initialized && TryGetRect())
        {
            _rect!.localScale = _baseScale;
            _rect.localRotation = _baseRotation;
        }
        if (_artworkRect != null)
        {
            _artworkRect.localPosition = _baseArtworkPosition;
            _artworkRect.localScale = _baseArtworkScale;
        }
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
        _rect.localRotation = _baseRotation;
        if (_artworkRect != null)
        {
            _artworkRect.localPosition = _artworkPosition;
            _artworkRect.localScale = _baseArtworkScale * _artworkScaleFactor;
        }
    }
}
