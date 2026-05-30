#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Preview;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Recycler virtualizer: instead of laying out every card in the visible set up front, we
// only realize cells inside the scroll window (+ overscan) and keep ~realizedRows*cols
// CardPreviewBase instances alive at a time. Scrolling just rewrites anchoredPosition on
// the existing cells (O(visible)); only when a cell index moves out of the window do we
// hand its CardPreviewBase back to the pool and bind a fresh one for the cell entering it.
//
// Cancellation race (design section 5.3): pool reuse means the same CardPreviewBase can be
// rebound while its previous SetUp's LoadFrame/LoadArt is still in-flight. The factory
// returns the SetUp Task; we hold it in the RealizedCell along with a generation counter.
// ShowWhenReady awaits the task and checks the generation before flipping the cell active:
// stale tasks no-op. When a cell is recycled mid-SetUp we mark it pending-return and only
// hand it back to the pool after the task settles, so the next Take never collides with
// the in-flight load on the same instance.
//
// Filter/tab changes cancel everything in flight by bumping the global generation guard
// and re-seeding the visible set; in-progress SetUp tasks complete (their continuations
// no-op because the generation has moved), and the realized cells are recycled.
internal sealed class CollectionGridVirtualizer
{
    private readonly CollectionGridOverlay _overlay;
    private readonly CollectionCardFactory _factory;
    private readonly Dictionary<int, RealizedCell> _realized = new();
    private readonly List<int> _recycleScratch = new();

    private IReadOnlyList<CollectionCardVm> _visible = Array.Empty<CollectionCardVm>();
    private ECardType _activeType = ECardType.Item;
    private float _viewportWidth;
    private float _viewportHeight;
    private float _scrollY;
    private float _cellWidth;
    private float _cellHeight;
    private float _gap;
    private int _columns = 1;
    private int _totalRows;
    private int _generation;
    private int _perCellGeneration;
    private float _lastScrollY = float.NaN;
    private int _hoverPollIndex = -1;
    private bool _hoverDispatched;

    public CollectionGridVirtualizer(CollectionGridOverlay overlay, CollectionCardFactory factory)
    {
        _overlay = overlay;
        _factory = factory;
    }

    public int Columns => _columns;
    public int TotalRows => _totalRows;
    public float RowHeight => _cellHeight + _gap;
    public float ContentHeight => _totalRows * RowHeight;
    public int VisibleCount => _visible.Count;

    // SetVisible swaps in a new ordered visible set (typically after filter change) and
    // recycles everything currently realized. Caller is expected to also reset scrollY to 0.
    public void SetVisible(IReadOnlyList<CollectionCardVm> visible, ECardType activeType)
    {
        BumpGeneration();
        _visible = visible ?? Array.Empty<CollectionCardVm>();
        _activeType = activeType;
        _cellWidth = CollectionGridConstants.CellWidthFor(activeType);
        _cellHeight = CollectionGridConstants.CellHeightFor(activeType);
        _gap = CollectionGridConstants.GridGap;
        RecycleAll();
        RecomputeLayout();
        _lastScrollY = float.NaN;
    }

    public void SetViewport(float width, float height)
    {
        if (
            Mathf.Approximately(_viewportWidth, width)
            && Mathf.Approximately(_viewportHeight, height)
        )
            return;
        _viewportWidth = Mathf.Max(0f, width);
        _viewportHeight = Mathf.Max(0f, height);
        RecomputeLayout();
        _lastScrollY = float.NaN;
    }

    public void SetScrollY(float y) => _scrollY = Mathf.Max(0f, y);

