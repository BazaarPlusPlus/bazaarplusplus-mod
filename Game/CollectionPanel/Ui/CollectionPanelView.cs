#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed class CollectionPanelViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public IReadOnlyList<BPPSupporterSample> Supporters { get; set; } =
        Array.Empty<BPPSupporterSample>();
    public string CountText { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public bool IsLoading { get; set; }
    public ECardType ActiveType { get; set; } = ECardType.Item;
    public HashSet<EHero> SelectedHeroes { get; set; } = new();
    public HashSet<ETier> SelectedTiers { get; set; } = new();
    public HashSet<ECardSize> SelectedSizes { get; set; } = new();
    public string? SelectedSourceKey { get; set; }
    public bool IncludePackages { get; set; }
    public bool ShowPackageToggle { get; set; } = true;
    public bool SourceSelectorEnabled { get; set; } = true;
    public CollectionSortPriority SortPriority { get; set; } = CollectionSortPriority.Quality;

    // Day filter icon: DayFilterValue is the number shown (current run day, or OutOfRunDay);
    // DayFilterActive highlights it when the day participates in filtering.
    public bool DayFilterActive { get; set; }
    public int DayFilterValue { get; set; }
    public IReadOnlyList<EHero> AvailableHeroes { get; set; } = Array.Empty<EHero>();
    public IReadOnlyList<ETier> AvailableTiers { get; set; } = Array.Empty<ETier>();
    public IReadOnlyList<ECardSize> AvailableSizes { get; set; } = Array.Empty<ECardSize>();
    public IReadOnlyList<CollectionSourceOptionViewModel> AvailableSources { get; set; } =
        Array.Empty<CollectionSourceOptionViewModel>();
    public float ContentHeight { get; set; }
}

internal sealed class CollectionSourceOptionViewModel
{
    public string SourceKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public CollectionSourceKind Kind { get; init; }
    public Guid RepresentativeTemplateId { get; init; }
    public bool BreakAfter { get; init; }
}

internal sealed partial class CollectionPanelView : IDisposable
{
    private const string SourceChipInitialsName = "bpp-source-chip-initials";

    private readonly Transform _parent;
    private readonly Action _close;
    private readonly Action<ECardType> _setActiveType;
    private readonly Action<EHero> _toggleHero;
    private readonly Action<ETier> _toggleTier;
    private readonly Action _toggleDayFilter;
    private readonly Action<ECardSize> _toggleSize;
    private readonly Action<string> _toggleSource;
    private readonly Action _togglePackages;
    private readonly Action<CollectionSortPriority> _setSortPriority;

    private GameObject? _rootObject;
    private UIDocument? _document;
    private PanelSettings? _panelSettings;
    private VisualElement? _root;
    private Label? _title;
    private VisualElement? _subtitle;
    private Label? _countLabel;
    private Label? _statusLabel;
    private Button? _itemTabButton;
    private Button? _skillTabButton;
    private Button? _closeButton;
    private Button? _packageToggleButton;
    private Button? _dayToggleButton;
    private Button? _sortQualityButton;
    private Button? _sortSizeButton;
    private VisualElement? _heroChipRow;
    private VisualElement? _tierChipRow;
    private VisualElement? _sizeChipRow;
    private Label? _sourceFilterLabel;
    private VisualElement? _sourceChipRow;
    private VisualElement? _sizeFilterSection;
    private VisualElement? _sourceFilterSection;
    private VisualElement? _gridViewport;
    private ScrollView? _gridScrollView;
    private VisualElement? _gridContentSpacer;
    private Label? _emptyLabel;
    private Label? _loadingLabel;
    private bool _loadingVisible;
    private string _loadingMessage = string.Empty;
    private float _loadingFrameElapsed;
    private int _loadingFrameIndex;

    private readonly Dictionary<EHero, Button> _heroChips = new();
    private readonly Dictionary<EHero, VisualElement> _heroChipIcons = new();
    private readonly Dictionary<ETier, Button> _tierChips = new();
    private readonly Dictionary<ECardSize, Button> _sizeChips = new();
    private readonly Dictionary<string, Button> _sourceChips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VisualElement> _sourceChipIcons = new(
        StringComparer.Ordinal
    );
    private readonly List<string> _sourceChipOrder = new();
    private Rect _lastGridBounds;
    private float _appliedSourceChipBox = -1f;

