#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using HistoryPanelHost = BazaarPlusPlus.Game.HistoryPanel.HistoryPanel;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanel : MonoBehaviour
{
    private const float SearchDebounceSeconds = 0.20f;

    private static CollectionPanel? _instance;
    public static bool IsVisible => _instance != null && _instance._isVisible;
    internal static CollectionPanel? Instance => _instance;

    private static readonly EHero[] HeroOrder = new[]
    {
        EHero.Common,
        EHero.Vanessa,
        EHero.Pygmalien,
        EHero.Dooley,
        EHero.Mak,
        EHero.Jules,
        EHero.Karnok,
        EHero.Stelle,
    };

    private static readonly ETier[] TierOrder = new[]
    {
        ETier.Bronze,
        ETier.Silver,
        ETier.Gold,
        ETier.Diamond,
        ETier.Legendary,
    };

    private readonly CollectionCatalog _catalog = new();
    private readonly CollectionFilterState _filter = new();
    private readonly List<CollectionCardVm> _visibleScratch = new();

    private IBppConfig _config = null!;
    private CollectionPanelView? _view;
    private CollectionGridOverlay? _overlay;
    private CollectionCardPool? _pool;
    private CollectionCardFactory? _factory;
    private CollectionGridVirtualizer? _virtualizer;
    private CollectionCardArtCache? _artCache;
    private CollectionCardMaterialCache? _materialCache;

    private IReadOnlyList<CollectionCardVm> _catalogCards = Array.Empty<CollectionCardVm>();
    private bool _isVisible;
    private bool _initialized;
    private string _lastSceneToken = string.Empty;
    private string _pendingSearch = string.Empty;
    private string _appliedSearch = string.Empty;
    private float _pendingSearchAt = float.NaN;
    private bool _statusVisible;
    private string? _statusMessage;
    private bool _viewportBoundsDirty;
    private Rect _viewportBoundsPx;
    private float _scrollY;

    public void Initialize(IBppServices services)
    {
        if (_initialized)
            return;
        _initialized = true;
        _instance = this;
        _config = services.Config;
        _lastSceneToken = GetSceneToken(SceneManager.GetActiveScene());
    }

    internal static void NotifyLocaleChanged()
    {
        if (_instance == null)
            return;
        _instance._catalog.InvalidateCache();
        _instance._catalogCards = Array.Empty<CollectionCardVm>();
        if (_instance._isVisible)
        {
            _instance.RebuildCatalogIfPossible();
            _instance.ApplyFilters();
            _instance.RefreshView();
        }
    }

    internal static void OpenFromDockEntry()
    {
        if (_instance == null)
        {
            BppLog.Warn("CollectionPanel", "Dock entry requested before CollectionPanel mounted.");
            return;
        }
        _instance.Open();
    }

    private void Open()
    {
        if (TheBazaar.Data.IsInCombat)
        {
            BppLog.Info("CollectionPanel", "Open suppressed: combat is active.");
            return;
        }

        // Panel mutex: collection + history share the same overlay sorting band (26/27),
        // showing both would z-fight, and the user cannot resolve which Escape press targets
        // which panel. Close the other before showing this one.
        if (HistoryPanelHost.IsVisible)
        {
            try
            {
                HistoryPanelHost.Instance?.ToggleFromHotkey();
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "CollectionPanel",
                    $"Failed to close HistoryPanel before opening CollectionPanel: {ex.Message}"
                );
            }
        }

        EnsureView();
        _isVisible = true;
        // SetVisible starts the fade-in ramp; overlay activates so its CanvasGroup starts
        // mirroring the view's opacity (Update pushes the live value each frame).
        _view!.SetVisible(true);
        _overlay?.SetVisible(true);
        _overlay?.SetAlpha(_view!.CurrentOpacity);

        RebuildCatalogIfPossible();
        ApplyFilters();
        RefreshView();
    }

    private void Close()
    {
        if (!_isVisible)
            return;
        _isVisible = false;
        // SetVisible(false) flips the fade target to 0; Update keeps ticking the fade and
        // mirroring opacity to the overlay until CurrentOpacity reaches ~0, at which point
        // the not-visible branch in Update does the actual SetActive(false) + virtualizer
        // teardown. Doing those immediately here would pop the cards off mid-fade.
        _view?.SetVisible(false);
    }

    private void Update()
    {
        DetectSceneChange();

        if (_isVisible && TheBazaar.Data.IsInCombat)
        {
            Close();
            return;
        }

        var togglePath = _config.CollectionPanelHotkeyPathConfig?.Value;
        if (
            !string.IsNullOrWhiteSpace(togglePath)
            && BppHotkeyService.WasPressedThisFrame(togglePath!)
        )
        {
            if (_isVisible)
                Close();
            else
                Open();
            return;
        }

        var dt = Time.unscaledDeltaTime;

        // Drive the panel fade every frame regardless of _isVisible so a Close mid-frame
        // can finish its fade-out animation before we tear runtime down.
        _view?.TickOpacity(dt);
        if (_view != null && _overlay != null)
            _overlay.SetAlpha(_view.CurrentOpacity);

        if (!_isVisible)
        {
            // Once the fade-out has settled, release the overlay's GameObject and the
            // virtualizer's realized cells so the next Open starts clean.
            if (_view != null && !_view.IsFadingOrVisible)
            {
                _overlay?.SetVisible(false);
                _virtualizer?.Dispose();
            }
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (!string.IsNullOrEmpty(_pendingSearch) || _pendingSearch != _appliedSearch)
        {
            if (
                !float.IsNaN(_pendingSearchAt)
                && Time.unscaledTime - _pendingSearchAt >= SearchDebounceSeconds
            )
            {
                _appliedSearch = _pendingSearch;
                _filter.Search = _pendingSearch;
                _pendingSearchAt = float.NaN;
                ApplyFilters();
                RefreshView();
            }
        }

        if (_viewportBoundsDirty && _virtualizer != null && _overlay != null)
        {
            _viewportBoundsDirty = false;
            _overlay.SetPosition(_viewportBoundsPx.position);
            _overlay.SetClipSize(_viewportBoundsPx.size);
            _virtualizer.SetViewport(_viewportBoundsPx.width, _viewportBoundsPx.height);
        }

        // UITK's default wheel handler updates scrollOffset directly; we just read the
        // resulting position each frame and feed it to the virtualizer. (An earlier smooth-
        // wheel intercept lived here but killed wheel scrolling entirely — see §16.6.)
        if (_view != null)
            _scrollY = _view.ReadScrollYPixels();
        _virtualizer?.SetScrollY(_scrollY);
        _virtualizer?.Tick();
        _virtualizer?.TickFades(dt);

        if (CollectionGridConstants.UsePolledHover && _virtualizer != null)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var pos = mouse.position.ReadValue();
                _virtualizer.PollHover(pos, _viewportBoundsPx);
            }
        }
    }

    private void DetectSceneChange()
    {
        var token = GetSceneToken(SceneManager.GetActiveScene());
        if (string.Equals(token, _lastSceneToken, StringComparison.Ordinal))
            return;
        _lastSceneToken = token;
        if (_isVisible)
            Close();
        DisposeRuntime();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;
        DisposeRuntime();
    }

    private void DisposeRuntime()
    {
        _virtualizer?.Dispose();
        _virtualizer = null;
        // Destroy card GameObjects first so their patched OnDestroy can null _cardMaterial
        // and Release art-cache refcounts BEFORE we tear the caches down.
        _pool?.DestroyAll();
        _pool = null;
        _factory = null;
        _overlay?.Dispose();
        _overlay = null;
        _view?.Dispose();
        _view = null;
        CollectionCardCacheHost.Uninstall(_artCache, _materialCache);
        _materialCache?.DisposeAll();
        _materialCache = null;
        _artCache?.DisposeAll();
        _artCache = null;
        _catalogCards = Array.Empty<CollectionCardVm>();
        _catalog.InvalidateCache();
    }

    private void EnsureView()
    {
        if (_view != null)
            return;

        _view = new CollectionPanelView(
            transform,
            close: Close,
            setActiveType: type =>
            {
                if (_filter.ActiveType == type)
                    return;
                _filter.ActiveType = type;
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            },
            toggleHero: hero =>
            {
                if (!_filter.Heroes.Remove(hero))
                    _filter.Heroes.Add(hero);
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            },
            toggleTier: tier =>
            {
                if (!_filter.Tiers.Remove(tier))
                    _filter.Tiers.Add(tier);
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            },
            setSearch: value =>
            {
                _pendingSearch = value ?? string.Empty;
                _pendingSearchAt = Time.unscaledTime;
            },
            clearFilters: () =>
            {
                _filter.Heroes.Clear();
                _filter.Tiers.Clear();
                _filter.Search = string.Empty;
                _appliedSearch = string.Empty;
                _pendingSearch = string.Empty;
                _pendingSearchAt = float.NaN;
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            }
        );

        _view.GridViewportBoundsChanged += bounds =>
        {
            _viewportBoundsPx = bounds;
            _viewportBoundsDirty = true;
        };

        _view.EnsureCreated();

        _overlay = new CollectionGridOverlay();
        _overlay.EnsureInitialized();

        _artCache = new CollectionCardArtCache();
        _materialCache = new CollectionCardMaterialCache();
        CollectionCardCacheHost.Install(_artCache, _materialCache);

        _pool = new CollectionCardPool(CollectionGridOverlay.DefaultLayer);
        _factory = new CollectionCardFactory(_pool, _overlay.BoardRoot!);
        _virtualizer = new CollectionGridVirtualizer(_overlay, _factory);
    }

    private void RebuildCatalogIfPossible()
    {
        if (_catalogCards.Count > 0)
            return;
        if (_catalog.TryBuild(out var cards))
        {
            _catalogCards = cards;
            ClearStatus();
        }
        else
        {
            _catalogCards = Array.Empty<CollectionCardVm>();
            SetStatus(CollectionPanelText.CatalogUnavailable());
        }
    }

    private void ApplyFilters()
    {
        if (_virtualizer == null)
            return;
        _visibleScratch.Clear();
        if (_catalogCards.Count > 0)
        {
            var ordered = CollectionFilterEngine.Apply(_catalogCards, _filter);
            _virtualizer.SetVisible(ordered, _filter.ActiveType);
        }
        else
        {
            _virtualizer.SetVisible(Array.Empty<CollectionCardVm>(), _filter.ActiveType);
        }
        // Reset the actual ScrollView scrollOffset and cancel any in-flight smooth-wheel
        // animation, otherwise switching from a large set to a small one strands the user
        // at the bottom (scrollOffset clamps to the new max instead of returning to top).
        _view?.ResetScroll();
        _scrollY = 0f;
    }

    private void RefreshView()
    {
        if (_view == null || _virtualizer == null)
            return;

        var model = new CollectionPanelViewModel
        {
            Title = CollectionPanelText.Title(),
            Subtitle = CollectionPanelText.Subtitle(),
            CountText = CollectionPanelText.MatchCount(_virtualizer.VisibleCount),
            StatusMessage = _statusVisible ? _statusMessage : null,
            ActiveType = _filter.ActiveType,
            SelectedHeroes = new HashSet<EHero>(_filter.Heroes),
            SelectedTiers = new HashSet<ETier>(_filter.Tiers),
            Search = _filter.Search,
            AvailableHeroes = HeroOrder,
            AvailableTiers = TierOrder,
            ContentHeight = _virtualizer.ContentHeight,
        };
        _view.Refresh(model);
    }

    private void SetStatus(string message)
    {
        _statusMessage = message;
        _statusVisible = true;
    }

    private void ClearStatus()
    {
        _statusMessage = null;
        _statusVisible = false;
    }

    private static string GetSceneToken(Scene scene) =>
        $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";
}
