#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HistoryPanel.Preview;

internal enum BattleBoardRenderPhase
{
    Empty,
    InitFailed,
    Loading,
    Done,
}

// Offscreen Camera + fixed-aspect RenderTexture board preview. Spawns CardPreviewBase clones
// (reflected off MonsterBoardTooltip) into 10 sockets under a ScreenSpaceCamera canvas; a
// dedicated orthographic Camera renders them continuously into a fixed 2400x600 RenderTexture.
// The caller shows OutputTexture in a UI Toolkit Image (ScaleToFit), so UI Toolkit owns all
// container scaling / letterboxing / resolution adaptation. The camera clears transparent so
// letterbox margins show the container background.
internal sealed class BattleBoardPreview
{
    private const int DefaultLayer = 30;
    private const int TextureWidth = HistoryPanelPreviewTextureGeometry.NativeBoardWidth; // 2400
    private const int TextureHeight = HistoryPanelPreviewTextureGeometry.NativeBoardHeight; // 600
    private const float CanvasPlaneDistance = 100f;
    private const float CanvasReferencePixelsPerUnit = 100f;

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

    public BattleBoardPreview(int layer = DefaultLayer)
    {
        _layer = layer;
    }

    // The live RenderTexture the caller displays in a UI Toolkit Image. Null until the first
    // successful EnsureInitialized inside Render.
    public Texture? OutputTexture => _texture;

    public void CancelPending()
    {
        _generation.Bump();
    }

    public IEnumerator Render(
        IReadOnlyList<HistoryItemSpec>? cards,
        string? signature = null,
        Action<BattleBoardRenderPhase>? onPhase = null,
        Action? onComplete = null
    )
    {
        var snapshot = _generation.Bump();

        if (cards == null || cards.Count == 0)
        {
            onPhase?.Invoke(BattleBoardRenderPhase.Empty);
            Hide();
            onComplete?.Invoke();
            yield break;
        }

        if (!EnsureInitialized())
        {
            onPhase?.Invoke(BattleBoardRenderPhase.InitFailed);
            onComplete?.Invoke();
            yield break;
        }

        if (
            !string.IsNullOrEmpty(_renderedSignature)
            && !string.IsNullOrEmpty(signature)
            && string.Equals(_renderedSignature, signature, StringComparison.Ordinal)
        )
        {
            onPhase?.Invoke(BattleBoardRenderPhase.Done);
            onComplete?.Invoke();
            yield break;
        }

        onPhase?.Invoke(BattleBoardRenderPhase.Loading);

        _root!.SetActive(true);
        ReturnActiveCardsToPool();
        _activeSetUpTasks.Clear();

        SpawnCards(cards);

        var aggregate = Task.WhenAll(_activeSetUpTasks);
        while (!aggregate.IsCompleted && _generation.IsCurrent(snapshot))
            yield return null;

        if (!_generation.IsCurrent(snapshot))
            yield break;

        ShowAllActiveCards();
        Canvas.ForceUpdateCanvases();

        // Settle layout for one frame so Resize/Show activation is committed before the live
        // camera samples the canvas.
        yield return null;
        if (!_generation.IsCurrent(snapshot))
            yield break;

        Canvas.ForceUpdateCanvases();

        // Cache the signature only when all SetUp tasks succeeded; faulted frames stay
        // un-cached so the next selection retries instead of locking in a half-rendered board.
        if (HistoryPanelPreviewSignatureGate.ShouldCache(aggregate))
            _renderedSignature = signature;

        onPhase?.Invoke(BattleBoardRenderPhase.Done);
        onComplete?.Invoke();
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
        DisposeRuntimeObjects();
    }

    private bool EnsureInitialized()
    {
        if (HistoryPanelCardPreviewReflection.SetUpMethod == null)
            return false;

        if (
            _root != null
            && _canvas != null
            && _camera != null
            && _texture != null
            && _sockets != null
            && _pool != null
        )
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
        _camera.enabled = true; // live: renders every frame while the root is active
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent letterbox
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

        CreateRenderTexture();
        return true;
    }

    private void CreateRenderTexture()
    {
        if (_texture != null)
        {
            _texture.Release();
            Object.Destroy(_texture);
            _texture = null;
        }

        _texture = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32)
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
            _camera.orthographicSize = TextureHeight * 0.5f / CanvasReferencePixelsPerUnit;
            _camera.aspect = (float)TextureWidth / Mathf.Max(1, TextureHeight);
        }
    }

    private int SpawnCards(IReadOnlyList<HistoryItemSpec> items)
    {
        if (_pool == null || _sockets == null)
            return 0;

        var staticData = BppStaticDataAccess.TryGet();
        if (staticData == null)
            return 0;

        var i = 0;
        foreach (var spec in items)
        {
            if (spec == null || spec.TemplateId == Guid.Empty)
                continue;

            var template = HistoryPanelPreviewTemplateLookup.GetCardTemplate(
                staticData,
                spec.TemplateId
            );
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

        return i;
    }

    private RectTransform? ResolveSocket(
        EContainerSocketId? requested,
        int fallbackIndex,
        ECardSize size
    )
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

    private static TCardInstanceItem BuildSyntheticInstance(HistoryItemSpec spec, int index)
    {
        return new TCardInstanceItem
        {
            TemplateId = spec.TemplateId,
            TemplateVersion = string.Empty,
            InstanceId = $"bpp-battleboard-{index}",
            Tier = spec.Tier,
            SocketId = spec.SocketId ?? (EContainerSocketId)Mathf.Clamp(index, 0, 9),
            EnchantmentType = spec.EnchantmentType,
            Attributes =
                spec.Attributes != null
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
                "BattleBoardPreview",
                $"CardPreviewBase.SetUp threw for template={template?.Id}: {ex.InnerException?.Message ?? ex.Message}"
            );
            throw;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BattleBoardPreview",
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
                BppLog.Warn("BattleBoardPreview", $"CardPreviewBase.Show threw: {ex.Message}");
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

        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _camera = null;
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
