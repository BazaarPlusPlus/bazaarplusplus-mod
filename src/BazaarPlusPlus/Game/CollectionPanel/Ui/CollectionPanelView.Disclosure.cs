#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed partial class CollectionPanelView
{
    private enum FilterSectionKind
    {
        Hero,
        TierSize,
        Keyword,
        Tag,
        Source,
    }

    private sealed class FilterSectionElements
    {
        public FilterSectionElements(
            VisualElement body,
            Label chevron,
            Label summary,
            Button clearButton
        )
        {
            Body = body;
            Chevron = chevron;
            Summary = summary;
            ClearButton = clearButton;
        }

        public VisualElement Body { get; }
        public Label Chevron { get; }
        public Label Summary { get; }
        public Button ClearButton { get; }
    }

    private readonly Dictionary<FilterSectionKind, FilterSectionElements> _filterSections = new();
    private readonly Dictionary<
        CollectionTabKind,
        Dictionary<FilterSectionKind, bool>
    > _filterSectionExpansion = new();
    private CollectionTabKind _activeDisclosureTab = CollectionTabKind.Items;
    private Label? _activeFiltersLabel;
    private Button? _clearAllFiltersButton;
    private VisualElement? _activeFilterChipRow;
    private Label? _activeFilterEmptyLabel;

    private VisualElement CreateFilterSection(
        VisualElement parent,
        FilterSectionKind kind,
        string title,
        float marginTop,
        Action clearAction,
        Button? modeButton,
        out VisualElement chipRow,
        out Label label
    )
    {
        var section = new VisualElement();
        section.style.flexDirection = FlexDirection.Column;
        section.style.flexShrink = 0f;
        section.style.marginTop = marginTop;
        section.style.borderTopWidth = Borders.Thin;
        section.style.borderTopColor = Colors.HistoryButtonBorder;
        section.style.paddingTop = UiSpacing.Sm;
        parent.Add(section);

        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.alignSelf = Align.Stretch;
        section.Add(headerRow);

        var toggleButton = new Button(() => ToggleFilterSection(kind));
        toggleButton.style.height = Sizes.ButtonStandardHeight;
        toggleButton.style.flexGrow = 1f;
        toggleButton.style.flexShrink = 1f;
        toggleButton.style.minWidth = 0f;
        toggleButton.style.flexDirection = FlexDirection.Row;
        toggleButton.style.alignItems = Align.Center;
        toggleButton.style.justifyContent = Justify.FlexStart;
        toggleButton.style.backgroundColor = Color.clear;
        toggleButton.style.borderLeftWidth = 0f;
        toggleButton.style.borderRightWidth = 0f;
        toggleButton.style.borderTopWidth = 0f;
        toggleButton.style.borderBottomWidth = 0f;
        UiStyle.Padding(toggleButton.style, UiSpacing.None);
        headerRow.Add(toggleButton);

        var chevron = CreateLabel(
            Sizes.CollectionFilterHeaderFont,
            FontStyle.Bold,
            Colors.HistorySubtitleText
        );
        chevron.pickingMode = PickingMode.Ignore;
        chevron.style.width = 18f;
        chevron.style.flexShrink = 0f;
        toggleButton.Add(chevron);

        label = CreateLabel(
            Sizes.CollectionFilterHeaderFont,
            FontStyle.Bold,
            Colors.HistorySubtitleText
        );
        label.pickingMode = PickingMode.Ignore;
        label.text = title;
        label.style.flexShrink = 0f;
        toggleButton.Add(label);

        var summary = CreateLabel(
            Sizes.CollectionFilterFont,
            FontStyle.Normal,
            Colors.HistoryStatusText
        );
        summary.pickingMode = PickingMode.Ignore;
        summary.style.flexGrow = 1f;
        summary.style.flexShrink = 1f;
        summary.style.minWidth = 0f;
        summary.style.marginLeft = UiSpacing.Sm;
        summary.style.whiteSpace = WhiteSpace.NoWrap;
        summary.style.overflow = Overflow.Hidden;
        toggleButton.Add(summary);

        if (modeButton != null)
            headerRow.Add(modeButton);

        var clearButton = CreateButton(
            CollectionPanelText.Clear(),
            clearAction,
            Sizes.CollectionFilterClearButtonWidth,
            Sizes.CollectionFacetChipHeight
        );
        clearButton.style.marginLeft = UiSpacing.Sm;
        clearButton.style.display = DisplayStyle.None;
        StyleButton(clearButton, Colors.HistoryChipBackground, Colors.HistoryStatusText);
        headerRow.Add(clearButton);

        var body = new VisualElement();
        body.style.flexDirection = FlexDirection.Column;
        body.style.flexShrink = 0f;
        body.style.marginTop = UiSpacing.Sm;
        section.Add(body);

        chipRow = new VisualElement();
        chipRow.style.flexDirection = FlexDirection.Row;
        chipRow.style.flexWrap = Wrap.Wrap;
        chipRow.style.alignItems = Align.Center;
        chipRow.style.alignSelf = Align.Stretch;
        body.Add(chipRow);

        _filterSections[kind] = new FilterSectionElements(body, chevron, summary, clearButton);
        ApplyFilterSectionExpansion(kind);
        return section;
    }

    private void BuildActiveFilterSummary(VisualElement parent)
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Column;
        container.style.flexShrink = 0f;
        container.style.marginTop = UiSpacing.Lg;
        parent.Add(container);

        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        container.Add(header);

        _activeFiltersLabel = CreateLabel(
            Sizes.CollectionFilterHeaderFont,
            FontStyle.Bold,
            Colors.HistorySubtitleText
        );
        _activeFiltersLabel.style.flexGrow = 1f;
        header.Add(_activeFiltersLabel);

        _clearAllFiltersButton = CreateButton(
            CollectionPanelText.ClearAll(),
            _commands.ClearAllFilters,
            Sizes.CloseButtonWidth,
            Sizes.CollectionFacetChipHeight
        );
        StyleButton(_clearAllFiltersButton, Colors.HistoryChipBackground, Colors.HistoryStatusText);
        header.Add(_clearAllFiltersButton);

        _activeFilterChipRow = new VisualElement();
        _activeFilterChipRow.style.flexDirection = FlexDirection.Row;
        _activeFilterChipRow.style.flexWrap = Wrap.Wrap;
        _activeFilterChipRow.style.alignItems = Align.Center;
        _activeFilterChipRow.style.marginTop = UiSpacing.Xs;
        _activeFilterChipRow.style.maxHeight = Sizes.CollectionActiveFilterHeight * 2f;
        _activeFilterChipRow.style.overflow = Overflow.Hidden;
        container.Add(_activeFilterChipRow);

        _activeFilterEmptyLabel = CreateLabel(
            Sizes.CollectionFilterFont,
            FontStyle.Normal,
            Colors.HistoryStatusText
        );
        _activeFilterChipRow.Add(_activeFilterEmptyLabel);
    }

    private void ToggleFilterSection(FilterSectionKind kind)
    {
        var state = ExpansionFor(_activeDisclosureTab);
        state[kind] = !IsFilterSectionExpanded(kind);
        ApplyFilterSectionExpansion(kind);
    }

    private void SetDisclosureTab(CollectionTabKind tab)
    {
        if (_activeDisclosureTab == tab)
            return;
        _activeDisclosureTab = tab;
        foreach (var kind in _filterSections.Keys)
            ApplyFilterSectionExpansion(kind);
    }

    private bool IsFilterSectionExpanded(FilterSectionKind kind)
    {
        var state = ExpansionFor(_activeDisclosureTab);
        if (state.TryGetValue(kind, out var expanded))
            return expanded;
        expanded = kind is FilterSectionKind.Hero or FilterSectionKind.TierSize;
        state[kind] = expanded;
        return expanded;
    }

    private Dictionary<FilterSectionKind, bool> ExpansionFor(CollectionTabKind tab)
    {
        if (_filterSectionExpansion.TryGetValue(tab, out var state))
            return state;
        state = new Dictionary<FilterSectionKind, bool>();
        _filterSectionExpansion[tab] = state;
        return state;
    }

    private void ApplyFilterSectionExpansion(FilterSectionKind kind)
    {
        if (!_filterSections.TryGetValue(kind, out var section))
            return;
        var expanded = IsFilterSectionExpanded(kind);
        section.Chevron.text = expanded ? "▼" : "▶";
        section.Body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void RefreshFilterDisclosure(CollectionPanelViewModel model)
    {
        SetDisclosureTab(model.ActiveTab);
        RefreshFilterSection(
            FilterSectionKind.Hero,
            Summary(
                model
                    .AvailableHeroes.Where(model.SelectedHeroes.Contains)
                    .Select(CollectionPanelText.Hero)
            ),
            model.SelectedHeroes.Count > 0
        );
        RefreshFilterSection(
            FilterSectionKind.TierSize,
            Summary(
                VisibleSelectedSizes(model)
                    .Select(CollectionPanelText.Size)
                    .Concat(
                        model
                            .AvailableTiers.Where(model.SelectedTiers.Contains)
                            .Select(CollectionPanelText.Tier)
                    )
            ),
            VisibleSelectedSizes(model).Any() || model.SelectedTiers.Count > 0
        );
        RefreshFilterSection(
            FilterSectionKind.Keyword,
            Summary(
                model
                    .AvailableKeywords.Where(model.SelectedKeywords.Contains)
                    .Select(keyword => ResolveTagDisplay(keyword).Label)
            ),
            model.SelectedKeywords.Count > 0
        );
        RefreshFilterSection(
            FilterSectionKind.Tag,
            Summary(
                model
                    .AvailableTags.Where(model.SelectedTags.Contains)
                    .Select(tag => ResolveTagDisplay(tag).Label)
            ),
            model.SelectedTags.Count > 0
        );
        RefreshFilterSection(
            FilterSectionKind.Source,
            SelectedSourceName(model),
            !string.IsNullOrWhiteSpace(model.SelectedSourceKey)
        );

        if (_tagMatchModeButton != null)
            _tagMatchModeButton.style.display =
                model.SelectedTags.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (_keywordMatchModeButton != null)
            _keywordMatchModeButton.style.display =
                model.SelectedKeywords.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void RefreshFilterSection(FilterSectionKind kind, string summary, bool canClear)
    {
        if (!_filterSections.TryGetValue(kind, out var section))
            return;
        section.Summary.text = summary;
        section.Summary.tooltip = summary;
        section.ClearButton.text = CollectionPanelText.Clear();
        section.ClearButton.style.display = canClear ? DisplayStyle.Flex : DisplayStyle.None;
        ApplyFilterSectionExpansion(kind);
    }

    private void RefreshActiveFilterSummary(CollectionPanelViewModel model)
    {
        if (_activeFilterChipRow == null)
            return;

        _activeFiltersLabel!.text = CollectionPanelText.ActiveFilters();
        _activeFilterChipRow.Clear();
        var count = 0;

        if (!string.IsNullOrWhiteSpace(model.SearchQuery))
        {
            AddActiveFilterChip(
                $"{CollectionPanelText.SearchLabel()}: {model.SearchQuery}",
                () => _commands.SetSearchQuery(string.Empty)
            );
            count++;
        }
        foreach (var hero in model.AvailableHeroes.Where(model.SelectedHeroes.Contains))
        {
            var captured = hero;
            AddActiveFilterChip(
                CollectionPanelText.Hero(hero),
                () => _commands.ToggleHero(captured)
            );
            count++;
        }
        if (model.DayFilterActive && model.DayFilterValue.HasValue)
        {
            AddActiveFilterChip(
                CollectionPanelText.DayValue(model.DayFilterValue),
                _commands.ToggleRunDayFilter
            );
            count++;
        }
        foreach (var size in VisibleSelectedSizes(model))
        {
            var captured = size;
            AddActiveFilterChip(
                CollectionPanelText.Size(size),
                () => _commands.ToggleSize(captured)
            );
            count++;
        }
        foreach (var tier in model.AvailableTiers.Where(model.SelectedTiers.Contains))
        {
            var captured = tier;
            AddActiveFilterChip(
                CollectionPanelText.Tier(tier),
                () => _commands.ToggleTier(captured)
            );
            count++;
        }
        foreach (var keyword in model.AvailableKeywords.Where(model.SelectedKeywords.Contains))
        {
            var captured = keyword;
            AddActiveFilterChip(
                ResolveTagDisplay(keyword).Label,
                () => _commands.ToggleKeyword(captured)
            );
            count++;
        }
        foreach (
            var tag in model.TabProfile.ShowTagFilter
                ? model.AvailableTags.Where(model.SelectedTags.Contains)
                : Enumerable.Empty<ECardTag>()
        )
        {
            var captured = tag;
            AddActiveFilterChip(ResolveTagDisplay(tag).Label, () => _commands.ToggleTag(captured));
            count++;
        }
        if (!string.IsNullOrWhiteSpace(model.SelectedSourceKey))
        {
            AddActiveFilterChip(SelectedSourceName(model), _commands.ClearSourceFilter);
            count++;
        }

        if (count == 0)
        {
            _activeFilterEmptyLabel!.text = CollectionPanelText.Unlimited();
            _activeFilterChipRow.Add(_activeFilterEmptyLabel);
        }
        _clearAllFiltersButton!.text = CollectionPanelText.ClearAll();
        _clearAllFiltersButton.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void AddActiveFilterChip(string text, Action onClick)
    {
        var chip = CreateButton(
            $"{StablePanelText.Compact(text, 28)}  ×",
            onClick,
            0f,
            Sizes.CollectionFacetChipHeight,
            fixedWidth: false
        );
        chip.style.minWidth = Sizes.InfoChipMinWidth;
        chip.style.marginRight = UiSpacing.Sm;
        chip.style.marginBottom = UiSpacing.Xs;
        chip.tooltip = text;
        UiStyle.HorizontalPadding(chip.style, UiSpacing.Md);
        StyleButton(chip, Colors.ButtonSelectedBackground, Colors.ButtonSelectedText);
        _activeFilterChipRow!.Add(chip);
    }

    private static string Summary(IEnumerable<string> values)
    {
        var labels = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (labels.Length == 0)
            return CollectionPanelText.Unlimited();
        if (labels.Length <= 2)
            return string.Join(" · ", labels);
        return CollectionPanelText.SelectedCount(labels.Length);
    }

    private static string SelectedSourceName(CollectionPanelViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.SelectedSourceKey))
            return CollectionPanelText.Unlimited();
        foreach (var source in model.AvailableSources)
            if (string.Equals(source.SourceKey, model.SelectedSourceKey, StringComparison.Ordinal))
                return source.DisplayName;
        return CollectionPanelText.SelectedCount(1);
    }

    private static IEnumerable<ECardSize> VisibleSelectedSizes(CollectionPanelViewModel model) =>
        model.TabProfile.ShowSizeFilter
            ? model.AvailableSizes.Where(model.SelectedSizes.Contains)
            : Enumerable.Empty<ECardSize>();
}