    public void Tick()
    {
        var board = _overlay.BoardRoot;
        if (board == null || _visible.Count == 0 || _columns <= 0 || _cellHeight <= 0f)
        {
            if (_realized.Count > 0)
                RecycleAll();
            return;
        }

        var rowHeight = RowHeight;
        var firstRow = Mathf.Max(
            0,
            Mathf.FloorToInt(_scrollY / rowHeight) - CollectionGridConstants.RowOverscan
        );
        var lastRow = Mathf.Min(
            _totalRows - 1,
            Mathf.FloorToInt((_scrollY + _viewportHeight) / rowHeight)
                + CollectionGridConstants.RowOverscan
        );
        var firstIdx = firstRow * _columns;
        var lastIdx = Mathf.Min(_visible.Count - 1, (lastRow + 1) * _columns - 1);

        // 1) Recycle cells that scrolled out of window.
        _recycleScratch.Clear();
        foreach (var pair in _realized)
        {
            if (pair.Key < firstIdx || pair.Key > lastIdx)
                _recycleScratch.Add(pair.Key);
        }
        foreach (var index in _recycleScratch)
        {
            var cell = _realized[index];
            _realized.Remove(index);
            RecycleCell(cell);
        }

        // 2) Realize newly-visible cells, rate-limited by wall-clock budget for cold binds.
        var tickStart = Time.realtimeSinceStartup;
        var coldBudgetSeconds = CollectionGridConstants.ColdBindBudgetMs * 0.001f;
        for (var idx = firstIdx; idx <= lastIdx; idx++)
        {
            if (_realized.ContainsKey(idx))
                continue;
            if (Time.realtimeSinceStartup - tickStart > coldBudgetSeconds)
                break;
            TryRealize(idx);
        }

        // 3) Reposition realized cells whenever the scroll offset moved. Skipping when
        // the value is unchanged avoids forcing a Canvas rebuild on idle frames.
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (_scrollY != _lastScrollY)
        {
            _lastScrollY = _scrollY;
            foreach (var pair in _realized)
                Reposition(pair.Key, pair.Value);
        }
    }

    public void Dispose()
    {
        BumpGeneration();
        RecycleAll();
        _visible = Array.Empty<CollectionCardVm>();
        _hoverPollIndex = -1;
        _hoverDispatched = false;
    }

    // Polled hover (CollectionGridConstants.UsePolledHover = true, the default). Called
    // from CollectionPanel.Update with the mouse position and the viewport rect, both in
    // screen pixels (bottom-left origin). Maps the cursor into the realized cell under it
    // and dispatches OnHover/OnHoverOut accordingly.
    //
    // Dispatch is deferred until the cell's SetUp Task completes successfully — without
    // this gate the cursor could land on a still-loading cell whose SetUp threw before
    // CreateTooltipData ran, and OnHover would NRE reading _tooltipData. _hoverDispatched
    // remembers whether we already fired OnHover for the current cell so subsequent frames
    // with the same idx retry until the cell becomes ready.
    public void PollHover(Vector2 mousePixels, Rect viewportBoundsPx)
    {
        if (_visible.Count == 0 || _columns <= 0)
        {
            DispatchHoverOut();
            return;
        }
        if (!viewportBoundsPx.Contains(mousePixels))
        {
            DispatchHoverOut();
            return;
        }

        var localX = mousePixels.x - viewportBoundsPx.x;
        // Viewport bottom-left origin → invert to top-left origin so layout math matches.
        var localY = viewportBoundsPx.height - (mousePixels.y - viewportBoundsPx.y);
        var contentY = localY + _scrollY;

        var colSpan = _cellWidth + _gap;
        var rowSpan = _cellHeight + _gap;
        var col = Mathf.FloorToInt(localX / colSpan);
        var row = Mathf.FloorToInt(contentY / rowSpan);
        if (col < 0 || col >= _columns || row < 0 || row >= _totalRows)
        {
            DispatchHoverOut();
            return;
        }

        // Reject hits inside the cell gap — those land in nothing visually.
        var inCol = localX - col * colSpan;
        var inRow = contentY - row * rowSpan;
        if (inCol > _cellWidth || inRow > _cellHeight)
        {
            DispatchHoverOut();
            return;
        }

        var idx = row * _columns + col;
        if (idx < 0 || idx >= _visible.Count)
        {
            DispatchHoverOut();
            return;
        }

        if (idx != _hoverPollIndex)
        {
            DispatchHoverOut();
            _hoverPollIndex = idx;
            _hoverDispatched = false;
        }

        // Already fired OnHover for this cell — nothing to do until the cursor leaves.
        if (_hoverDispatched)
            return;

        // Cell may be realized but its SetUp Task could still be in flight or have faulted.
        // OnHover reads _tooltipData which is only populated by CreateTooltipData inside
        // SetUp's sync prefix; a fault before that point leaves it null. Wait for a clean
        // completion before dispatching, and retry on subsequent frames if not yet ready.
        if (_realized.TryGetValue(idx, out var cell) && cell.SetUpTask.IsCompletedSuccessfully)
        {
            cell.HoverRelay?.OnPointerEnter(null!);
            _hoverDispatched = true;
        }
    }

