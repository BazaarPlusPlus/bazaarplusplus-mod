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
    private readonly List<CollectionGridRect> _slotRects = new();
    private readonly CollectionGridSlotLayer? _slots;

    private IReadOnlyList<CollectionCardVm> _visible = Array.Empty<CollectionCardVm>();
    private CollectionGridLayout _layout = CollectionGridLayout.Empty;
    private float _viewportWidth;
    private float _viewportHeight;
    private float _scrollY;

    // Pixelization of the fixed unit grid for the current viewport. _unit is one column's
    // width (px), derived from the viewport so the 8-column grid fills the width then clamps;
    // _originX/_originY are the display-case inset (horizontal centering + uniform padding).
    private float _unit;
    private float _gap = CollectionGridConstants.GridGap;
    private float _originX = CollectionGridConstants.GridOuterPadding;
    private float _originY = CollectionGridConstants.GridOuterPadding;
    private bool _scaleDirty = true;

    private int _generation;
    private int _perCellGeneration;
    private float _lastScrollY = float.NaN;
    private int _hoverPollIndex = -1;
    private bool _hoverDispatched;

    public CollectionGridVirtualizer(CollectionGridOverlay overlay, CollectionCardFactory factory)
    {
        _overlay = overlay;
        _factory = factory;
        if (overlay.BoardRoot != null)
            _slots = new CollectionGridSlotLayer(overlay.BoardRoot);
    }

    // Content height in overlay pixels: top + bottom display-case padding plus the packed
    // shelves. Consumed by the UITK ScrollView spacer so the scrollbar range is correct.
    public float ContentHeight =>
        _layout.ShelfCount == 0
            ? 0f
            : 2f * CollectionGridConstants.GridOuterPadding + _layout.ContentHeight(_unit, _gap);
    public int VisibleCount => _visible.Count;

    // SetVisible swaps in a new ordered visible set (typically after filter change) and
    // recycles everything currently realized. Caller is expected to also reset scrollY to 0.
    public void SetVisible(IReadOnlyList<CollectionCardVm> visible, ECardType activeType)
    {
        BumpGeneration();
        _visible = visible ?? Array.Empty<CollectionCardVm>();
        _gap = CollectionGridConstants.GridGap;
        _layout = CollectionGridLayout.Build(_visible, activeType);
        RecomputePixelization();
        RecycleAll();
        _slots?.Clear();
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
        RecomputePixelization();
        _lastScrollY = float.NaN;
    }

    public void SetScrollY(float y) => _scrollY = Mathf.Max(0f, y);

    public void Tick()
    {
        var board = _overlay.BoardRoot;
        if (board == null || _visible.Count == 0 || _layout.ShelfCount == 0 || _unit <= 0f)
        {
            if (_realized.Count > 0)
                RecycleAll();
            _slots?.Clear();
            return;
        }

        // Visible window is a contiguous shelf range. Shelves are a uniform pitch within a tab
        // (Items 2 units, Skills 1 unit tall), so the first/last visible shelf is a division,
        // and each shelf maps directly onto a contiguous visible-index span.
        var shelfPitch = _layout.ShelfPitch(_unit, _gap);
        // Clamp BOTH ends into [0, maxShelf] before indexing ShelfAt. _scrollY can transiently
        // overshoot the packed content — a frame during a viewport resize before the ScrollView
        // re-clamps its offset, or a DPI scroll-range mismatch — which would otherwise push
        // firstShelf past the last shelf (or lastShelf below 0) and throw IndexOutOfRange.
        var maxShelf = _layout.ShelfCount - 1;
        var firstShelf = Mathf.Clamp(
            Mathf.FloorToInt((_scrollY - _originY) / shelfPitch)
                - CollectionGridConstants.RowOverscan,
            0,
            maxShelf
        );
        var lastShelf = Mathf.Clamp(
            Mathf.FloorToInt((_scrollY + _viewportHeight - _originY) / shelfPitch)
                + CollectionGridConstants.RowOverscan,
            0,
            maxShelf
        );
        var firstIdx = _layout.ShelfAt(firstShelf).FirstIndex;
        var lastIdx = _layout.ShelfAt(lastShelf).LastIndex;

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

        // 3) Reposition realized cells whenever the scroll offset moved (or the base unit
        // changed on a viewport resize, _scaleDirty). Skipping when nothing changed avoids
        // forcing a Canvas rebuild on idle frames. Slots track the same window so the grid
        // order is visible even where cards have not finished loading.
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (_scrollY != _lastScrollY || _scaleDirty)
        {
            _lastScrollY = _scrollY;
            foreach (var pair in _realized)
            {
                Reposition(pair.Key, pair.Value);
                if (_scaleDirty)
                    ApplyCellScale(pair.Key, pair.Value);
            }
            _scaleDirty = false;
            SyncSlots(firstIdx, lastIdx);
        }
    }

    public void Dispose()
    {
        BumpGeneration();
        RecycleAll();
        _slots?.Clear();
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
        if (_visible.Count == 0 || _layout.ShelfCount == 0 || _unit <= 0f)
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

        // Hit-test against precomputed cell rects: find the shelf band under the cursor, then
        // the cell within it whose span rect contains the point. Rects exclude the gutter, so a
        // cursor in the gap matches nothing and dispatches hover-out — same as the old gap reject.
        var shelfPitch = _layout.ShelfPitch(_unit, _gap);
        if (shelfPitch <= 0f)
        {
            DispatchHoverOut();
            return;
        }
        var shelf = Mathf.FloorToInt((contentY - _originY) / shelfPitch);
        if (shelf < 0 || shelf >= _layout.ShelfCount)
        {
            DispatchHoverOut();
            return;
        }

        var band = _layout.ShelfAt(shelf);
        var idx = -1;
        for (var candidate = band.FirstIndex; candidate <= band.LastIndex; candidate++)
        {
            if (
                _layout
                    .ContentRectFor(candidate, _unit, _gap, _originX, _originY)
                    .Contains(localX, contentY)
            )
            {
                idx = candidate;
                break;
            }
        }
        if (idx < 0)
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

        // Move the display-case hover highlight to the pointed cell (board-local rect, y down).
        var hoverRect = _layout.ContentRectFor(idx, _unit, _gap, _originX, _originY);
        _slots?.SetHover(
            new CollectionGridRect(
                hoverRect.X,
                hoverRect.Y - _scrollY,
                hoverRect.Width,
                hoverRect.Height
            )
        );

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
        // Hide the display-case highlight on every hover-out path (outside the viewport, in a
        // gutter, off the grid, or moving to a new cell).
        _slots?.SetHover(null);
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
        ApplyCellScale(index, cell);
        _ = ShowWhenReady(cell, _generation);
    }

    // Scale the native card to fit its span cell, centered, never stretched. The cell is shrunk
    // by CellContentInset on every side so the slot background reads as a frame around the card.
    private void ApplyCellScale(int index, RealizedCell cell)
    {
        var rect = cell.CachedRect;
        if (rect == null)
            return;
        var cellRect = _layout.ContentRectFor(index, _unit, _gap, _originX, _originY);
        var inset = CollectionGridConstants.CellContentInset;
        var sizeDelta = rect.sizeDelta;
        var natW = Mathf.Max(1f, sizeDelta.x);
        var natH = Mathf.Max(1f, sizeDelta.y);

        // Scale to the cell HEIGHT so every card in a shelf renders the same height. Native item
        // cards share one prefab height and a shelf shares one cell height, so a height-based
        // scale aligns small/medium/large tops and bottoms — the old min(w,h) fit left the
        // narrow small cards width-limited and therefore slightly shorter. Clamp so a card never
        // grows past its cell width + one gutter (prevents overlapping the neighbour); because
        // that bound is exactly span*(unit+gap), the clamped scale is identical across spans, so
        // the uniform height survives even when a card is width-limited.
        var targetH = Mathf.Max(1f, cellRect.Height * (1f - 2f * inset));
        var scale = targetH / natH;
        var maxWidth = cellRect.Width + _gap;
        if (natW * scale > maxWidth)
            scale = maxWidth / natW;
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
            scale = 1f;
        rect.localScale = new Vector3(scale, scale, 1f);
    }

    private void Reposition(int index, RealizedCell cell)
    {
        var rect = cell.CachedRect;
        if (rect == null)
            return;
        var cellRect = _layout.ContentRectFor(index, _unit, _gap, _originX, _originY);
        var screenTop = cellRect.Y - _scrollY;
        // Board pivot is top-left, so y goes negative. Place card pivot at cell center.
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(
            cellRect.X + cellRect.Width * 0.5f,
            -(screenTop + cellRect.Height * 0.5f)
        );
    }

    // Push the visible window's board-local cell rects to the slot layer. Driven by the layout
    // window (not the realized-card set), so every visible cell shows its display-case slot
    // immediately, before its native card art has loaded. Board space is top-left origin, y
    // increasing downward, so content Y is offset by the current scroll.
    private void SyncSlots(int firstIdx, int lastIdx)
    {
        if (_slots == null)
            return;
        _slotRects.Clear();
        for (var idx = firstIdx; idx <= lastIdx; idx++)
        {
            var r = _layout.ContentRectFor(idx, _unit, _gap, _originX, _originY);
            _slotRects.Add(new CollectionGridRect(r.X, r.Y - _scrollY, r.Width, r.Height));
        }
        _slots.Sync(_slotRects);
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

    // Derive the per-viewport base unit and display-case origin. The grid is always 8 columns:
    // the unit fills the available width then clamps to [Min, Max]; once clamped, any surplus
    // width becomes centering margin (originX) rather than extra columns. originY is the fixed
    // top padding. Marks the realized cards for a rescale on the next reposition pass.
    private void RecomputePixelization()
    {
        _scaleDirty = true;
        var columns = _layout.Columns;
        var pad = CollectionGridConstants.GridOuterPadding;
        _originY = pad;
        if (_viewportWidth <= 0f)
        {
            _unit = CollectionGridConstants.MinUnitWidth;
            _originX = pad;
            return;
        }
        var available = _viewportWidth - 2f * pad - (columns - 1) * _gap;
        var rawUnit = available / columns;
        _unit = Mathf.Clamp(rawUnit, CollectionGridConstants.MinUnitWidth, _layout.MaxUnitWidth);
        var gridWidth = columns * _unit + (columns - 1) * _gap;
        _originX = Mathf.Max(pad, (_viewportWidth - gridWidth) * 0.5f);
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
