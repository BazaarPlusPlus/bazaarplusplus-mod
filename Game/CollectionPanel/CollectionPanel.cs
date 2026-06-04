#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using HistoryPanelHost = BazaarPlusPlus.Game.HistoryPanel.HistoryPanel;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanel : MonoBehaviour
{
    private const float CatalogBuildFrameBudgetMs = 4f;

    private static CollectionPanel? _instance;
    public static bool IsVisible => _instance != null && _instance._isVisible;
    internal static CollectionPanel? Instance => _instance;

    private static readonly EHero[] HeroOrder = new[]
    {
        EHero.Common,
        EHero.Vanessa,
        EHero.Dooley,
        EHero.Pygmalien,
        EHero.Karnok,
        EHero.Mak,
        EHero.Stelle,
        EHero.Jules,
    };

    private static readonly ETier[] TierOrder = new[]
    {
        ETier.Bronze,
        ETier.Silver,
        ETier.Gold,
        ETier.Diamond,
        ETier.Legendary,
    };

    private static readonly ECardSize[] SizeOrder = new[]
    {
        ECardSize.Small,
        ECardSize.Medium,
        ECardSize.Large,
    };

    private readonly CollectionCatalog _catalog = new();
    private readonly CollectionFilterState _filter = new();
    private readonly CollectionSourceOfferPoolCache _offerPoolCache = new();

    private IBppConfig _config = null!;
    private CollectionPanelView? _view;
    private CollectionGridOverlay? _overlay;
    private CollectionCardPool? _pool;
    private CollectionCardFactory? _factory;
    private CollectionGridVirtualizer? _virtualizer;
    private CollectionCardArtCache? _artCache;
    private CollectionCardMaterialCache? _materialCache;

    private IBppServices _services = null!;
    private IReadOnlyList<CollectionCardVm> _catalogCards = Array.Empty<CollectionCardVm>();
    private IReadOnlyList<BPPSupporterSample> _supporters = Array.Empty<BPPSupporterSample>();
    private bool _isVisible;
    private bool _initialized;
    private string _lastSceneToken = string.Empty;
    private bool _statusVisible;
    private string? _statusMessage;
    private bool _viewportBoundsDirty;
    private Rect _viewportBoundsPx;
    private float _scrollY;
    private Coroutine? _loadCoroutine;
    private int _loadGeneration;
    private bool _isLoadingCatalog;

    // The run's current day, captured once per open in ResolveOpenSelection (null out of run, or
    // when Data.Run is unreadable). The Day toggle filters by this value, falling back to
    // DayTierSchedule.OutOfRunDay. Recomputed on every open.
    private int? _currentRunDay;

    public void Initialize(IBppServices services)
    {
        if (_initialized)
            return;
        _initialized = true;
        _instance = this;
        _services = services;
        _config = services.Config;
        _lastSceneToken = GetSceneToken(SceneManager.GetActiveScene());
    }

    internal static void NotifyLocaleChanged()
    {
        if (_instance == null)
            return;
        _instance.InvalidateCatalog("locale-change");
        if (_instance._isVisible)
            _instance.StartPanelLoad();
    }

    internal static void OpenFromDockButton()
    {
        if (_instance == null)
        {
            BppLog.Warn("CollectionPanel", "Dock button requested before CollectionPanel mounted.");
            return;
        }
        _instance.Open(_instance.ResolveOpenSelection());
    }

    internal static CollectionPanelSelectionState GetCurrentSelectionState() =>
        _instance?._filter.ToSelectionState() ?? CollectionPanelSelectionState.Default;

    private void Open() => Open(ResolveOpenSelection());

    private CollectionPanelSelectionState ResolveOpenSelection()
    {
        var isInGameRun = IsInGameRunForOpen();
        var hero = isInGameRun ? TryReadCurrentHero() : null;
        var encounterIds = isInGameRun ? TryReadEncounterIds() : EncounterIdsSnapshot.Empty;
        var selection = CollectionPanelOpenSelectionResolver.Resolve(
            isInGameRun,
            hero,
            encounterIds.CurrentEncounterTemplateId,
            encounterIds.ChoiceSelectionTemplateIds,
            CollectionSourceCatalog.Entries
        );

        BppLog.Debug(
            "CollectionPanel",
            "Open selection resolved "
                + $"inRun={isInGameRun} "
                + $"hero={selection.SelectedHero?.ToString() ?? "none"} "
                + $"currentEncounterId={encounterIds.CurrentEncounterId ?? "none"} "
                + $"currentEncounterTemplateId={encounterIds.CurrentEncounterTemplateId?.ToString() ?? "none"} "
                + $"sourceKind={selection.SelectedSourceKind} "
                + $"source={selection.SelectedSourceKey ?? "none"} "
                + $"matched={IsMatchedOpenSelection(selection)}"
        );

        // The day does not change while the panel is open, so capture it once here. Both Open()
        // entry paths call ResolveOpenSelection before applying the selection.
        _currentRunDay = isInGameRun ? TryReadCurrentDay() : null;
        return selection;
    }

    private static bool IsMatchedOpenSelection(CollectionPanelSelectionState selection) =>
        selection.SelectedSourceKind != CollectionSourceKind.Merchant
        || !string.Equals(
            selection.SelectedSourceKey,
            CollectionPanelSelectionState.DefaultMerchantSourceKey,
            StringComparison.Ordinal
        );

    private bool IsInGameRunForOpen()
    {
        try
        {
            return _services.RunContext.IsInGameRun
                || _services.GameStateProbe.ComputeIsInGameRun();
        }
        catch (Exception ex)
        {
            BppLog.Warn("CollectionPanel", $"Open selection run-state read failed: {ex.Message}");
            return _services.RunContext.IsInGameRun;
        }
    }

    private static EHero? TryReadCurrentHero()
    {
        try
        {
            var runHero = TheBazaar.Data.Run?.Player?.Hero;
            if (CollectionPanelOpenSelectionResolver.IsConcreteHero(runHero))
                return runHero;

            var selectedHero = TheBazaar.Data.SelectedHero;
            return CollectionPanelOpenSelectionResolver.IsConcreteHero(selectedHero)
                ? selectedHero
                : null;
        }
        catch (Exception ex)
        {
            BppLog.Warn("CollectionPanel", $"Open selection hero read failed: {ex.Message}");
            return null;
        }
    }

    private static int? TryReadCurrentDay()
    {
        try
        {
            return (int?)TheBazaar.Data.Run?.Day;
        }
        catch (Exception ex)
        {
            BppLog.Warn("CollectionPanel", $"Open selection day read failed: {ex.Message}");
            return null;
        }
    }

    private EncounterIdsSnapshot TryReadEncounterIds()
    {
        try
        {
            return _services.EncounterState.GetEncounterIds();
        }
        catch (Exception ex)
        {
            BppLog.Warn("CollectionPanel", $"Open selection encounter read failed: {ex.Message}");
            return EncounterIdsSnapshot.Empty;
        }
    }

    private void Open(CollectionPanelSelectionState selection)
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

        ApplyOpenSelection(selection);
        // Temporary main-path probe: EnsureView() is heavy one-time UITK construction (visual
        // tree + CJK glyph raster + cold OTF extract) that runs on the click frame BEFORE the
        // panel is shown and is invisible to CollectionPanelLoadDiagnostics (created later in the
        // coroutine). Time the first construction so its click-frame cost is attributable.
        var ensureViewStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var firstViewConstruction = _view == null;
        EnsureView();
        if (firstViewConstruction)
        {
            var ensureViewMs =
                (System.Diagnostics.Stopwatch.GetTimestamp() - ensureViewStartedAt)
                * 1000.0
                / System.Diagnostics.Stopwatch.Frequency;
            BppLog.Info(
                "CollectionPanelLoad",
                "openPrologue ensureView="
                    + ensureViewMs.ToString(
                        "0.0",
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                    + "ms (first view construction)"
            );
        }
        _supporters = BPPSupporters.SampleMany(4);
        _isVisible = true;
        // SetVisible starts the fade-in ramp; overlay activates so its CanvasGroup starts
        // mirroring the view's opacity (Update pushes the live value each frame).
        _view!.SetVisible(true);
        _overlay?.SetVisible(true);
        _overlay?.SetAlpha(_view!.CurrentOpacity);
        StartPanelLoad();
    }

    private void ApplyOpenSelection(CollectionPanelSelectionState selection)
    {
        _filter.ApplySelection(selection);
        // The Day toggle's on/off persists across opens (like the package toggle); when it is on,
        // re-pin it to the freshly-read day. _currentRunDay was just captured in
        // ResolveOpenSelection, which always runs before this.
        if (_filter.SelectedRunDay != null)
            _filter.SelectedRunDay = _currentRunDay ?? DayTierSchedule.OutOfRunDay;
        PruneInvisibleSourceSelections();
        _scrollY = 0f;
    }

    private void Close()
    {
        if (!_isVisible)
            return;
        CancelPanelLoad();
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

        // Opportunistically warm the card catalog off-thread once static data is ready, so the
        // first panel open does not pay the full-table card read (JsonGameDataManager.GetCardMap
        // -> ReadAllCards) on the main thread. The catalog kicks a single shared Task per
        // static-data source; the first open then awaits it instead of blocking.
        if (!_catalog.HasCardMapLoadStarted)
            _catalog.BeginCardMapLoad(out _);

        if (_isVisible && TheBazaar.Data.IsInCombat)
        {
            Close();
            return;
        }

        var dt = Time.unscaledDeltaTime;

        // Drive the panel fade every frame regardless of _isVisible so a Close mid-frame
        // can finish its fade-out animation before we tear runtime down.
        _view?.TickOpacity(dt);
        _view?.TickLoading(dt);
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

        if (_viewportBoundsDirty && _virtualizer != null && _overlay != null)
        {
            _viewportBoundsDirty = false;
            _overlay.SetPosition(_viewportBoundsPx.position);
            _overlay.SetClipSize(_viewportBoundsPx.size);
            _virtualizer.SetViewport(_viewportBoundsPx.width, _viewportBoundsPx.height);
            // The base unit (and therefore ContentHeight) is derived from the viewport width,
            // so re-publish the scroll-spacer height once real bounds arrive — otherwise the
            // first-open estimate computed at the placeholder width leaves the bottom rows
            // unreachable.
            _view?.UpdateContentSpacerHeight(_virtualizer.ContentHeight);
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
        DisposeUnityRuntime();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;
        DisposeRuntime();
    }

    private void DisposeRuntime()
    {
        DisposeUnityRuntime();
        InvalidateCatalog("runtime-dispose");
    }

    private void DisposeUnityRuntime()
    {
        CancelPanelLoad();
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
                PruneInvisibleSourceSelections();
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            },
            toggleHero: hero =>
            {
                _filter.ToggleHero(hero);
                PruneInvisibleSourceSelections();
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
            toggleDayFilter: () =>
            {
                // Toggle whether the day participates in filtering. On uses the current run day
                // (or OutOfRunDay out of run); off clears the day filter entirely.
                _filter.SelectedRunDay = _filter.SelectedRunDay is null
                    ? _currentRunDay ?? DayTierSchedule.OutOfRunDay
                    : (int?)null;
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            },
            toggleSize: size =>
            {
                if (!_filter.Sizes.Remove(size))
                    _filter.Sizes.Add(size);
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            },
            toggleSource: sourceKey =>
            {
                _filter.ToggleSource(_filter.ActiveType, sourceKey);
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            },
            togglePackages: () =>
            {
                _filter.IncludePackages = !_filter.IncludePackages;
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            },
            setSortPriority: priority =>
            {
                if (_filter.SortPriority == priority)
                    return;
                _filter.SortPriority = priority;
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

    private void StartPanelLoad()
    {
        CancelPanelLoad();
        var generation = ++_loadGeneration;
        _loadCoroutine = StartCoroutine(LoadPanelAsync(generation));
    }

    private void CancelPanelLoad()
    {
        _loadGeneration++;
        if (_loadCoroutine != null)
        {
            StopCoroutine(_loadCoroutine);
            _loadCoroutine = null;
        }
        _isLoadingCatalog = false;
    }

    private IEnumerator LoadPanelAsync(int generation)
    {
        var diagnostics = new CollectionPanelLoadDiagnostics();
        _isLoadingCatalog = true;
        SetStatus(CollectionPanelText.CatalogLoading());
        ApplyEmptyVisibleSet();
        RefreshView();

        yield return null;

        if (!IsLoadGenerationCurrent(generation))
            yield break;

        var started = diagnostics.Now();
        CollectionCatalogBuildResult? catalogResult = null;
        if (_catalog.TryGetCached(out var cached))
        {
            catalogResult = cached;
            _catalogCards = cached.Cards;
            ClearStatus();
        }
        else
        {
            // First open (cache miss): acquire the full card map OFF the main thread. The heavy
            // cost is JsonGameDataManager.GetCardMap() -> ReadAllCards (~22 MB full-table SQLite
            // read + polymorphic deserialize); running it synchronously here froze the click path
            // because it sits before the time-sliced Step loop and cannot be preempted. Await the
            // shared prewarm Task (kicked from Update / reused here) while the loading shell +
            // spinner keep animating, then build the catalog from the materialised map.
            var acquireStarted = diagnostics.Now();
            var loadTask = _catalog.BeginCardMapLoad(out var source);
            if (loadTask != null)
            {
                while (!loadTask.IsCompleted)
                {
                    if (!IsLoadGenerationCurrent(generation))
                        yield break;
                    yield return null;
                }
            }
            diagnostics.AddSegment("catalogAcquire", acquireStarted);

            var map = loadTask is { Status: TaskStatus.RanToCompletion } ? loadTask.Result : null;
            if (loadTask is { IsFaulted: true })
            {
                // A faulted Task always carries a non-null AggregateException.
                BppLog.Error(
                    "CollectionPanel",
                    "Off-thread card map load failed.",
                    loadTask.Exception!.GetBaseException()
                );
            }

            if (
                _catalog.TryCreateBuildSession(
                    source,
                    map,
                    out var session,
                    out var unavailableReason
                )
            )
            {
                var buildSession = session!;
                using (buildSession)
                {
                    while (true)
                    {
                        var frameStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                        if (buildSession.Step(() => ShouldPauseCatalogBuild(frameStartedAt)))
                            break;
                        if (!IsLoadGenerationCurrent(generation))
                            yield break;
                        yield return null;
                    }

                    catalogResult = _catalog.Commit(buildSession);
                    _catalogCards = catalogResult.Cards;
                    ClearStatus();
                }
            }
            else
            {
                _catalogCards = Array.Empty<CollectionCardVm>();
                SetStatus(CollectionPanelText.CatalogUnavailable());
                diagnostics.AddValue("unavailableReason", unavailableReason);
            }
        }
        diagnostics.AddSegment("catalog", started);
        if (catalogResult != null)
        {
            diagnostics.AddValue("catalogCacheHit", catalogResult.WasCacheHit ? "true" : "false");
            diagnostics.AddValue("sourceTemplates", catalogResult.SourceTemplateCount);
            diagnostics.AddValue("accepted", catalogResult.AcceptedCount);
            diagnostics.AddValue("rejected", catalogResult.RejectedCount);
        }

        if (!IsLoadGenerationCurrent(generation))
            yield break;

        started = diagnostics.Now();
        ApplyFilters();
        diagnostics.AddSegment("filter", started);

        started = diagnostics.Now();
        _isLoadingCatalog = false;
        if (_catalogCards.Count > 0)
            ClearStatus();
        RefreshView();
        diagnostics.AddSegment("refresh", started);
        diagnostics.AddValue("catalogCards", _catalogCards.Count);
        diagnostics.AddValue("visibleCards", _virtualizer?.VisibleCount ?? 0);
        diagnostics.Log(_catalogCards.Count > 0 ? "loaded" : "unavailable");
        _loadCoroutine = null;
    }

    private bool IsLoadGenerationCurrent(int generation) =>
        generation == _loadGeneration && _isVisible;

    private static bool ShouldPauseCatalogBuild(long startedAt)
    {
        var elapsedMs =
            (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt)
            * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;
        return elapsedMs >= CatalogBuildFrameBudgetMs;
    }

    private void ApplyEmptyVisibleSet()
    {
        if (_virtualizer == null)
            return;

        _virtualizer.SetVisible(Array.Empty<CollectionCardVm>(), _filter.ActiveType);
        _view?.ResetScroll();
        _scrollY = 0f;
    }

    private void ApplyFilters()
    {
        if (_virtualizer == null)
            return;
        if (_catalogCards.Count == 0)
        {
            _virtualizer.SetVisible(Array.Empty<CollectionCardVm>(), _filter.ActiveType);
        }
        else
        {
            var sourceEntry = ResolveSelectedSourceEntry();
            var hasSelectedSource = sourceEntry != null;
            IReadOnlyCollection<Guid>? offeredCardIds = null;
            if (sourceEntry != null)
            {
                var offerPoolResult = _offerPoolCache.GetOrResolve(
                    sourceEntry,
                    _filter.SelectedHero,
                    _catalogCards
                );
                if (offerPoolResult.Status == CollectionSourceOfferPoolStatus.Ready)
                    offeredCardIds = offerPoolResult.OfferedCardIds;
            }

            if (!_isLoadingCatalog)
                ClearStatus();
            var ordered = CollectionFilterEngine.Apply(
                _catalogCards,
                _filter,
                new CollectionFilterContext
                {
                    OfferedCardIds = offeredCardIds,
                    ApplyHeroFilter = !hasSelectedSource || _filter.ActiveType == ECardType.Skill,
                }
            );
            _virtualizer.SetVisible(ordered, _filter.ActiveType);
        }
        ResetVisibleScroll();
    }

    private void RefreshView()
    {
        if (_view == null || _virtualizer == null)
            return;

        var model = new CollectionPanelViewModel
        {
            Title = CollectionPanelText.Title(),
            Subtitle = CollectionPanelText.Subtitle(),
            Supporters = _supporters,
            CountText = CollectionPanelText.MatchCount(_virtualizer.VisibleCount),
            StatusMessage = _statusVisible ? _statusMessage : null,
            IsLoading = _isLoadingCatalog,
            ActiveType = _filter.ActiveType,
            SelectedHeroes = new HashSet<EHero>(_filter.Heroes),
            SelectedTiers = new HashSet<ETier>(_filter.Tiers),
            SelectedSizes = new HashSet<ECardSize>(_filter.Sizes),
            SelectedSourceKey = _filter.GetSelectedSourceKey(_filter.ActiveType),
            IncludePackages = _filter.IncludePackages,
            HasPackages = HasPackages(),
            SourceSelectorEnabled = !_isLoadingCatalog,
            SortPriority = _filter.SortPriority,
            DayFilterActive = _filter.SelectedRunDay != null,
            DayFilterValue = _currentRunDay ?? DayTierSchedule.OutOfRunDay,
            AvailableHeroes = HeroOrder,
            AvailableTiers = TierOrder,
            AvailableSizes = SizeOrder,
            AvailableSources = AvailableSourcesFor(_filter.ActiveType),
            ContentHeight = _virtualizer.ContentHeight,
        };
        _view.Refresh(model);
    }

    private IReadOnlyList<CollectionSourceOptionViewModel> AvailableSourcesFor(ECardType activeType)
    {
        var kind =
            activeType == ECardType.Skill
                ? CollectionSourceKind.Trainer
                : CollectionSourceKind.Merchant;
        var roster = CollectionSourceRoster.Build(
            CollectionSourceCatalog.For(kind, _filter.SelectedHero)
        );
        var result = new List<CollectionSourceOptionViewModel>(roster.Count);
        foreach (var item in roster)
        {
            var entry = item.Entry;
            result.Add(
                new CollectionSourceOptionViewModel
                {
                    SourceKey = entry.SourceKey,
                    DisplayName = entry.Name,
                    Description = entry.Description,
                    Kind = entry.Kind,
                    RepresentativeTemplateId = entry.PortraitTemplateId,
                    BreakAfter = item.BreakAfter,
                }
            );
        }
        return result;
    }

    private bool HasPackages()
    {
        foreach (var card in _catalogCards)
        {
            if (card.IsPackage)
                return true;
        }

        return false;
    }

    private bool PruneInvisibleSourceSelections()
    {
        var selectedHero = _filter.SelectedHero;
        var visibleMerchants = SourceKeysFor(CollectionSourceKind.Merchant, selectedHero);
        var visibleTrainers = SourceKeysFor(CollectionSourceKind.Trainer, selectedHero);
        return _filter.PruneSelectedSources(visibleMerchants, visibleTrainers);
    }

    private static IReadOnlyList<string> SourceKeysFor(
        CollectionSourceKind kind,
        EHero? selectedHero
    )
    {
        var keys = new List<string>();
        foreach (var entry in CollectionSourceCatalog.For(kind, selectedHero))
            keys.Add(entry.SourceKey);
        return keys;
    }

    private CollectionSourceEntry? ResolveSelectedSourceEntry()
    {
        var sourceKey = _filter.GetSelectedSourceKey(_filter.ActiveType);
        if (string.IsNullOrWhiteSpace(sourceKey))
            return null;

        if (!CollectionSourceCatalog.TryGetBySourceKey(sourceKey!, out var entry) || entry == null)
        {
            _filter.ClearSelectedSource(_filter.ActiveType);
            return null;
        }

        var expectedKind =
            _filter.ActiveType == ECardType.Skill
                ? CollectionSourceKind.Trainer
                : CollectionSourceKind.Merchant;
        if (entry.Kind != expectedKind)
        {
            _filter.ClearSelectedSource(_filter.ActiveType);
            return null;
        }

        var selectedHero = _filter.SelectedHero;
        if (selectedHero.HasValue && !entry.AppliesToHero(selectedHero.Value))
        {
            _filter.ClearSelectedSource(_filter.ActiveType);
            return null;
        }

        return entry;
    }

    private void ResetVisibleScroll()
    {
        // Reset the actual ScrollView scrollOffset and cancel any in-flight smooth-wheel
        // animation, otherwise switching from a large set to a small one strands the user
        // at the bottom (scrollOffset clamps to the new max instead of returning to top).
        _view?.ResetScroll();
        _scrollY = 0f;
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

    private void InvalidateCatalog(string reason)
    {
        _catalogCards = Array.Empty<CollectionCardVm>();
        _offerPoolCache.Clear();
        _catalog.InvalidateCache(reason);
    }

    private static string GetSceneToken(Scene scene) =>
        $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";
}