    private void DispatchHoverOut()
    {
        if (_hoverPollIndex < 0 || !_hoverDispatched)
        {
            _hoverPollIndex = -1;
            _hoverDispatched = false;
            return;
        }
        if (_realized.TryGetValue(_hoverPollIndex, out var cell))
            cell.HoverRelay?.TryInvokeHoverOut();
        _hoverPollIndex = -1;
        _hoverDispatched = false;
    }

    private void TryRealize(int index)
    {
        var vm = _visible[index];
        var binding = _factory.TryBind(vm);
        if (binding == null)
            return;
        var card = binding.Value.Card;
        var rect = card.transform as RectTransform;
        if (rect == null)
        {
            _factory.Return(card, binding.Value.Kind);
            return;
        }

        var hover = card.gameObject.GetComponent<CollectionCardHoverRelay>();
        if (hover == null)
            hover = card.gameObject.AddComponent<CollectionCardHoverRelay>();
        hover.Bind(card);

        if (!CollectionGridConstants.UsePolledHover)
            EnsureHitTarget(card.gameObject);

        var cell = new RealizedCell(
            index,
            vm,
            binding.Value.Card,
            binding.Value.Kind,
            binding.Value.SetUpTask,
            ++_perCellGeneration,
            hover,
            rect
        );
        _realized[index] = cell;
        Reposition(index, cell);
        ApplyCellScale(cell);
        _ = ShowWhenReady(cell, _generation);
    }

    private void ApplyCellScale(RealizedCell cell)
    {
        var rect = cell.CachedRect;
        if (rect == null)
            return;
        var sizeDelta = rect.sizeDelta;
        var natW = Mathf.Max(1f, sizeDelta.x);
        var natH = Mathf.Max(1f, sizeDelta.y);
        var scale = Mathf.Min(_cellWidth / natW, _cellHeight / natH);
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
            scale = 1f;
        rect.localScale = new Vector3(scale, scale, 1f);
    }

    private void Reposition(int index, RealizedCell cell)
    {
        var rect = cell.CachedRect;
        if (rect == null)
            return;
        var row = index / _columns;
        var col = index % _columns;
        var cellOriginX = col * (_cellWidth + _gap);
        var cellOriginY = row * (_cellHeight + _gap) - _scrollY;
        // Board pivot is top-left, so y goes negative. Place card pivot at cell center.
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(
            cellOriginX + _cellWidth * 0.5f,
            -(cellOriginY + _cellHeight * 0.5f)
        );
    }

    private static void EnsureHitTarget(GameObject host)
    {
        var hit = host.GetComponent<Image>();
        if (hit == null)
        {
            hit = host.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            // Push the hit image to the back of the sibling list so it does not paint over
            // the card frame/art; raycastTarget is unaffected by sibling order.
            hit.transform.SetAsFirstSibling();
        }
        else
        {
            hit.raycastTarget = true;
        }
    }

    private async Task ShowWhenReady(RealizedCell cell, int generationSnapshot)
    {
        try
        {
            await cell.SetUpTask;
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CollectionGridVirtualizer",
                $"SetUp task for {cell.Vm.Id} faulted: {ex.Message}"
            );
        }

        if (generationSnapshot != _generation)
            return;
        if (cell.PendingReturn)
        {
            CompleteRecycle(cell);
            return;
        }

