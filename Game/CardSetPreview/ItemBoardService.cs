#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CardSetPreview;

internal sealed class ItemBoardService : IDisposable
{
    private const int OverlayLayer = 30;
    private const int BoardSortingOrder = 27;
    private const int ChromeSortingOrder = 28;

    private sealed class ItemBoardCoroutineHost : MonoBehaviour { }

    private readonly ItemBoardPreviewSurface _surface = new();
    private readonly SponsorPanelRenderer _sponsorPanel = new();
    private readonly ItemBoardPreviewOptions _options = new()
    {
        Layer = OverlayLayer,
        SortingOrder = BoardSortingOrder,
        LayoutMode = ItemBoardPreviewLayoutMode.Socketed,
        ShowHover = true,
        LogComponent = "ItemBoardService",
    };

    private GameObject? _chromeRoot;
    private RectTransform? _chromeRootRect;
    private Canvas? _chromeCanvas;
    private ItemBoardCoroutineHost? _coroutineHost;
    private Coroutine? _renderCoroutine;
    private ItemBoardTemplateSetRequest? _currentRequest;

    public bool IsAlive => _coroutineHost != null && _chromeRoot != null;

    public bool ShowTemplateSet(ItemBoardTemplateSetRequest request)
    {
        if (request == null)
            return false;

        _currentRequest = request.Clone();
        var items =
            _currentRequest.Items?.Where(item => item?.TemplateId != Guid.Empty).ToList()
            ?? new List<ItemBoardItemSpec>();
        if (items.Count == 0)
        {
            Hide();
            return true;
        }

        if (!EnsureRuntime())
            return false;
        ApplyChromeGeometry();

        var scale = Mathf.Clamp(_currentRequest.Scale, 0.2f, 2f);
        var anchoredPosition = _currentRequest.AnchoredPosition ?? Vector2.zero;
        var boardSize = new Vector2(
            ItemBoardSocketLayout.NativeBoardWidth * scale,
            ItemBoardSocketLayout.NativeBoardHeight * scale
        );
        var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) + anchoredPosition;

        _surface.SetPosition(center - boardSize * 0.5f);
        _surface.SetClipSize(boardSize);
        _surface.SetCardScale(scale);

        _chromeRoot!.SetActive(true);
        _sponsorPanel.UpdateSponsorVisual(
            _currentRequest.SponsorText,
            _currentRequest.SponsorName,
            _currentRequest.SponsorTier,
            _currentRequest.CandidateIndex,
            _currentRequest.CandidateCount,
            _currentRequest.IsAlertState,
            scale,
            anchoredPosition
        );

        StopRenderCoroutine();
        _renderCoroutine = _coroutineHost!.StartCoroutine(
            _surface.Render(MapSpecs(items), _options)
        );
        return true;
    }

    public void PollHover(Vector2 mousePixels)
    {
        _surface.PollHover(mousePixels);
    }

    public void Hide(float hideTime = 0f)
    {
        StopRenderCoroutine();
        _surface.Hide();
        _chromeRoot?.SetActive(false);
    }

    public void Dispose()
    {
        StopRenderCoroutine();
        _surface.Dispose();
        if (_chromeRoot != null)
            Object.Destroy(_chromeRoot);

        _chromeRoot = null;
        _chromeRootRect = null;
        _chromeCanvas = null;
        _coroutineHost = null;
        _currentRequest = null;
        _sponsorPanel.Reset();
    }

    private bool EnsureRuntime()
    {
        if (
            _chromeRoot != null
            && _chromeRootRect != null
            && _chromeCanvas != null
            && _coroutineHost != null
        )
        {
            return true;
        }

        DisposeChrome();

        _chromeRoot = new GameObject("BppItemBoardChrome", typeof(RectTransform), typeof(Canvas));
        _chromeRoot.layer = OverlayLayer;
        _chromeRootRect = _chromeRoot.GetComponent<RectTransform>();
        ApplyChromeGeometry();

        _chromeCanvas = _chromeRoot.GetComponent<Canvas>();
        _chromeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _chromeCanvas.overrideSorting = true;
        _chromeCanvas.sortingOrder = ChromeSortingOrder;
        _chromeCanvas.pixelPerfect = false;

        _coroutineHost = _chromeRoot.AddComponent<ItemBoardCoroutineHost>();
        _sponsorPanel.CreateSponsorPanel(_chromeRootRect);
        _chromeRoot.SetActive(false);
        return true;
    }

    private void ApplyChromeGeometry()
    {
        if (_chromeRootRect == null)
            return;

        _chromeRootRect.anchorMin = Vector2.zero;
        _chromeRootRect.anchorMax = Vector2.zero;
        _chromeRootRect.pivot = Vector2.zero;
        _chromeRootRect.anchoredPosition = Vector2.zero;
        _chromeRootRect.sizeDelta = new Vector2(
            Mathf.Max(1, Screen.width),
            Mathf.Max(1, Screen.height)
        );
    }

    private void DisposeChrome()
    {
        StopRenderCoroutine();
        if (_chromeRoot != null)
            Object.Destroy(_chromeRoot);

        _chromeRoot = null;
        _chromeRootRect = null;
        _chromeCanvas = null;
        _coroutineHost = null;
        _sponsorPanel.Reset();
    }

    private void StopRenderCoroutine()
    {
        if (_coroutineHost != null && _renderCoroutine != null)
            _coroutineHost.StopCoroutine(_renderCoroutine);

        _renderCoroutine = null;
        _surface.CancelPending();
    }

    private static IReadOnlyList<NativeCardPreviewSpec> MapSpecs(
        IReadOnlyList<ItemBoardItemSpec> items
    )
    {
        var specs = new List<NativeCardPreviewSpec>(items.Count);
        foreach (var item in items)
        {
            if (item == null || item.TemplateId == Guid.Empty)
                continue;

            specs.Add(
                new NativeCardPreviewSpec
                {
                    TemplateId = item.TemplateId,
                    Tier = item.Tier,
                    SocketId = item.SocketId,
                    EnchantmentType = item.EnchantmentType,
                    Attributes = item.Attributes,
                    InstanceIdPrefix = "bpp-itemboard",
                }
            );
        }

        return specs;
    }
}
