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
using Object = UnityEngine.Object;
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel.Preview;

internal enum BattleBoardRenderPhase
{
    Empty,
    InitFailed,
    Loading,
    Done,
}

// Self-contained 10-socket card board preview. Owns its overlay Canvas, clip, socket
// RectTransforms, card pool, and reflection-driven SetUp/Show/Resize lifecycle. The caller
// supplies cards (HistoryItemSpec list) and configures three independent transform knobs:
// SetPosition / SetClipSize / SetCardScale. Status reporting is via BattleBoardRenderPhase
// so callers map to their own UI strings.
internal sealed class BattleBoardPreview
{
    private const int DefaultLayer = 30;
    private const int OverlaySortingOrder = 27;

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

    // Default geometry: clip area exactly matches the native board at 1.0× card scale,
    // anchored at the screen's bottom-left. Callers override via SetPosition / SetClipSize.
    private Vector2 _position = Vector2.zero;
    private Vector2 _clipSize = new(
        HistoryPanelPreviewTextureGeometry.NativeBoardWidth,
        HistoryPanelPreviewTextureGeometry.NativeBoardHeight
    );
    private float _cardScale = 1f;

    public BattleBoardPreview(int layer = DefaultLayer)
    {
        _layer = layer;
    }

    public void CancelPending()
    {
        _generation.Bump();
    }

    // Screen-space pixel coordinates of the clip rect's bottom-left corner.
    public void SetPosition(Vector2 position)
    {
        var rounded = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
        if (Mathf.Approximately(_position.x, rounded.x)
            && Mathf.Approximately(_position.y, rounded.y))
            return;

        _position = rounded;
        ApplyTransform();
    }

    // Size of the clip rect in pixels. Cards rendered outside this rect are masked.
    public void SetClipSize(Vector2 size)
    {
        var rounded = new Vector2(
            Mathf.Max(1f, Mathf.Round(size.x)),
            Mathf.Max(1f, Mathf.Round(size.y))
        );
        if (Mathf.Approximately(_clipSize.x, rounded.x)
            && Mathf.Approximately(_clipSize.y, rounded.y))
            return;

        _clipSize = rounded;
        ApplyTransform();
    }

    // Multiplier on top of native card size (the prefab's intrinsic sizeDelta from
    // MonsterBoardTooltip sockets). 1.0 = native; values < 1 shrink; > 1 enlarge and may
    // overflow the clip rect. Returns true if the value actually changed; in that case the
    // render cache is invalidated, and the caller should re-issue Render() if it wants the
    // cards re-taken from the pool with a fresh CardPreviewBase.Resize.
    public bool SetCardScale(float scale)
    {
        var clamped = Mathf.Max(0.05f, scale);
        if (Mathf.Approximately(_cardScale, clamped))
            return false;

        _cardScale = clamped;
        ApplyTransform();
        _renderedSignature = null;
        return true;
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

        if (!string.IsNullOrEmpty(_renderedSignature)
            && !string.IsNullOrEmpty(signature)
            && string.Equals(_renderedSignature, signature, StringComparison.Ordinal))
        {
            onPhase?.Invoke(BattleBoardRenderPhase.Done);
            onComplete?.Invoke();
            yield break;
        }

        onPhase?.Invoke(BattleBoardRenderPhase.Loading);

        _root!.SetActive(true);
        ApplyTransform();
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

        // Settle layout for one frame so Resize/Show activation is committed before render.
        yield return null;
        if (!_generation.IsCurrent(snapshot))
            yield break;

        Canvas.ForceUpdateCanvases();

        // Cache the signature only when all SetUp tasks succeeded; faulted frames stay
        // un-cached so the next selection retries instead of locking in a half-rendered RT.
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
            ApplyTransform();
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
            "BattleBoardPreviewRoot",
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
            "BattleBoardPreviewClip",
            typeof(RectTransform),
            typeof(UnityEngine.UI.RectMask2D)
        );
        clipObject.layer = _layer;
        clipObject.transform.SetParent(_root.transform, worldPositionStays: false);
        _clipRect = clipObject.GetComponent<RectTransform>();

        var boardObject = new GameObject("BattleBoardPreviewBoard", typeof(RectTransform));
        boardObject.layer = _layer;
        boardObject.transform.SetParent(_clipRect, worldPositionStays: false);
        _boardRect = boardObject.GetComponent<RectTransform>();
        _sockets = HistoryPanelPreviewLayout.BuildSockets(_boardRect, _layer);

        ApplyTransform();
        return true;
    }

    private void ApplyTransform()
    {
        if (_rootRect == null || _clipRect == null || _boardRect == null)
            return;

        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.zero;
        _rootRect.pivot = Vector2.zero;
        _rootRect.anchoredPosition = Vector2.zero;
        _rootRect.sizeDelta = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        _rootRect.localScale = Vector3.one;

        // Clip rect: anchored at bottom-left of overlay canvas, pivot bottom-left, exact
        // pixel size from _clipSize. Anything outside is masked by RectMask2D.
        _clipRect.anchorMin = Vector2.zero;
        _clipRect.anchorMax = Vector2.zero;
        _clipRect.pivot = Vector2.zero;
        _clipRect.anchoredPosition = _position;
        _clipRect.sizeDelta = _clipSize;
        _clipRect.localScale = Vector3.one;

        // Board: anchored to clip-rect center with center pivot, so the native 2400x600
        // board stays visually centred regardless of clip size. Scale is the card-scale
        // multiplier; values > 1 will bleed past the clip and get masked.
        _boardRect.anchorMin = new Vector2(0.5f, 0.5f);
        _boardRect.anchorMax = new Vector2(0.5f, 0.5f);
        _boardRect.pivot = new Vector2(0.5f, 0.5f);
        _boardRect.anchoredPosition = Vector2.zero;
        _boardRect.sizeDelta = new Vector2(
            HistoryPanelPreviewTextureGeometry.NativeBoardWidth,
            HistoryPanelPreviewTextureGeometry.NativeBoardHeight
        );
        _boardRect.localScale = Vector3.one * _cardScale;
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
            InstanceId = $"bpp-battleboard-{index}",
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