    // Panel-open/-close fade state. _opacity is the displayed alpha, _targetOpacity is what
    // SetVisible asked for; TickOpacity ramps the first toward the second with an
    // exponential lerp whose time constant is direction-dependent (in vs. out).
    private float _opacity;
    private float _targetOpacity;

    private static readonly string[] LoadingFrames = { "|", "/", "-", "\\" };

    public event Action<Rect>? GridViewportBoundsChanged;

    public CollectionPanelView(
        Transform parent,
        Action close,
        Action<ECardType> setActiveType,
        Action<EHero> toggleHero,
        Action<ETier> toggleTier,
        Action toggleDayFilter,
        Action<ECardSize> toggleSize,
        Action<string> toggleSource,
        Action togglePackages,
        Action<CollectionSortPriority> setSortPriority
    )
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _setActiveType = setActiveType ?? throw new ArgumentNullException(nameof(setActiveType));
        _toggleHero = toggleHero ?? throw new ArgumentNullException(nameof(toggleHero));
        _toggleTier = toggleTier ?? throw new ArgumentNullException(nameof(toggleTier));
        _toggleDayFilter =
            toggleDayFilter ?? throw new ArgumentNullException(nameof(toggleDayFilter));
        _toggleSize = toggleSize ?? throw new ArgumentNullException(nameof(toggleSize));
        _toggleSource = toggleSource ?? throw new ArgumentNullException(nameof(toggleSource));
        _togglePackages = togglePackages ?? throw new ArgumentNullException(nameof(togglePackages));
        _setSortPriority =
            setSortPriority ?? throw new ArgumentNullException(nameof(setSortPriority));
    }

    public void EnsureCreated()
    {
        if (_rootObject != null)
            return;

        _rootObject = new GameObject("CollectionPanelUiToolkitRoot");
        _rootObject.transform.SetParent(_parent, false);
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.sortingOrder = 26;
        _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        _panelSettings.match = 1f;
        _panelSettings.clearColor = false;
        _panelSettings.targetDisplay = 0;

        _document = _rootObject.AddComponent<UIDocument>();
        _document.panelSettings = _panelSettings;
        _root = _document.rootVisualElement;
        _root.style.flexGrow = 1f;
        _root.style.position = Position.Absolute;
        _root.style.left = 0f;
        _root.style.right = 0f;
        _root.style.top = 0f;
        _root.style.bottom = 0f;
        _root.style.display = DisplayStyle.None;
        BppUiFont.RequestCharactersInTexture(
            CollectionPanelText.Title()
                + CollectionPanelText.Subtitle()
                + CollectionPanelText.ItemsTab()
                + CollectionPanelText.SkillsTab()
                + CollectionPanelText.Close()
                + CollectionPanelText.PackagesToggle()
                + CollectionPanelText.SortHeader()
                + CollectionPanelText.SortQuality()
                + CollectionPanelText.SortSize()
                + CollectionPanelText.NoMatches()
                + CollectionPanelText.DayHeader()
                + "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ -_:/?()[]%+,.!|#\\",
            Sizes.FontButton,
            FontStyle.Normal
        );
        _root.style.unityFont = BppUiFont.Default;
        _root.pickingMode = PickingMode.Position;

        BuildTree(_root);

        _gridViewport?.RegisterCallback<GeometryChangedEvent>(OnGridViewportGeometryChanged);
    }

    public void SetVisible(bool visible)
    {
        _targetOpacity = visible ? 1f : 0f;
        if (_root == null)
            return;
        if (visible)
        {
            // Show the element synchronously so the first ramp frame paints; opacity starts
            // wherever the previous tick left it (0 on first open, mid-fade if Close was
            // pressed during an in-flight open animation).
            _root.style.display = DisplayStyle.Flex;
            _root.style.opacity = _opacity;
        }
    }

    public float CurrentOpacity => _opacity;

    // True until the fade animation has settled and the root has finished hiding (if the
    // target was 0). CollectionPanel uses this to defer overlay deactivation + virtualizer
    // disposal until the visual fade-out is complete.
    public bool IsFadingOrVisible => _targetOpacity > 0f || _opacity > 0.005f;

    public void TickOpacity(float deltaSeconds)
    {
        if (_root == null || deltaSeconds <= 0f)
            return;
        if (Mathf.Approximately(_opacity, _targetOpacity))
        {
            if (_targetOpacity <= 0f && _root.style.display.value != DisplayStyle.None)
                _root.style.display = DisplayStyle.None;
            return;
        }
        // Asymmetric tau: open is a presentation (slower, more deliberate); close is a
        // dismissal (snappier, the user just wants the panel gone).
        var tau =
            _targetOpacity > _opacity
                ? CollectionGridConstants.PanelFadeInSeconds
                : CollectionGridConstants.PanelFadeOutSeconds;
        var t = 1f - Mathf.Exp(-deltaSeconds / tau);
        _opacity = Mathf.Lerp(_opacity, _targetOpacity, t);
        if (Mathf.Abs(_opacity - _targetOpacity) < 0.005f)
            _opacity = _targetOpacity;
        _root.style.opacity = _opacity;
        if (_targetOpacity <= 0f && _opacity <= 0.005f)
            _root.style.display = DisplayStyle.None;
    }

    public void TickLoading(float deltaSeconds)
    {
        if (!_loadingVisible || _loadingLabel == null)
            return;

        _loadingFrameElapsed += Mathf.Max(0f, deltaSeconds);
        if (_loadingFrameElapsed < 0.16f)
            return;

        _loadingFrameElapsed = 0f;
        _loadingFrameIndex = (_loadingFrameIndex + 1) % LoadingFrames.Length;
        UpdateLoadingLabelText();
    }

    private void UpdateLoadingLabelText()
    {
        if (_loadingLabel == null)
            return;

        var message = string.IsNullOrWhiteSpace(_loadingMessage)
            ? CollectionPanelText.CatalogLoading()
            : _loadingMessage;
        _loadingLabel.text = $"{LoadingFrames[_loadingFrameIndex]} {message}";
    }

    // Snap the ScrollView to the top. Called on filter / tab changes so the user is not
    // stranded at the bottom of a tiny new visible set — the previous scrollOffset would
    // otherwise clamp to the new (smaller) max instead of returning to top.
    public void ResetScroll()
    {
        if (_gridScrollView != null)
            _gridScrollView.scrollOffset = new Vector2(_gridScrollView.scrollOffset.x, 0f);
    }

    public void Refresh(CollectionPanelViewModel model)
    {
        if (_root == null)
            return;

        _title!.text = model.Title;
        BPPSupporterAttributionRow.Bind(_subtitle!, model.Supporters, model.Subtitle);
        _countLabel!.text = model.CountText;
        _statusLabel!.text = model.StatusMessage ?? string.Empty;
        _statusLabel.style.display = string.IsNullOrWhiteSpace(model.StatusMessage)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
        _loadingVisible = model.IsLoading;
        _loadingMessage = model.StatusMessage ?? CollectionPanelText.CatalogLoading();
        if (_loadingLabel != null)
        {
            _loadingLabel.style.display = model.IsLoading ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateLoadingLabelText();
        }

        RefreshTabButton(_itemTabButton!, model.ActiveType == ECardType.Item);
        RefreshTabButton(_skillTabButton!, model.ActiveType == ECardType.Skill);

        EnsureHeroChips(model.AvailableHeroes);
        EnsureTierChips(model.AvailableTiers);
        EnsureSizeChips(model.AvailableSizes);
        EnsureSourceChips(model.AvailableSources);
        foreach (var pair in _heroChips)
            RefreshHeroChip(pair.Key, pair.Value, model.SelectedHeroes.Contains(pair.Key));
        foreach (var pair in _tierChips)
            RefreshChip(pair.Value, model.SelectedTiers.Contains(pair.Key));
        foreach (var pair in _sizeChips)
            RefreshChip(pair.Value, model.SelectedSizes.Contains(pair.Key));
        foreach (var pair in _sourceChips)
        {
            RefreshChip(
                pair.Value,
                string.Equals(pair.Key, model.SelectedSourceKey, StringComparison.Ordinal)
            );
            pair.Value.SetEnabled(model.SourceSelectorEnabled);
            pair.Value.style.opacity = model.SourceSelectorEnabled ? 1f : 0.58f;
        }
        if (_packageToggleButton != null)
            RefreshPackageToggle(model.IncludePackages, model.ShowPackageToggle);
        if (_dayToggleButton != null)
            RefreshDayToggle(model.DayFilterValue, model.DayFilterActive);
        if (_sortQualityButton != null)
            RefreshChip(_sortQualityButton, model.SortPriority == CollectionSortPriority.Quality);
        if (_sortSizeButton != null)
            RefreshChip(_sortSizeButton, model.SortPriority == CollectionSortPriority.Size);

        // Size only narrows Items; keep the section's reserved layout slot on Skills.
        if (_sizeFilterSection != null)
            _sizeFilterSection.style.display = DisplayStyle.Flex;
        if (_sizeChipRow != null)
        {
            var showSizeChips = model.ActiveType == ECardType.Item;
            _sizeChipRow.style.visibility = showSizeChips ? Visibility.Visible : Visibility.Hidden;
            _sizeChipRow.SetEnabled(showSizeChips);
            _sizeChipRow.pickingMode = showSizeChips ? PickingMode.Position : PickingMode.Ignore;
        }
        if (_sourceFilterSection != null)
            _sourceFilterSection.style.display =
                model.AvailableSources.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (_sourceFilterLabel != null)
            _sourceFilterLabel.text = CollectionPanelText.SourceHeader(model.ActiveType);

        UpdateContentSpacerHeight(model.ContentHeight);

        if (_emptyLabel != null)
        {
            var showEmpty =
                model.ContentHeight <= 0f
                && string.IsNullOrWhiteSpace(model.StatusMessage)
                && !model.IsLoading;
            _emptyLabel.style.display = showEmpty ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    public void UpdateContentSpacerHeight(float contentHeightPixels)
    {
        if (_gridContentSpacer == null || _gridViewport == null)
            return;
        // ContentHeight is in overlay physical pixels, but the spacer height is UITK points and
        // ReadScrollYPixels multiplies scrollOffset (points) by scaledPixelsPerPoint to recover
        // the pixel offset the virtualizer consumes. So the spacer must be points = px / ppp, or
        // the scroll range and the card content diverge on any non-1.0 UI scale (1440p/4K), which
        // both strands the bottom rows and lets the view over-scroll into empty space. At ppp = 1
        // (1080p with the 1080 reference) this is a no-op.
        var ppp = _gridViewport.scaledPixelsPerPoint;
        if (ppp <= 0f)
            ppp = 1f;
        var height = Mathf.Max(0f, contentHeightPixels / ppp);
        _gridContentSpacer.style.height = height;
        _gridContentSpacer.style.minHeight = height;
    }

    public float ReadScrollYPixels()
    {
        if (_gridScrollView == null || _gridViewport == null)
            return 0f;
        return _gridScrollView.scrollOffset.y * _gridViewport.scaledPixelsPerPoint;
    }

    public void Dispose()
    {
        if (_rootObject != null)
            UnityEngine.Object.Destroy(_rootObject);
        if (_panelSettings != null)
            UnityEngine.Object.Destroy(_panelSettings);
        _rootObject = null;
        _document = null;
        _panelSettings = null;
        _root = null;
    }

    private void OnGridViewportGeometryChanged(GeometryChangedEvent evt)
    {
        if (_gridViewport == null)
            return;
        var worldBound = _gridViewport.worldBound;
        var ppp = _gridViewport.scaledPixelsPerPoint;
        var bounds = new Rect(
            Mathf.Round(worldBound.x * ppp),
            Mathf.Round(Screen.height - worldBound.yMax * ppp),
            Mathf.Max(1f, Mathf.Round(worldBound.width * ppp)),
            Mathf.Max(1f, Mathf.Round(worldBound.height * ppp))
        );
        if (bounds.width <= 0f || bounds.height <= 0f)
            return;
        if (RectApproximately(_lastGridBounds, bounds))
            return;
        _lastGridBounds = bounds;
        GridViewportBoundsChanged?.Invoke(bounds);
    }

    private static bool RectApproximately(Rect left, Rect right) =>
        Mathf.Approximately(left.x, right.x)
        && Mathf.Approximately(left.y, right.y)
        && Mathf.Approximately(left.width, right.width)
        && Mathf.Approximately(left.height, right.height);
}
