#nullable enable
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Sibling ScreenSpaceOverlay canvas where native CardPreviewBase instances are parented
// above the UITK panel. The UITK panel publishes a pixel-space rect each time its grid
// viewport's geometry changes; this overlay reapplies that rect to its clip RectTransform
// and to the native-renderer camera's pixelRect so the grid scrolls underneath the same
// hole that the UITK viewport opens.
//
// The Canvas remains ScreenSpaceOverlay so existing card pixel coordinates and UI masking
// stay unchanged. Its card children also contain native MeshRenderers for tier frames;
// those do not participate in RectMask2D/Mask, so a camera restricted to the card layer
// supplies the missing rectangular scissor. A GraphicRaycaster is added only when the
// raycaster-hover dispatch path is selected via CollectionGridConstants.UsePolledHover =
// false; under the default (polled hover) the overlay is purely visual and the lower UITK
// panel receives every click / wheel uninterrupted.
internal sealed class CollectionGridOverlay
{
    public const int DefaultLayer = 30;

    private readonly int _layer;
    private readonly int _layerMask;
    private GameObject? _root;
    private GameObject? _cameraObject;
    private Canvas? _canvas;
    private CanvasGroup? _canvasGroup;
    private Camera? _camera;
    private Camera? _baseCamera;
    private IList? _cameraStack;
    private bool _baseLayerWasVisible;
    private bool _baseLayerRemoved;
    private RectTransform? _rootRect;
    private RectTransform? _clipRect;
    private RectTransform? _boardRect;

    private Vector2 _position = Vector2.zero;
    private Vector2 _clipSize = new(1f, 1f);

    public CollectionGridOverlay(int layer = DefaultLayer)
    {
        _layer = layer;
        _layerMask = 1 << layer;
    }

    public RectTransform? BoardRoot => _boardRect;

    public bool EnsureInitialized()
    {
        if (
            _root != null
            && _canvas != null
            && _rootRect != null
            && _clipRect != null
            && _boardRect != null
        )
        {
            EnsureCamera();
            ApplyTransform();
            return true;
        }

        Dispose();

        _root = new GameObject("CollectionPanelOverlayRoot", typeof(RectTransform), typeof(Canvas));
        _root.layer = _layer;
        _root.SetActive(false);

        _canvas = _root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = CollectionGridConstants.OverlaySortingOrder;
        _canvas.pixelPerfect = false;

        _cameraObject = new GameObject("CollectionPanelOverlayCamera", typeof(Camera));
        _cameraObject.layer = _layer;
        _cameraObject.transform.SetParent(_root.transform, worldPositionStays: false);
        _camera = _cameraObject.GetComponent<Camera>();
        _camera.enabled = false;

        // CanvasGroup at the overlay root lets the panel cross-fade the entire card layer
        // in sync with the UITK panel's opacity transition. Starts at 0 because the panel
        // is created hidden; CollectionPanel.SetAlpha pushes the live value each frame.
        _canvasGroup = _root.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;

        // Only attach a GraphicRaycaster when the raycaster-hover path is selected. Under
        // the default (polled hover) the overlay is purely visual: the card prefab's
        // RawImage has raycastTarget=true, so a raycaster here would silently swallow
        // every wheel event landing on a card and freeze ScrollView scrolling.
        if (!CollectionGridConstants.UsePolledHover)
            _root.AddComponent<GraphicRaycaster>();

        _rootRect = _root.GetComponent<RectTransform>();

        var clipObject = new GameObject(
            "CollectionPanelOverlayClip",
            typeof(RectTransform),
            typeof(RectMask2D),
            typeof(Image),
            typeof(Mask)
        );
        clipObject.layer = _layer;
        clipObject.transform.SetParent(_root.transform, worldPositionStays: false);
        _clipRect = clipObject.GetComponent<RectTransform>();

        // RectMask2D clips standard UI graphics through the UI clip rectangle. The invisible
        // Image + Mask pair also writes a stencil rectangle so mask-aware card art and tier
        // gems are clipped at render time without painting a solid cover over the panel
        // backdrop. Native frame Renderers are clipped by _camera.pixelRect instead.
        var maskGraphic = clipObject.GetComponent<Image>();
        maskGraphic.color = Color.white;
        maskGraphic.raycastTarget = false;
        clipObject.GetComponent<Mask>().showMaskGraphic = false;

        var boardObject = new GameObject("CollectionPanelOverlayBoard", typeof(RectTransform));
        boardObject.layer = _layer;
        boardObject.transform.SetParent(_clipRect, worldPositionStays: false);
        _boardRect = boardObject.GetComponent<RectTransform>();

        ApplyTransform();
        return true;
    }

    public void SetVisible(bool visible)
    {
        if (visible)
            EnsureCamera();
        if (_root != null)
            _root.SetActive(visible);
    }

    public void SetAlpha(float alpha)
    {
        if (_canvasGroup != null)
            _canvasGroup.alpha = Mathf.Clamp01(alpha);
        if (_camera != null)
            _camera.enabled = alpha > 0f;
    }

