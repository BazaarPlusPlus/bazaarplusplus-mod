#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelPreviewRenderer
{
    private const int DefaultPreviewLayer = 30;
    private const int OverlaySortingOrder = 27;
    private const float PreviewHorizontalInset = 4f;
    private const float PreviewVerticalInset = 10f;

    private readonly int _layer;
    private GameObject? _root;
    private Canvas? _canvas;
    private RectTransform? _rootRect;
    private RectTransform? _clipRect;
    private RectTransform? _boardRect;
    private RectTransform[]? _sockets;
    private HistoryPanelPreviewCardPool? _pool;
    private readonly List<Component> _active = new();
    private readonly List<Task> _activeSetUpTasks = new();
    private readonly HistoryPanelPreviewGenerationGuard _generation = new();
    private string? _renderedSignature;
    private Rect _previewBounds;
    private bool _hasPreviewBounds;

    public HistoryPanelPreviewRenderer(int layer = DefaultPreviewLayer)
    {
        _layer = layer;
    }

    public Texture? CurrentTexture => null;

    public void CancelPending()
    {
        _generation.Bump();
    }

    public bool SetPreviewBounds(Rect bounds)
    {
        var normalized = NormalizePreviewBounds(bounds);
        var sizeChanged =
            !_hasPreviewBounds
            || !Mathf.Approximately(_previewBounds.width, normalized.width)
            || !Mathf.Approximately(_previewBounds.height, normalized.height);

        _previewBounds = normalized;
        _hasPreviewBounds = true;
        ApplyPreviewBounds();

        if (sizeChanged)
            _renderedSignature = null;
        return sizeChanged;
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
        ApplyPreviewBounds();
        ReturnActiveCardsToPool();
        _activeSetUpTasks.Clear();

        SpawnCards(previewData.Items);

        var aggregate = Task.WhenAll(_activeSetUpTasks);
        while (!aggregate.IsCompleted && _generation.IsCurrent(snapshot))
            yield return null;

        if (!_generation.IsCurrent(snapshot))
            yield break;

        ShowAllActiveCards();
        Canvas.ForceUpdateCanvases();

        // Settle layout for one frame so Resize/Show activation is committed before render.
        yield return null;
        if (!_generation.IsCurrent(snapshot))
            yield break;

        Canvas.ForceUpdateCanvases();

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

        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _rootRect = null;
            _clipRect = null;
            _boardRect = null;
        }
    }

    private bool EnsureInitialized()
    {
        if (HistoryPanelCardPreviewReflection.SetUpMethod == null)
            return false;

        if (
            _root != null
            && _canvas != null
            && _rootRect != null
            && _clipRect != null
            && _boardRect != null
            && _sockets != null
            && _pool != null
        )
        {
            ApplyPreviewBounds();
            return _pool.TryEnsurePrefabRefs();
        }

        DisposeRuntimeObjects();

        _pool = new HistoryPanelPreviewCardPool(_layer);
        if (!_pool.TryEnsurePrefabRefs())
        {
            DisposeRuntimeObjects();
            return false;
        }

        _root = new GameObject(
            "HistoryPanelPreviewOverlayRoot",
            typeof(RectTransform),
            typeof(Canvas)
        );
        _root.layer = _layer;
        _root.SetActive(false);

        _canvas = _root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = OverlaySortingOrder;
        _canvas.pixelPerfect = false;

        _rootRect = _root.GetComponent<RectTransform>();

        var clipObject = new GameObject(
            "HistoryPanelPreviewClip",
            typeof(RectTransform),
            typeof(RectMask2D)
        );
        clipObject.layer = _layer;
        clipObject.transform.SetParent(_root.transform, worldPositionStays: false);
        _clipRect = clipObject.GetComponent<RectTransform>();

        var boardObject = new GameObject("HistoryPanelPreviewBoard", typeof(RectTransform));
        boardObject.layer = _layer;
        boardObject.transform.SetParent(_clipRect, worldPositionStays: false);
        _boardRect = boardObject.GetComponent<RectTransform>();
        _sockets = HistoryPanelPreviewLayout.BuildSockets(_boardRect, _layer);

        ApplyPreviewBounds();
        return true;
    }

    private static Rect NormalizePreviewBounds(Rect bounds)
    {
        return new Rect(
            Mathf.Round(bounds.x),
            Mathf.Round(bounds.y),
            Mathf.Max(1f, Mathf.Round(bounds.width)),
            Mathf.Max(1f, Mathf.Round(bounds.height))
        );
    }

    private void ApplyPreviewBounds()
    {
        if (_rootRect == null || _clipRect == null || _boardRect == null)
            return;

        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.zero;
        _rootRect.pivot = Vector2.zero;
        _rootRect.anchoredPosition = Vector2.zero;
        _rootRect.sizeDelta = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        _rootRect.localScale = Vector3.one;

        if (!_hasPreviewBounds)
            return;

        var x = _previewBounds.x + PreviewHorizontalInset;
        var y = _previewBounds.y + PreviewVerticalInset;
        var width = Mathf.Max(1f, _previewBounds.width - PreviewHorizontalInset * 2f);
        var height = Mathf.Max(1f, _previewBounds.height - PreviewVerticalInset * 2f);

        _clipRect.anchorMin = Vector2.zero;
        _clipRect.anchorMax = Vector2.zero;
        _clipRect.pivot = Vector2.zero;
        _clipRect.anchoredPosition = new Vector2(x, y);
        _clipRect.sizeDelta = new Vector2(width, height);
        _clipRect.localScale = Vector3.one;

        var placement = HistoryPanelPreviewTextureGeometry.ResolveBoardPlacement(
            Mathf.RoundToInt(width),
            Mathf.RoundToInt(height)
        );
        _boardRect.anchorMin = Vector2.zero;
        _boardRect.anchorMax = Vector2.zero;
        _boardRect.pivot = Vector2.zero;
        _boardRect.anchoredPosition = new Vector2(placement.OffsetX, placement.OffsetY);
        _boardRect.sizeDelta = new Vector2(
            HistoryPanelPreviewTextureGeometry.NativeBoardWidth,
            HistoryPanelPreviewTextureGeometry.NativeBoardHeight
        );
        _boardRect.localScale = Vector3.one * placement.Scale;
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

        return i;
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

    private static TCardInstanceItem BuildSyntheticInstance(HistoryItemSpec spec, int index)
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

        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _rootRect = null;
            _clipRect = null;
            _boardRect = null;
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