        if (cell.Card == null)
            return;
        try
        {
            cell.Card.gameObject.SetActive(true);
            HistoryPanelCardPreviewReflection.ShowMethod?.Invoke(cell.Card, new object[] { true });
            // Show(true) re-activates _cardImage / _frameContainer; the CanvasGroup at the
            // root was zeroed on Take, so the card still renders transparent. Hand the cell
            // off to TickFades to ramp it up.
            cell.FadeAlpha = 0f;
            cell.FadeActive = true;
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CollectionGridVirtualizer",
                $"Show invocation for {cell.Vm.Id} failed: {ex.Message}"
            );
        }
    }

    // Advance the per-cell fade-in animation. Called from CollectionPanel.Update each frame
    // while the panel is visible. Cells that ShowWhenReady has not yet handed off remain at
    // CanvasGroup.alpha = 0 (set on Take) and are skipped here.
    public void TickFades(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
            return;
        var t = 1f - Mathf.Exp(-deltaSeconds / CollectionGridConstants.CardFadeInSeconds);
        foreach (var pair in _realized)
        {
            var cell = pair.Value;
            if (!cell.FadeActive || cell.Card == null)
                continue;
            cell.FadeAlpha = Mathf.Lerp(cell.FadeAlpha, 1f, t);
            if (cell.FadeAlpha >= 0.995f)
            {
                cell.FadeAlpha = 1f;
                cell.FadeActive = false;
            }
            var canvasGroup = cell.Card.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
                canvasGroup.alpha = cell.FadeAlpha;
        }
    }

    private void RecycleCell(RealizedCell cell)
    {
        cell.HoverRelay?.Clear();
        if (cell.SetUpTask is { IsCompleted: false })
        {
            cell.PendingReturn = true;
            return;
        }
        CompleteRecycle(cell);
    }

    private void CompleteRecycle(RealizedCell cell)
    {
        cell.HoverRelay?.Clear();
        _factory.Return(cell.Card, cell.Kind);
    }

    private void RecycleAll()
    {
        foreach (var pair in _realized)
            RecycleCell(pair.Value);
        _realized.Clear();
    }

    private void BumpGeneration() => _generation++;

    private void RecomputeLayout()
    {
        if (_visible.Count == 0 || _cellWidth <= 0f || _viewportWidth <= 0f)
        {
            _columns = Mathf.Max(1, CollectionGridConstants.MinColumnsFor(_activeType));
            _totalRows = 0;
            return;
        }
        var available = _viewportWidth + _gap;
        var per = _cellWidth + _gap;
        var raw = Mathf.FloorToInt(available / per);
        _columns = Mathf.Clamp(
            raw,
            CollectionGridConstants.MinColumnsFor(_activeType),
            CollectionGridConstants.MaxColumnsFor(_activeType)
        );
        _totalRows = Mathf.CeilToInt(_visible.Count / (float)_columns);
    }

    private sealed class RealizedCell
    {
        public RealizedCell(
            int index,
            CollectionCardVm vm,
            Component card,
            CollectionCardKind kind,
            Task setUpTask,
            int generation,
            CollectionCardHoverRelay hoverRelay,
            RectTransform cachedRect
        )
        {
            Index = index;
            Vm = vm;
            Card = card;
            Kind = kind;
            SetUpTask = setUpTask;
            Generation = generation;
            HoverRelay = hoverRelay;
            CachedRect = cachedRect;
        }

        public int Index { get; }
        public CollectionCardVm Vm { get; }
        public Component Card { get; }
        public CollectionCardKind Kind { get; }
        public Task SetUpTask { get; }
        public int Generation { get; }
        public CollectionCardHoverRelay HoverRelay { get; }
        public RectTransform CachedRect { get; }
        public bool PendingReturn { get; set; }

        // Fade state. ShowWhenReady sets FadeActive=true with FadeAlpha=0 right after the
        // card's Show(true); TickFades ramps FadeAlpha → 1 and writes it to the CanvasGroup.
        // FadeActive stays false during the SetUp loading phase so the card remains hidden
        // (the CanvasGroup alpha was zeroed on Take).
        public bool FadeActive { get; set; }
        public float FadeAlpha { get; set; }
    }
}
