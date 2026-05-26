#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.ItemBoard;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelPreviewRenderer
{
    private const int DefaultPreviewLayer = 30;
    private const int DefaultTextureWidth = 2400;
    private const int DefaultTextureHeight = 600;
    private const float CanvasPlaneDistance = 100f;

    private readonly int _layer;
    private GameObject? _root;
    private Canvas? _canvas;
    private Camera? _camera;
    private RenderTexture? _texture;
    private RectTransform[]? _sockets;
    private HistoryPanelPreviewCardPool? _pool;
    private readonly List<Component> _active = new();
    private readonly List<Task> _activeSetUpTasks = new();
    private readonly HistoryPanelPreviewGenerationGuard _generation = new();
    private string? _renderedSignature;
    private int _textureWidth = DefaultTextureWidth;
    private int _textureHeight = DefaultTextureHeight;

    public HistoryPanelPreviewRenderer(int layer = DefaultPreviewLayer)
    {
        _layer = layer;
    }

    public Texture? CurrentTexture => _texture;

    public void CancelPending()
    {
        _generation.Bump();
    }

    public bool SetTextureSize(int width, int height)
    {
        var clampedWidth = Mathf.Clamp(width, 256, 4096);
        var clampedHeight = Mathf.Clamp(height, 128, 2048);
        if (clampedWidth == _textureWidth && clampedHeight == _textureHeight)
            return false;

        _textureWidth = clampedWidth;
        _textureHeight = clampedHeight;
        RebuildRenderTexture();
        _renderedSignature = null;
        return true;
    }

    public IEnumerator RenderPreview(
        HistoryBattlePreviewData? previewData,
        Action<string?, bool> setStatus,
        Action? onRendered = null
    )
    {
        var snapshot = _generation.Bump();

        if (previewData == null || !previewData.HasRenderableCards)
        {
            setStatus(HistoryPanelText.NoLocallyRenderableCards(), true);
            Hide();
            onRendered?.Invoke();
            yield break;
        }

        if (!EnsureInitialized())
        {
            setStatus(HistoryPanelText.PreviewRendererInitFailed(), true);
            onRendered?.Invoke();
            yield break;
        }

        if (!string.IsNullOrEmpty(_renderedSignature)
            && string.Equals(_renderedSignature, previewData.Signature, StringComparison.Ordinal))
        {
            setStatus(null, false);
            onRendered?.Invoke();
            yield break;
        }

        setStatus(HistoryPanelText.LoadingPreview(), true);

        _root!.SetActive(true);
        ReturnActiveCardsToPool();
        _activeSetUpTasks.Clear();

        SpawnCards(previewData.Items);

        var aggregate = Task.WhenAll(_activeSetUpTasks);
        while (!aggregate.IsCompleted && _generation.IsCurrent(snapshot))
            yield return null;

        if (!_generation.IsCurrent(snapshot))
            yield break;

        // Settle layout for one frame so Resize/anchored positions are committed before render.
        yield return new WaitForEndOfFrame();
        if (!_generation.IsCurrent(snapshot))
            yield break;

        ShowAllActiveCards();

        if (_camera != null && _texture != null)
        {
            if (!_texture.IsCreated())
                _texture.Create();
            if (_camera.targetTexture != _texture)
                _camera.targetTexture = _texture;
            _camera.Render();
        }

        // Cache the signature only when all SetUp tasks succeeded; faulted frames stay
        // un-cached so the next selection retries instead of locking in a half-rendered RT.
        if (HistoryPanelPreviewSignatureGate.ShouldCache(aggregate))
            _renderedSignature = previewData.Signature;

        setStatus(null, false);
        onRendered?.Invoke();
    }

    public void Hide()
    {
        CancelPending();
        _renderedSignature = null;
        if (_root != null)
            _root.SetActive(false);
    }

    public void Dispose()
    {
        CancelPending();
        _renderedSignature = null;
        _active.Clear();
        _activeSetUpTasks.Clear();

        _pool?.DestroyAll();
        _pool = null;
        _sockets = null;

        if (_texture != null)
        {
            _texture.Release();
            Object.Destroy(_texture);
            _texture = null;
        }

        if (_camera != null)
        {
            Object.Destroy(_camera.gameObject);
            _camera = null;
        }

        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
            _canvas = null;
        }
    }

    private bool EnsureInitialized()
    {
        if (HistoryPanelCardPreviewReflection.SetUpMethod == null)
            return false;

        if (_root != null && _canvas != null && _camera != null && _sockets != null && _pool != null)
            return _pool.TryEnsurePrefabRefs();

        DisposeRuntimeObjects();

        _pool = new HistoryPanelPreviewCardPool(_layer);
        if (!_pool.TryEnsurePrefabRefs())
        {
            DisposeRuntimeObjects();
            return false;
        }

        _root = new GameObject("HistoryPanelPreviewRoot");
        _root.layer = _layer;
        _root.SetActive(false);
        var rootTransform = _root.transform;

        var cameraObject = new GameObject("HistoryPanelPreviewCamera");
        cameraObject.layer = _layer;
        cameraObject.transform.SetParent(rootTransform, worldPositionStays: false);
        _camera = cameraObject.AddComponent<Camera>();
        _camera.enabled = false;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.cullingMask = 1 << _layer;
        _camera.orthographic = true;
        _camera.nearClipPlane = 0.1f;
        _camera.farClipPlane = 200f;
        _camera.allowMSAA = true;
        _camera.allowHDR = false;
        _camera.transform.localPosition = Vector3.zero;
        _camera.transform.localRotation = Quaternion.identity;

        var canvasObject = new GameObject(
            "HistoryPanelPreviewCanvas",
            typeof(RectTransform),
            typeof(Canvas)
        );
        canvasObject.layer = _layer;
        canvasObject.transform.SetParent(rootTransform, worldPositionStays: false);
        _canvas = canvasObject.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.worldCamera = _camera;
        _canvas.planeDistance = CanvasPlaneDistance;
        _canvas.sortingLayerID = 0;
        _canvas.sortingOrder = 0;

        var canvasRect = canvasObject.GetComponent<RectTransform>();
        _sockets = HistoryPanelPreviewLayout.BuildSockets(canvasRect, _layer);

        RebuildRenderTexture();
        return true;
    }

    private void RebuildRenderTexture()
    {
        if (_texture != null)
        {
            _texture.Release();
            Object.Destroy(_texture);
            _texture = null;
        }

        _texture = new RenderTexture(_textureWidth, _textureHeight, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 2,
            useMipMap = false,
            autoGenerateMips = false,
            name = "HistoryPanelPreviewRT",
        };
        _texture.Create();

        if (_camera != null)
        {
            _camera.targetTexture = _texture;
            // ortho size = half-height in world units = (texture-pixels / 2) / referencePixelsPerUnit.
            // Canvas defaults referencePixelsPerUnit = 100, so 1 world unit = 100 pixels at planeDistance.
            _camera.orthographicSize = _textureHeight * 0.5f / 100f;
            _camera.aspect = (float)_textureWidth / Mathf.Max(1, _textureHeight);
        }
    }

    private void SpawnCards(IReadOnlyList<ItemBoardItemSpec> items)
    {
        if (_pool == null || _sockets == null)
            return;

        var staticData = BppStaticDataAccess.TryGet();
        if (staticData == null)
            return;

        var i = 0;
        foreach (var spec in items)
        {
            if (spec == null || spec.TemplateId == Guid.Empty)
                continue;

            var template = HistoryPanelPreviewTemplateLookup.GetCardTemplate(staticData, spec.TemplateId);
            if (template == null)
                continue;

            var size = ResolveCardSize(template);
            var socket = ResolveSocket(spec.SocketId, i, size);
            if (socket == null)
                continue;

            var card = _pool.Take(size, socket);
            if (card == null)
                continue;

            var instance = BuildSyntheticInstance(spec, i);
            _activeSetUpTasks.Add(InvokeSetUpSafe(card, template, instance));
            _active.Add(card);
            i++;
        }
    }

    private RectTransform? ResolveSocket(EContainerSocketId? requested, int fallbackIndex, ECardSize size)
    {
        if (_sockets == null || _sockets.Length == 0)
            return null;

        var span = size switch
        {
            ECardSize.Small => 1,
            ECardSize.Medium => 2,
            ECardSize.Large => 3,
            _ => 1,
        };
        var lastValidStart = _sockets.Length - span;
        if (lastValidStart < 0)
            return null;

        int index;
        if (requested.HasValue)
            index = (int)requested.Value;
        else
            index = fallbackIndex;

        index = Mathf.Clamp(index, 0, lastValidStart);
        return _sockets[index];
    }

    private static ECardSize ResolveCardSize(TCardBase template)
    {
        return template.Size switch
        {
            ECardSize.Small => ECardSize.Small,
            ECardSize.Medium => ECardSize.Medium,
            ECardSize.Large => ECardSize.Large,
            _ => ECardSize.Small,
        };
    }

    private static TCardInstanceItem BuildSyntheticInstance(ItemBoardItemSpec spec, int index)
    {
        return new TCardInstanceItem
        {
            TemplateId = spec.TemplateId,
            TemplateVersion = string.Empty,
            InstanceId = $"bpp-historypanel-{index}",
            Tier = spec.Tier,
            SocketId = spec.SocketId ?? (EContainerSocketId)Mathf.Clamp(index, 0, 9),
            EnchantmentType = spec.EnchantmentType,
            Attributes = spec.Attributes != null
                ? new Dictionary<ECardAttributeType, int>(spec.Attributes)
                : new Dictionary<ECardAttributeType, int>(),
        };
    }

    private static async Task InvokeSetUpSafe(
        Component card,
        TCardBase template,
        TCardInstanceItem instance
    )
    {
        var method = HistoryPanelCardPreviewReflection.SetUpMethod;
        if (method == null)
            return;

        try
        {
            var raw = method.Invoke(card, new object[] { template, false, instance });
            if (raw is Task task)
                await task;
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Warn(
                "HistoryPanelPreviewRenderer",
                $"CardPreviewBase.SetUp threw for template={template?.Id}: {ex.InnerException?.Message ?? ex.Message}"
            );
            throw;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "HistoryPanelPreviewRenderer",
                $"CardPreviewBase.SetUp invocation failed for template={template?.Id}: {ex.Message}"
            );
            throw;
        }
    }

    private void ShowAllActiveCards()
    {
        var show = HistoryPanelCardPreviewReflection.ShowMethod;
        if (show == null)
            return;

        var args = new object[] { true };
        foreach (var card in _active)
        {
            if (card == null)
                continue;
            try
            {
                show.Invoke(card, args);
            }
            catch (Exception ex)
            {
                BppLog.Warn("HistoryPanelPreviewRenderer", $"CardPreviewBase.Show threw: {ex.Message}");
            }
        }
    }

    private void ReturnActiveCardsToPool()
    {
        if (_pool == null)
        {
            _active.Clear();
            return;
        }

        foreach (var card in _active)
            _pool.Return(card);

        _active.Clear();
    }

    private void DisposeRuntimeObjects()
    {
        _active.Clear();
        _activeSetUpTasks.Clear();

        _pool?.DestroyAll();
        _pool = null;
        _sockets = null;

        if (_texture != null)
        {
            _texture.Release();
            Object.Destroy(_texture);
            _texture = null;
        }

        if (_camera != null)
        {
            Object.Destroy(_camera.gameObject);
            _camera = null;
        }

        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
            _canvas = null;
        }
    }
}

internal static class HistoryPanelPreviewTemplateLookup
{
    private static MethodInfo? _getCardByIdMethod;
    private static Type? _lastStaticDataType;

    public static TCardBase? GetCardTemplate(object? staticData, Guid templateId)
    {
        if (staticData == null || templateId == Guid.Empty)
            return null;

        var staticType = staticData.GetType();
        if (!ReferenceEquals(_lastStaticDataType, staticType))
        {
            _lastStaticDataType = staticType;
            _getCardByIdMethod = staticType.GetMethod(
                "GetCardById",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Guid) },
                null
            );
        }

        return _getCardByIdMethod?.Invoke(staticData, new object[] { templateId }) as TCardBase;
    }
}
