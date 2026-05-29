#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.HistoryPanel.Data;
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

// Self-contained 10-socket card board preview. Owns its overlay Canvas, clip, socket
// RectTransforms and the reflection-driven SetUp/Show/Resize lifecycle (delegated to
// BattleBoardCardFactory). The caller supplies cards (HistoryItemSpec list) and configures three
// independent transform knobs: SetPosition / SetClipSize / SetCardScale. Status reporting is via
// BattleBoardRenderPhase so callers map to their own UI strings.
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
    private readonly BattleBoardCardFactory _factory;
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
        _factory = new BattleBoardCardFactory(layer);
    }

    public void CancelPending()
    {
        _generation.Bump();
    }

    // Screen-space pixel coordinates of the clip rect's bottom-left corner.
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

    // Size of the clip rect in pixels. Cards rendered outside this rect are masked.
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

        LayoutCardsPacked();

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

        _factory.DestroyAll();
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
        if (!_factory.ReflectionReady)
            return false;

        if (
            _root != null
            && _canvas != null
            && _rootRect != null
            && _clipRect != null
            && _boardRect != null
            && _sockets != null
        )
        {
            ApplyTransform();
            return _factory.TryEnsurePrefabRefs();
        }

        DisposeRuntimeObjects();

        if (!_factory.EnsureReady())
        {
            DisposeRuntimeObjects();
            return false;
        }

        _root = new GameObject("BattleBoardPreviewRoot", typeof(RectTransform), typeof(Canvas));
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

    private void SpawnCards(IReadOnlyList<HistoryItemSpec> items)
    {
        if (_sockets == null)
            return;

        // `i` counts only successfully spawned cards: it is both the socket fallback index and the
        // synthetic instance index, and must advance only when a card is actually placed.
        var i = 0;
        foreach (var spec in items)
        {
            var spawn = _factory.TrySpawn(spec, i, _sockets);
            if (spawn == null)
                continue;

            _activeSetUpTasks.Add(spawn.Value.SetUpTask);
            _active.Add(spawn.Value.Card);
            i++;
        }
    }

    // Packs the spawned cards edge-to-edge by their FRAME width (the gold border, not the wider
    // gem bounding box), centered on the board. Cards of any size (1/2/3 slots) sit flush with no
    // gaps and no overlap, independent of the slot grid — frame widths are measured live so it
    // adapts to the real card art. (Trade-off: real empty-slot gaps are closed up.)
    private void LayoutCardsPacked()
    {
        if (_boardRect == null || _active.Count == 0)
            return;

        var corners = new Vector3[4];
        var laid = new List<(Transform card, float frameLeft, float frameWidth)>();
        foreach (var card in _active)
        {
            if (card == null || card.transform is not RectTransform root)
                continue;
            var frame = FindDescendant(root, "FrameContainer") ?? root;
            frame.GetWorldCorners(corners);
            laid.Add((card.transform, corners[0].x, corners[2].x - corners[0].x));
        }
        if (laid.Count == 0)
            return;

        // Pre-pack order is the socket (SocketId) order, captured by current left edge.
        laid.Sort((a, b) => a.frameLeft.CompareTo(b.frameLeft));

        var totalFrameWidth = 0f;
        foreach (var entry in laid)
            totalFrameWidth += entry.frameWidth;

        // Board pivot is centered → position.x is the board centre in screen px. Translate each
        // card (whole, preserving its internal layout) so its frame sits flush after the previous.
        var cursor = _boardRect.position.x - totalFrameWidth * 0.5f;
        foreach (var entry in laid)
        {
            entry.card.position += new Vector3(cursor - entry.frameLeft, 0f, 0f);
            cursor += entry.frameWidth;
        }
    }

    private static RectTransform? FindDescendant(Transform root, string childName)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt != null && rt.name == childName)
                return rt;
        }
        return null;
    }

    private void ShowAllActiveCards()
    {
        _factory.Show(_active);
    }

    private void ReturnActiveCardsToPool()
    {
        foreach (var card in _active)
            _factory.Return(card);

        _active.Clear();
    }

    private void DisposeRuntimeObjects()
    {
        _active.Clear();
        _activeSetUpTasks.Clear();

        _factory.DestroyAll();
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
