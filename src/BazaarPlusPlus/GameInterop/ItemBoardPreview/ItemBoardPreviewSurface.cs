#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal sealed class ItemBoardPreviewSurface : IDisposable
{
    private readonly ItemBoardPreviewGenerationGuard _generation = new();
    private readonly List<NativeCardPreviewHandle> _active = new();

    private ItemBoardPreviewOptions _options = new();
    private CancellationTokenSource? _loadCancellation;
    private RuntimeCreationTracker? _runtimeTracker;
    private GameObject? _root;
    private Canvas? _canvas;
    private CanvasGroup? _canvasGroup;
    private RectTransform? _rootRect;
    private RectTransform? _clipRect;
    private RectTransform? _boardRect;
    private RectTransform[]? _sockets;
    private NativeCardPreviewPool? _pool;
    private NativeCardPreviewFactory? _factory;
    private NativeCardPreviewHoverRelay? _hoverRelay;
    private NativeCardPreviewHandle? _hoveredHandle;
    private string? _renderedSignature;
    private Vector2 _position = Vector2.zero;
    private Vector2 _clipSize = new(
        ItemBoardSocketLayout.NativeBoardWidth,
        ItemBoardSocketLayout.NativeBoardHeight
    );
    private float _cardScale = 1f;
    private int _runtimeLayer = int.MinValue;

    public bool EnsureInitialized()
    {
        return EnsureInitialized(_options);
    }

    public void SetPosition(Vector2 screenBottomLeft)
    {
        var rounded = new Vector2(Mathf.Round(screenBottomLeft.x), Mathf.Round(screenBottomLeft.y));
        if (
            Mathf.Approximately(_position.x, rounded.x)
            && Mathf.Approximately(_position.y, rounded.y)
        )
        {
            return;
        }

        _position = rounded;
        ApplyTransform();
    }

    public void SetClipSize(Vector2 pixels)
    {
        var rounded = new Vector2(
            Mathf.Max(1f, Mathf.Round(pixels.x)),
            Mathf.Max(1f, Mathf.Round(pixels.y))
        );
        if (
            Mathf.Approximately(_clipSize.x, rounded.x)
            && Mathf.Approximately(_clipSize.y, rounded.y)
        )
        {
            return;
        }

        _clipSize = rounded;
        ApplyTransform();
        _renderedSignature = null;
    }

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
        IReadOnlyList<NativeCardPreviewSpec>? cards,
        ItemBoardPreviewOptions options,
        string? signature = null,
        Action<ItemBoardPreviewPhase>? onPhase = null,
        Action? onComplete = null
    )
    {
        _options = options ?? new ItemBoardPreviewOptions();
        CancelPending();

        if (cards == null || cards.Count == 0)
        {
            onPhase?.Invoke(ItemBoardPreviewPhase.Empty);
            Hide();
            onComplete?.Invoke();
            yield break;
        }

        if (!EnsureInitialized(_options))
        {
            onPhase?.Invoke(ItemBoardPreviewPhase.InitFailed);
            onComplete?.Invoke();
            yield break;
        }

        if (
            !string.IsNullOrEmpty(_renderedSignature)
            && !string.IsNullOrEmpty(signature)
            && string.Equals(_renderedSignature, signature, StringComparison.Ordinal)
        )
        {
            onPhase?.Invoke(ItemBoardPreviewPhase.Done);
            onComplete?.Invoke();
            yield break;
        }

        var snapshot = _generation.Bump();
        var loadCancellation = new CancellationTokenSource();
        _loadCancellation = loadCancellation;
        var token = loadCancellation.Token;

        onPhase?.Invoke(ItemBoardPreviewPhase.Loading);
        _root!.SetActive(true);
        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f;

        ApplyTransform();
        ReturnActiveCardsToPool();
        var factory = _factory!;
        var sockets = _sockets!;
        var tracker = _runtimeTracker!;
        var operation = tracker.RegisterOperation();
        var aggregate = CreateCardsForRenderAsync(
            cards,
            factory,
            sockets,
            snapshot,
            token,
            tracker,
            operation
        );
        var claimed = false;
        var completedLoad = false;

        try
        {
            while (
                !aggregate.IsCompleted
                && _generation.IsCurrent(snapshot)
                && !token.IsCancellationRequested
            )
            {
                yield return null;
            }

            if (!_generation.IsCurrent(snapshot) || token.IsCancellationRequested)
                yield break;

            var creation = aggregate.GetAwaiter().GetResult();
            if (!operation.TryClaim())
                yield break;

            claimed = true;
            _active.AddRange(creation.Handles);
            if (_active.Count == 0)
            {
                onPhase?.Invoke(ItemBoardPreviewPhase.Empty);
                Hide();
                onComplete?.Invoke();
                yield break;
            }

            ShowSetUpCards();
            Canvas.ForceUpdateCanvases();

            yield return null;
            if (!_generation.IsCurrent(snapshot))
                yield break;

            Canvas.ForceUpdateCanvases();
            if (_options.LayoutMode == ItemBoardPreviewLayoutMode.SlotGrid)
                LayoutCardsSlotGrid();
            else if (_options.LayoutMode == ItemBoardPreviewLayoutMode.Packed)
                LayoutCardsPacked();

            if (ItemBoardPreviewSignatureGate.ShouldCache(aggregate) && !creation.HadFailures)
                _renderedSignature = signature;

            CompleteLoad(loadCancellation);
            completedLoad = true;
            onPhase?.Invoke(ItemBoardPreviewPhase.Done);
            onComplete?.Invoke();
        }
        finally
        {
            if (!completedLoad)
            {
                if (claimed)
                {
                    CompleteLoad(loadCancellation);
                }
                else
                {
                    operation.Abandon();
                    CancelAndDisposeLoad(loadCancellation);
                }
            }
        }
    }

    public void PollHover(Vector2 mousePixels)
    {
        if (!_options.ShowHover || _root == null || !_root.activeSelf || _clipRect == null)
        {
            DispatchHoverOut();
            return;
        }

        var clipBounds = new Rect(_position.x, _position.y, _clipSize.x, _clipSize.y);
        if (!clipBounds.Contains(mousePixels))
        {
            DispatchHoverOut();
            return;
        }

        var next = FindHoveredHandle(mousePixels);
        if (ReferenceEquals(next, _hoveredHandle))
            return;

        DispatchHoverOut();
        if (next == null)
            return;

        _hoveredHandle = next;
        _hoverRelay ??= new NativeCardPreviewHoverRelay(_options.LogComponent);
        _hoverRelay.Bind(next.Card);
        _hoverRelay.InvokeHover();
    }

    public void Hide()
    {
        CancelPending();
        _renderedSignature = null;
        ReturnActiveCardsToPool();
        if (_canvasGroup != null)
            _canvasGroup.alpha = 0f;
        if (_root != null)
            _root.SetActive(false);
    }

    public void CancelPending()
    {
        _generation.Bump();
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        if (cancellation != null)
            CancelAndDispose(cancellation);

        _runtimeTracker?.AbandonOperations();
        DispatchHoverOut();
    }

    public void Dispose()
    {
        CancelPending();
        _renderedSignature = null;
        DisposeRuntimeObjects();
        _runtimeLayer = int.MinValue;
    }

    private bool EnsureInitialized(ItemBoardPreviewOptions options)
    {
        if (NativeCardPreviewReflection.SetUpMethod == null)
            return false;

        if (_runtimeLayer != int.MinValue && _runtimeLayer != options.Layer)
            DisposeRuntimeObjects();

        _runtimeLayer = options.Layer;
        _pool ??= new NativeCardPreviewPool(
            options.Layer,
            requireSockets: true,
            options.LogComponent
        );
        _factory ??= new NativeCardPreviewFactory(_pool, options.LogComponent);
        _hoverRelay ??= new NativeCardPreviewHoverRelay(options.LogComponent);

        if (
            _root != null
            && _canvas != null
            && _rootRect != null
            && _clipRect != null
            && _boardRect != null
            && _sockets != null
        )
        {
            ApplyOptions(options);
            ApplyTransform();
            _runtimeTracker ??= new RuntimeCreationTracker(_root, _factory);
            return true;
        }

        DisposeRuntimeObjects();
        _runtimeLayer = options.Layer;
        _pool = new NativeCardPreviewPool(
            options.Layer,
            requireSockets: true,
            options.LogComponent
        );
        _factory = new NativeCardPreviewFactory(_pool, options.LogComponent);
        _hoverRelay = new NativeCardPreviewHoverRelay(options.LogComponent);

        _root = new GameObject("BppItemBoardPreviewSurface", typeof(RectTransform), typeof(Canvas));
        _root.layer = options.Layer;
        _root.SetActive(false);

        _canvas = _root.GetComponent<Canvas>();
        _rootRect = _root.GetComponent<RectTransform>();
        ApplyOptions(options);

        var clipObject = new GameObject(
            "BppItemBoardPreviewClip",
            typeof(RectTransform),
            typeof(RectMask2D)
        );
        clipObject.layer = options.Layer;
        clipObject.transform.SetParent(_root.transform, worldPositionStays: false);
        _clipRect = clipObject.GetComponent<RectTransform>();

        var boardObject = new GameObject("BppItemBoardPreviewBoard", typeof(RectTransform));
        boardObject.layer = options.Layer;
        boardObject.transform.SetParent(_clipRect, worldPositionStays: false);
        _boardRect = boardObject.GetComponent<RectTransform>();
        _sockets = ItemBoardSocketLayout.BuildSockets(
            _boardRect,
            options.Layer,
            "BppItemBoardPreviewSocket"
        );
        _runtimeTracker = new RuntimeCreationTracker(_root, _factory);

        ApplyTransform();
        return true;
    }

    private void ApplyOptions(ItemBoardPreviewOptions options)
    {
        if (_canvas != null)
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = options.SortingOrder;
            _canvas.pixelPerfect = false;
        }

        if (!options.UseCanvasGroup)
            return;

        if (_root != null && _canvasGroup == null)
            _canvasGroup = _root.AddComponent<CanvasGroup>();
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = _root?.activeSelf == true ? 1f : 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }
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

        _clipRect.anchorMin = Vector2.zero;
        _clipRect.anchorMax = Vector2.zero;
        _clipRect.pivot = Vector2.zero;
        _clipRect.anchoredPosition = _position;
        _clipRect.sizeDelta = _clipSize;
        _clipRect.localScale = Vector3.one;

        if (_options.LayoutMode == ItemBoardPreviewLayoutMode.SlotGrid)
        {
            _boardRect.anchorMin = Vector2.zero;
            _boardRect.anchorMax = Vector2.one;
            _boardRect.pivot = new Vector2(0.5f, 0.5f);
            _boardRect.anchoredPosition = Vector2.zero;
            _boardRect.sizeDelta = Vector2.zero;
            _boardRect.localScale = Vector3.one;
            return;
        }

        _boardRect.anchorMin = new Vector2(0.5f, 0.5f);
        _boardRect.anchorMax = new Vector2(0.5f, 0.5f);
        _boardRect.pivot = new Vector2(0.5f, 0.5f);
        _boardRect.anchoredPosition = Vector2.zero;
        _boardRect.sizeDelta = new Vector2(
            ItemBoardSocketLayout.NativeBoardWidth,
            ItemBoardSocketLayout.NativeBoardHeight
        );
        _boardRect.localScale = Vector3.one * _cardScale;
    }

    private async Task<CardCreationCollection> CreateCardsForRenderAsync(
        IReadOnlyList<NativeCardPreviewSpec> cards,
        NativeCardPreviewFactory factory,
        RectTransform[] sockets,
        int snapshot,
        CancellationToken token,
        RuntimeCreationTracker tracker,
        CardCreationOperation operation
    )
    {
        var creation = await SpawnCardsAsync(cards, factory, sockets, token);
        var staleOrCanceled =
            !_generation.IsCurrent(snapshot)
            || token.IsCancellationRequested
            || !ReferenceEquals(_runtimeTracker, tracker);

        return operation.CompleteCreation(creation, staleOrCanceled);
    }

    private async Task<CardCreationCollection> SpawnCardsAsync(
        IReadOnlyList<NativeCardPreviewSpec> cards,
        NativeCardPreviewFactory factory,
        RectTransform[] sockets,
        CancellationToken token
    )
    {
        var tasks = new List<Task<CardCreationResult>>();
        var fallbackIndex = 0;
        foreach (var spec in cards)
        {
            if (!factory.TryResolveSpan(spec, out var span))
                continue;

            var socketIndex = ItemBoardSocketResolver.ResolveIndex(
                sockets.Length,
                spec.SocketId.HasValue ? (int)spec.SocketId.Value : (int?)null,
                fallbackIndex,
                span
            );
            if (socketIndex < 0)
                continue;

            tasks.Add(
                CreateCardHandleAsync(factory, spec, sockets[socketIndex], fallbackIndex, token)
            );
            fallbackIndex++;
        }

        var aggregate =
            tasks.Count == 0
                ? Task.FromResult(Array.Empty<CardCreationResult>())
                : Task.WhenAll(tasks);

        return await CollectCreatedHandlesAsync(aggregate, token);
    }

    private async Task<CardCreationResult> CreateCardHandleAsync(
        NativeCardPreviewFactory factory,
        NativeCardPreviewSpec spec,
        RectTransform socket,
        int fallbackIndex,
        CancellationToken token
    )
    {
        try
        {
            var handle = await factory.CreateAsync(spec, socket, fallbackIndex, token);
            if (handle != null)
                return CardCreationResult.Success(handle);

            return token.IsCancellationRequested
                ? CardCreationResult.FromCanceled(spec, fallbackIndex)
                : CardCreationResult.Failed(spec, fallbackIndex, null);
        }
        catch (OperationCanceledException)
        {
            return CardCreationResult.FromCanceled(spec, fallbackIndex);
        }
        catch (Exception ex)
        {
            return CardCreationResult.Failed(spec, fallbackIndex, ex);
        }
    }

    private async Task<CardCreationCollection> CollectCreatedHandlesAsync(
        Task<CardCreationResult[]> aggregate,
        CancellationToken token
    )
    {
        var created = new List<NativeCardPreviewHandle>();
        var hadFailures = false;
        CardCreationResult[] results;
        try
        {
            results = await aggregate;
        }
        catch (OperationCanceledException)
        {
            return new CardCreationCollection(created, hadFailures: true);
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                BppLog.Warn(
                    _options.LogComponent,
                    $"Card preview creation aggregate failed: {ex.Message}"
                );
            return new CardCreationCollection(created, hadFailures: true);
        }

        foreach (var result in results)
        {
            if (result.Handle != null)
            {
                created.Add(result.Handle);
                continue;
            }

            if (result.Canceled)
                continue;

            hadFailures = true;
            var message =
                result.Exception != null
                    ? $"Card preview creation failed template={result.TemplateId} index={result.FallbackIndex}: {result.Exception.Message}"
                    : $"Card preview creation returned no handle template={result.TemplateId} index={result.FallbackIndex}.";
            BppLog.Warn(_options.LogComponent, message);
        }

        return new CardCreationCollection(created, hadFailures);
    }

    private static void ReturnHandles(
        NativeCardPreviewFactory factory,
        IReadOnlyList<NativeCardPreviewHandle> handles
    )
    {
        foreach (var handle in handles)
            factory.Return(handle);
    }

    private void CompleteLoad(CancellationTokenSource cancellation)
    {
        if (!ReferenceEquals(_loadCancellation, cancellation))
            return;

        _loadCancellation = null;
        cancellation.Dispose();
    }

    private void CancelAndDisposeLoad(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_loadCancellation, cancellation))
            _loadCancellation = null;

        CancelAndDispose(cancellation);
    }

    private static void CancelAndDispose(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A caller may have already canceled and disposed this source.
        }

        cancellation.Dispose();
    }

    private void ShowSetUpCards()
    {
        if (_factory == null)
            return;

        foreach (var handle in _active)
        {
            if (handle.SetUpTask.IsCompletedSuccessfully)
                _factory.Show(handle);
        }
    }

    private void LayoutCardsPacked()
    {
        if (_boardRect == null || _active.Count == 0)
            return;

        var corners = new Vector3[4];
        var laid = new List<(Transform card, float frameLeft, float frameWidth)>();
        foreach (var handle in _active)
        {
            if (!handle.SetUpTask.IsCompletedSuccessfully || handle.Card == null)
                continue;

            var root = handle.Rect;
            var frame = FindDescendant(root, "FrameContainer") ?? root;
            frame.GetWorldCorners(corners);
            laid.Add((handle.Card.transform, corners[0].x, corners[2].x - corners[0].x));
        }

        if (laid.Count == 0)
            return;

        laid.Sort((a, b) => a.frameLeft.CompareTo(b.frameLeft));

        var totalFrameWidth = 0f;
        foreach (var entry in laid)
            totalFrameWidth += entry.frameWidth;

        var cursor = _boardRect.position.x - totalFrameWidth * 0.5f;
        foreach (var entry in laid)
        {
            entry.card.position += new Vector3(cursor - entry.frameLeft, 0f, 0f);
            cursor += entry.frameWidth;
        }
    }

    private void LayoutCardsSlotGrid()
    {
        if (_active.Count == 0)
            return;

        var corners = new Vector3[4];
        foreach (var handle in _active)
        {
            if (!handle.SetUpTask.IsCompletedSuccessfully || handle.Card == null)
                continue;
            if (!handle.Card.gameObject.activeInHierarchy)
                continue;

            var frame = FindDescendant(handle.Rect, "FrameContainer") ?? handle.Rect;
            frame.GetWorldCorners(corners);
            var frameWidth = corners[2].x - corners[0].x;
            var frameHeight = corners[2].y - corners[0].y;
            if (frameWidth <= 0f || frameHeight <= 0f)
                continue;

            var socketIndex = handle.Spec.SocketId.HasValue ? (int)handle.Spec.SocketId.Value : 0;
            var occupied = ItemBoardSlotGridGeometry.ResolveOccupiedRect(
                _clipSize.x,
                _clipSize.y,
                socketIndex,
                handle.Spec.DisplaySpan,
                _options.SlotGridHorizontalInsetPixels,
                _options.SlotGridVerticalInsetPixels
            );
            var targetHeight = ItemBoardSlotGridGeometry.ResolveScaledTargetHeight(
                occupied.Height,
                ItemBoardSocketLayout.NativeBoardHeight,
                _cardScale,
                _options.SlotGridMaxHeightRatio
            );
            var scale = ItemBoardSlotGridGeometry.ResolveHeightScale(
                frameHeight,
                targetHeight,
                _options.SlotGridMaxScale
            );

            var cardTransform = handle.Card.transform;
            cardTransform.localScale = new Vector3(
                cardTransform.localScale.x * scale,
                cardTransform.localScale.y * scale,
                cardTransform.localScale.z
            );

            frame.GetWorldCorners(corners);
            var frameCenter = new Vector2(
                (corners[0].x + corners[2].x) * 0.5f,
                (corners[0].y + corners[2].y) * 0.5f
            );
            var targetCenter = new Vector2(
                _position.x + occupied.CenterX,
                _position.y + occupied.CenterY
            );
            cardTransform.position += new Vector3(
                targetCenter.x - frameCenter.x,
                targetCenter.y - frameCenter.y,
                0f
            );
        }
    }

    private NativeCardPreviewHandle? FindHoveredHandle(Vector2 mousePixels)
    {
        var corners = new Vector3[4];
        foreach (var handle in _active)
        {
            if (!handle.SetUpTask.IsCompletedSuccessfully || handle.Card == null)
                continue;
            if (!handle.Card.gameObject.activeInHierarchy)
                continue;

            handle.Rect.GetWorldCorners(corners);
            var rect = new Rect(
                corners[0].x,
                corners[0].y,
                corners[2].x - corners[0].x,
                corners[2].y - corners[0].y
            );
            if (rect.Contains(mousePixels))
                return handle;
        }

        return null;
    }

    private void DispatchHoverOut()
    {
        _hoverRelay?.Clear();
        _hoveredHandle = null;
    }

    private void ReturnActiveCardsToPool()
    {
        DispatchHoverOut();
        if (_factory != null)
        {
            foreach (var handle in _active)
                _factory.Return(handle);
        }

        _active.Clear();
    }

    private void DisposeRuntimeObjects()
    {
        ReturnActiveCardsToPool();
        var tracker = _runtimeTracker ?? new RuntimeCreationTracker(_root, _factory);
        tracker.RequestCleanup();

        _factory = null;
        _pool = null;
        _sockets = null;
        _runtimeTracker = null;
        _root = null;
        _canvas = null;
        _canvasGroup = null;
        _rootRect = null;
        _clipRect = null;
        _boardRect = null;
        _hoverRelay = null;
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

    private readonly struct CardCreationResult
    {
        private CardCreationResult(
            NativeCardPreviewHandle? handle,
            NativeCardPreviewSpec spec,
            int fallbackIndex,
            Exception? exception,
            bool canceled
        )
        {
            Handle = handle;
            TemplateId = spec.TemplateId;
            FallbackIndex = fallbackIndex;
            Exception = exception;
            Canceled = canceled;
        }

        public NativeCardPreviewHandle? Handle { get; }
        public Guid TemplateId { get; }
        public int FallbackIndex { get; }
        public Exception? Exception { get; }
        public bool Canceled { get; }

        public static CardCreationResult Success(NativeCardPreviewHandle handle) =>
            new(handle, handle.Spec, -1, null, canceled: false);

        public static CardCreationResult Failed(
            NativeCardPreviewSpec spec,
            int fallbackIndex,
            Exception? exception
        ) => new(null, spec, fallbackIndex, exception, canceled: false);

        public static CardCreationResult FromCanceled(
            NativeCardPreviewSpec spec,
            int fallbackIndex
        ) => new(null, spec, fallbackIndex, null, canceled: true);
    }

    private readonly struct CardCreationCollection
    {
        public CardCreationCollection(
            IReadOnlyList<NativeCardPreviewHandle> handles,
            bool hadFailures
        )
        {
            Handles = handles;
            HadFailures = hadFailures;
        }

        public IReadOnlyList<NativeCardPreviewHandle> Handles { get; }
        public bool HadFailures { get; }

        public CardCreationCollection WithoutHandles() =>
            new(Array.Empty<NativeCardPreviewHandle>(), HadFailures);
    }

    private sealed class RuntimeCreationTracker
    {
        private readonly object _syncRoot = new();
        private readonly HashSet<CardCreationOperation> _operations = new();
        private bool _cleanupRequested;
        private bool _destroyed;

        public RuntimeCreationTracker(GameObject? root, NativeCardPreviewFactory? factory)
        {
            Root = root;
            Factory = factory;
        }

        public GameObject? Root { get; }
        public NativeCardPreviewFactory? Factory { get; }

        public bool CleanupRequested
        {
            get
            {
                lock (_syncRoot)
                    return _cleanupRequested;
            }
        }

        public CardCreationOperation RegisterOperation()
        {
            var operation = new CardCreationOperation(this);
            lock (_syncRoot)
                _operations.Add(operation);

            return operation;
        }

        public void OperationSettled(CardCreationOperation operation)
        {
            lock (_syncRoot)
                _operations.Remove(operation);

            DestroyIfReady();
        }

        public void RequestCleanup()
        {
            if (Root != null)
                Root.SetActive(false);

            List<CardCreationOperation> operations;
            lock (_syncRoot)
            {
                _cleanupRequested = true;
                operations = new List<CardCreationOperation>(_operations);
            }

            foreach (var operation in operations)
                operation.Abandon();

            DestroyIfReady();
        }

        public void AbandonOperations()
        {
            List<CardCreationOperation> operations;
            lock (_syncRoot)
                operations = new List<CardCreationOperation>(_operations);

            foreach (var operation in operations)
                operation.Abandon();
        }

        private void DestroyIfReady()
        {
            var destroyNow = false;
            lock (_syncRoot)
            {
                if (!_cleanupRequested || _operations.Count != 0 || _destroyed)
                    return;

                _destroyed = true;
                destroyNow = true;
            }

            if (!destroyNow)
                return;

            Factory?.DestroyAll();
            if (Root != null)
                Object.Destroy(Root);
        }
    }

    private sealed class CardCreationOperation
    {
        private readonly RuntimeCreationTracker _tracker;
        private readonly object _syncRoot = new();
        private CardCreationCollection? _published;
        private bool _creationCompleted;
        private bool _abandoned;
        private bool _claimed;

        public CardCreationOperation(RuntimeCreationTracker tracker)
        {
            _tracker = tracker;
        }

        public CardCreationCollection CompleteCreation(
            CardCreationCollection creation,
            bool staleOrCanceled
        )
        {
            var shouldReturn = false;
            lock (_syncRoot)
            {
                _creationCompleted = true;
                shouldReturn =
                    staleOrCanceled || _abandoned || _claimed || _tracker.CleanupRequested;
                if (!shouldReturn)
                    _published = creation;
            }

            if (shouldReturn)
                ReturnHandles(_tracker.Factory!, creation.Handles);

            if (shouldReturn)
                _tracker.OperationSettled(this);

            return shouldReturn ? creation.WithoutHandles() : creation;
        }

        public bool TryClaim()
        {
            lock (_syncRoot)
            {
                if (_abandoned || _claimed || !_published.HasValue)
                    return false;

                _published = null;
                _claimed = true;
            }

            _tracker.OperationSettled(this);
            return true;
        }

        public void Abandon()
        {
            CardCreationCollection? abandoned;
            var settled = false;
            lock (_syncRoot)
            {
                if (_abandoned || _claimed)
                    return;

                _abandoned = true;
                abandoned = _published;
                _published = null;
                settled = _creationCompleted;
            }

            if (abandoned.HasValue)
                ReturnHandles(_tracker.Factory!, abandoned.Value.Handles);

            if (settled)
                _tracker.OperationSettled(this);
        }
    }
}