    public void SetPosition(Vector2 position)
    {
        var rounded = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
        if (
            Mathf.Approximately(_position.x, rounded.x)
            && Mathf.Approximately(_position.y, rounded.y)
        )
            return;

        _position = rounded;
        ApplyTransform();
    }

    public void SetClipSize(Vector2 size)
    {
        var rounded = new Vector2(
            Mathf.Max(1f, Mathf.Round(size.x)),
            Mathf.Max(1f, Mathf.Round(size.y))
        );
        if (
            Mathf.Approximately(_clipSize.x, rounded.x)
            && Mathf.Approximately(_clipSize.y, rounded.y)
        )
            return;

        _clipSize = rounded;
        ApplyTransform();
    }

    public void Dispose()
    {
        DetachCamera();
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _canvasGroup = null;
            _rootRect = null;
            _clipRect = null;
            _boardRect = null;
        }
        _cameraObject = null;
        _camera = null;
    }

    private void ApplyTransform()
    {
        if (_rootRect == null || _clipRect == null || _boardRect == null)
            return;

        EnsureCamera();

        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.zero;
        _rootRect.pivot = Vector2.zero;
        _rootRect.anchoredPosition = Vector2.zero;
        _rootRect.sizeDelta = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        _rootRect.localScale = Vector3.one;

        _clipRect.anchorMin = Vector2.zero;
        _clipRect.anchorMax = Vector2.zero;
        _clipRect.pivot = Vector2.zero;
        _clipRect.anchoredPosition = _position;
        _clipRect.sizeDelta = _clipSize;
        _clipRect.localScale = Vector3.one;

        // Board: pinned to clip top-left with pivot at top-left, so anchoredPosition (x, y)
        // for cells uses (right of origin, down from origin). y values are negative.
        _boardRect.anchorMin = new Vector2(0f, 1f);
        _boardRect.anchorMax = new Vector2(0f, 1f);
        _boardRect.pivot = new Vector2(0f, 1f);
        _boardRect.anchoredPosition = Vector2.zero;
        _boardRect.sizeDelta = _clipSize;
        _boardRect.localScale = Vector3.one;

        if (_camera != null)
        {
            _camera.pixelRect = new Rect(_position.x, _position.y, _clipSize.x, _clipSize.y);
        }
    }

    private void EnsureCamera()
    {
        if (_camera == null)
            return;

        var baseCamera = Camera.main;
        if (baseCamera == null)
            return;

        if (_baseCamera != baseCamera)
        {
            DetachCamera();
            _baseCamera = baseCamera;
        }

        _camera.CopyFrom(baseCamera);
        _camera.transform.SetPositionAndRotation(
            baseCamera.transform.position,
            baseCamera.transform.rotation
        );
        _camera.transform.localScale = Vector3.one;
        _camera.cullingMask = _layerMask;
        _camera.clearFlags = CameraClearFlags.Depth;
        _camera.backgroundColor = Color.clear;
        _camera.enabled = true;

        if (!TryAttachAsUrpOverlay(baseCamera, _camera))
        {
            _camera.depth = baseCamera.depth + 1f;
        }

        if (!_baseLayerRemoved)
        {
            _baseLayerWasVisible = (baseCamera.cullingMask & _layerMask) != 0;
            baseCamera.cullingMask &= ~_layerMask;
            _baseLayerRemoved = true;
        }
    }

    private bool TryAttachAsUrpOverlay(Camera baseCamera, Camera overlayCamera)
    {
        var additionalCameraDataType = ResolveType(
            "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData"
        );
        if (additionalCameraDataType == null)
            return false;

        var additionalCameraData = baseCamera.GetComponent(additionalCameraDataType);
        if (additionalCameraData == null)
            return false;

        try
        {
            var renderType = additionalCameraDataType.GetProperty("renderType");
            if (renderType == null || !renderType.CanWrite)
                return false;

            renderType.SetValue(
                overlayCamera.gameObject.GetComponent(additionalCameraDataType)
                    ?? overlayCamera.gameObject.AddComponent(additionalCameraDataType),
                Enum.Parse(renderType.PropertyType, "Overlay", ignoreCase: true)
            );

            var cameraStack =
                additionalCameraDataType.GetProperty("cameraStack")?.GetValue(additionalCameraData)
                as IList;
            if (cameraStack == null)
                return false;

            if (!cameraStack.Contains(overlayCamera))
                cameraStack.Add(overlayCamera);
            _cameraStack = cameraStack;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void DetachCamera()
    {
        if (_cameraStack != null && _camera != null && _cameraStack.Contains(_camera))
            _cameraStack.Remove(_camera);

        if (_baseCamera != null && _baseLayerRemoved && _baseLayerWasVisible)
            _baseCamera.cullingMask |= _layerMask;

        _cameraStack = null;
        _baseCamera = null;
        _baseLayerWasVisible = false;
        _baseLayerRemoved = false;
    }

    private static Type? ResolveType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
                return type;
        }

        return null;
    }
}
