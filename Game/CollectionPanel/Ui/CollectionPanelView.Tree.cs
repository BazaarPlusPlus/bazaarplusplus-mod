#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed partial class CollectionPanelView
{
    private void BuildTree(VisualElement root)
    {
        var panel = new VisualElement();
        panel.style.flexGrow = 1f;
        panel.style.backgroundColor = Colors.HistoryPanelBackground;
        panel.style.paddingLeft = UiSpacing.PanelPadding;
        panel.style.paddingRight = UiSpacing.PanelPadding;
        panel.style.paddingTop = UiSpacing.PanelPadding;
        panel.style.paddingBottom = UiSpacing.Xxl;
        panel.style.flexDirection = FlexDirection.Row;
        root.Add(panel);

        BuildGrid(panel);
        BuildOperationRail(panel);
    }

    private void BuildOperationRail(VisualElement parent)
    {
        var rail = new VisualElement();
        rail.style.flexDirection = FlexDirection.Column;
        rail.style.flexGrow = 0f;
        rail.style.flexShrink = 0f;
        rail.style.flexBasis = Length.Percent(Sizes.OperationRailWidthPercent);
        rail.style.minWidth = Sizes.OperationRailMinWidth;
        rail.style.maxWidth = Sizes.OperationRailMaxWidth;
        rail.style.marginLeft = UiSpacing.ColumnGap;
        parent.Add(rail);

        // Title + Close (Close lives here in the operation area, not a top bar).
        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        rail.Add(titleRow);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Bold, Colors.HistoryTitleText);
        _title.style.flexGrow = 1f;
        _title.style.flexShrink = 1f;
        titleRow.Add(_title);

        _closeButton = CreateButton(
            CollectionPanelText.Close(),
            _close,
            Sizes.CloseButtonWidth,
            Sizes.ButtonStandardHeight
        );
        StyleButton(_closeButton, Colors.CloseBackground, Colors.CloseText);
        titleRow.Add(_closeButton);

        _subtitle = BPPSupporterAttributionRow.Create();
        rail.Add(_subtitle);

        // Item / Skill tabs.
        var tabsRow = new VisualElement();
        tabsRow.style.flexDirection = FlexDirection.Row;
        tabsRow.style.alignItems = Align.Center;
        tabsRow.style.flexWrap = Wrap.Wrap;
        tabsRow.style.marginTop = UiSpacing.Xl;
        rail.Add(tabsRow);

        _itemTabButton = CreateButton(
            CollectionPanelText.ItemsTab(),
            () => _setActiveType(ECardType.Item),
            Sizes.RunsTabWidth,
            Sizes.ButtonStandardHeight
        );
        _skillTabButton = CreateButton(
            CollectionPanelText.SkillsTab(),
            () => _setActiveType(ECardType.Skill),
            Sizes.RunsTabWidth,
            Sizes.ButtonStandardHeight
        );
        tabsRow.Add(_itemTabButton);
        _skillTabButton.style.marginLeft = UiSpacing.Md;
        tabsRow.Add(_skillTabButton);

        var tabsSpacer = new VisualElement();
        tabsSpacer.style.flexGrow = 1f;
        tabsSpacer.style.flexShrink = 1f;
        tabsSpacer.style.minWidth = UiSpacing.Md;
        tabsRow.Add(tabsSpacer);

        var sortGroup = new VisualElement();
        sortGroup.style.flexDirection = FlexDirection.Row;
        sortGroup.style.alignItems = Align.Center;
        sortGroup.style.flexShrink = 0f;
        sortGroup.style.marginLeft = UiSpacing.Md;
        tabsRow.Add(sortGroup);

        var sortLabel = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistorySubtitleText);
        sortLabel.text = CollectionPanelText.SortHeader();
        sortLabel.style.marginRight = UiSpacing.Xs;
        sortGroup.Add(sortLabel);

        _sortQualityButton = CreateInlineSortButton(
            CollectionPanelText.SortQuality(),
            () => _setSortPriority(CollectionSortPriority.Quality)
        );
        _sortSizeButton = CreateInlineSortButton(
            CollectionPanelText.SortSize(),
            () => _setSortPriority(CollectionSortPriority.Size)
        );
        sortGroup.Add(_sortQualityButton);
        _sortSizeButton.style.marginLeft = UiSpacing.Xs;
        sortGroup.Add(_sortSizeButton);

        _countLabel = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistoryStatusText);
        _countLabel.style.backgroundColor = Colors.HistoryStatusBackground;
        _countLabel.style.height = Sizes.ButtonCompactHeight;
        _countLabel.style.minWidth = Sizes.ChipMinWidth;
        _countLabel.style.flexShrink = 0f;
        _countLabel.style.marginLeft = UiSpacing.Md;
        _countLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _countLabel.style.alignSelf = Align.Center;
        UiStyle.HorizontalPadding(_countLabel.style, UiSpacing.Md);
        UiStyle.Radius(_countLabel.style, Radii.Md);
        UiStyle.Border(_countLabel.style, Borders.Thin, Colors.HistoryStatusBorder);
        tabsRow.Add(_countLabel);

        var toolsRow = new VisualElement();
        toolsRow.style.flexDirection = FlexDirection.Row;
        toolsRow.style.alignItems = Align.Center;
        toolsRow.style.flexWrap = Wrap.NoWrap;
        toolsRow.style.marginTop = UiSpacing.Md;
        rail.Add(toolsRow);

        _searchShell = CreateSearchShell();
        toolsRow.Add(_searchShell);

        _clearButton = CreateButton(
            CollectionPanelText.Reset(),
            _clearFilters,
            Sizes.SearchResetButtonWidth,
            Sizes.ButtonStandardHeight
        );
        _clearButton.style.marginLeft = UiSpacing.Sm;
        toolsRow.Add(_clearButton);

        _packageToggleButton = CreatePackageToggleButton();
        _packageToggleButton.style.marginLeft = UiSpacing.Sm;
        toolsRow.Add(_packageToggleButton);

        // Hero filter.
        CreateFilterSection(rail, CollectionPanelText.HeroHeader(), UiSpacing.Xl, out _heroChipRow);
        _heroChipRow.style.flexWrap = Wrap.NoWrap;
        _heroChipRow.style.justifyContent = Justify.SpaceBetween;

        // Tier filter.
        CreateFilterSection(rail, CollectionPanelText.TierHeader(), UiSpacing.Lg, out _tierChipRow);
        _tierChipRow.style.flexWrap = Wrap.NoWrap;
        _tierChipRow.style.justifyContent = Justify.SpaceBetween;

        // Size filter (Items only — Refresh hides this row on the Skill tab).
        _sizeFilterSection = CreateFilterSection(
            rail,
            CollectionPanelText.SizeHeader(),
            UiSpacing.Lg,
            out _sizeChipRow
        );
        _sizeChipRow.style.flexWrap = Wrap.NoWrap;
        _sizeChipRow.style.justifyContent = Justify.SpaceBetween;

        // Source filter (merchant portraits on Items, trainer portraits on Skills).
        _sourceFilterSection = CreateFilterSection(
            rail,
            CollectionPanelText.SourceHeader(ECardType.Item),
            UiSpacing.Lg,
            out _sourceChipRow,
            out _sourceFilterLabel
        );
        _sourceChipRow.style.flexWrap = Wrap.Wrap;
        _sourceChipRow.style.justifyContent = Justify.FlexStart;
        _sourceChipRow.RegisterCallback<GeometryChangedEvent>(OnSourceChipRowGeometryChanged);

        _statusLabel = CreateLabel(Sizes.FontSmall, FontStyle.Normal, Colors.HistoryStatusText);
        _statusLabel.style.marginTop = UiSpacing.Lg;
        _statusLabel.style.whiteSpace = WhiteSpace.Normal;
        _statusLabel.style.display = DisplayStyle.None;
        rail.Add(_statusLabel);
    }

    private static Button CreateInlineSortButton(string text, Action onClick)
    {
        var button = CreateButton(text, onClick, Sizes.RunsTabWidth, Sizes.ButtonStandardHeight);
        button.style.flexShrink = 1f;
        StyleButton(button, Colors.HistoryChipBackground, Colors.HistoryChipText);
        return button;
    }

    private static VisualElement CreateFilterSection(
        VisualElement parent,
        string title,
        float marginTop,
        out VisualElement chipRow
    ) => CreateFilterSection(parent, title, marginTop, out chipRow, out _);

    private static VisualElement CreateFilterSection(
        VisualElement parent,
        string title,
        float marginTop,
        out VisualElement chipRow,
        out Label label
    )
    {
        var section = new VisualElement();
        section.style.flexDirection = FlexDirection.Column;
        section.style.marginTop = marginTop;
        parent.Add(section);

        label = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistorySubtitleText);
        label.text = title;
        label.style.marginBottom = UiSpacing.Sm;
        section.Add(label);

        chipRow = new VisualElement();
        chipRow.style.flexDirection = FlexDirection.Row;
        chipRow.style.flexWrap = Wrap.Wrap;
        chipRow.style.alignItems = Align.Center;
        chipRow.style.alignSelf = Align.Stretch;
        section.Add(chipRow);

        return section;
    }

    private VisualElement CreateSearchShell()
    {
        var shell = new VisualElement();
        shell.style.flexDirection = FlexDirection.Row;
        shell.style.alignItems = Align.Center;
        shell.style.flexGrow = 1f;
        shell.style.flexShrink = 1f;
        shell.style.minWidth = 0f;
        shell.style.height = Sizes.ButtonStandardHeight;
        shell.style.position = Position.Relative;
        shell.style.backgroundColor = Colors.HistoryStatusBackground;
        UiStyle.Radius(shell.style, Radii.Md);
        UiStyle.Border(shell.style, Borders.Thin, Colors.HistoryStatusBorder);
        UiStyle.HorizontalPadding(shell.style, UiSpacing.Md);
        shell.Add(CreateSearchIcon());

        _searchField = new TextField();
        _searchField.tooltip = CollectionPanelText.SearchPlaceholder();
        _searchField.style.flexGrow = 1f;
        _searchField.style.flexShrink = 1f;
        _searchField.style.minWidth = 0f;
        _searchField.style.height = Sizes.ButtonStandardHeight;
        _searchField.style.minHeight = Sizes.ButtonStandardHeight;
        _searchField.style.unityFont = BppUiFont.Default;
        _searchField.style.unityTextAlign = TextAnchor.MiddleLeft;
        _searchField.style.color = Colors.White;
        _searchField.style.backgroundColor = Colors.Clear;
        _searchField.style.marginLeft = UiSpacing.Md;
        _searchField.style.marginTop = 0f;
        _searchField.style.marginBottom = 0f;
        _searchField.style.paddingTop = 0f;
        _searchField.style.paddingBottom = 0f;
        _searchField.style.justifyContent = Justify.Center;
        UiStyle.BorderWidth(_searchField.style, Borders.None);
        _searchField.RegisterValueChangedCallback(evt =>
        {
            var value = evt.newValue ?? string.Empty;
            RefreshSearchPlaceholder(value);
            _setSearch(value);
        });
        _searchField.RegisterCallback<FocusInEvent>(_ => StyleSearchShell(true));
        _searchField.RegisterCallback<FocusOutEvent>(_ => StyleSearchShell(false));

        var inputField = _searchField.Q<VisualElement>(TextField.textInputUssName);
        if (inputField != null)
            StyleSearchInputField(inputField);
        shell.Add(_searchField);

        _searchPlaceholderLabel = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.WithAlpha(Colors.HistorySubtitleText, 0.66f)
        );
        _searchPlaceholderLabel.text = CollectionPanelText.SearchPlaceholder();
        _searchPlaceholderLabel.pickingMode = PickingMode.Ignore;
        _searchPlaceholderLabel.style.position = Position.Absolute;
        _searchPlaceholderLabel.style.left = 38f;
        _searchPlaceholderLabel.style.right = UiSpacing.Md;
        _searchPlaceholderLabel.style.top = 0f;
        _searchPlaceholderLabel.style.bottom = 0f;
        _searchPlaceholderLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        shell.Add(_searchPlaceholderLabel);
        return shell;
    }

    private static void StyleSearchInputField(VisualElement inputField)
    {
        inputField.style.flexGrow = 1f;
        inputField.style.height = Sizes.ButtonStandardHeight;
        inputField.style.minHeight = 0f;
        inputField.style.marginTop = 0f;
        inputField.style.marginBottom = 0f;
        inputField.style.paddingTop = 0f;
        inputField.style.paddingBottom = 0f;
        inputField.style.unityFont = BppUiFont.Default;
        inputField.style.unityTextAlign = TextAnchor.MiddleLeft;
        inputField.style.backgroundColor = Colors.Clear;
        inputField.style.color = Colors.White;
        inputField.style.justifyContent = Justify.Center;
        UiStyle.BorderWidth(inputField.style, Borders.None);

        var textElement = inputField.Q<TextElement>();
        if (textElement == null)
            return;

        textElement.style.height = Sizes.ButtonStandardHeight;
        textElement.style.unityFont = BppUiFont.Default;
        textElement.style.unityTextAlign = TextAnchor.MiddleLeft;
        textElement.style.marginTop = 0f;
        textElement.style.marginBottom = 0f;
        textElement.style.paddingTop = 0f;
        textElement.style.paddingBottom = 0f;
    }

    private static VisualElement CreateSearchIcon()
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        UiStyle.FixedSize(icon.style, 18f, 18f);
        icon.style.position = Position.Relative;
        icon.style.flexShrink = 0f;

        var lens = new VisualElement { pickingMode = PickingMode.Ignore };
        lens.style.position = Position.Absolute;
        lens.style.left = 2f;
        lens.style.top = 2f;
        UiStyle.FixedSize(lens.style, 10f, 10f);
        UiStyle.Radius(lens.style, 5f);
        UiStyle.Border(lens.style, Borders.Accent, Colors.HistorySubtitleText);
        icon.Add(lens);

        var handle = new VisualElement { pickingMode = PickingMode.Ignore };
        handle.style.position = Position.Absolute;
        handle.style.left = 11f;
        handle.style.top = 13f;
        UiStyle.FixedSize(handle.style, 7f, 2f);
        handle.style.backgroundColor = Colors.HistorySubtitleText;
        UiStyle.Radius(handle.style, 1f);
        icon.Add(handle);
        return icon;
    }

    private Button CreatePackageToggleButton()
    {
        var button = CreateButton(
            string.Empty,
            _togglePackages,
            Sizes.PackageToggleWidth,
            Sizes.ButtonStandardHeight
        );
        button.style.flexDirection = FlexDirection.Row;
        button.style.justifyContent = Justify.SpaceBetween;
        button.style.alignItems = Align.Center;
        UiStyle.HorizontalPadding(button.style, UiSpacing.Md);

        _packageToggleLabel = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistoryChipText);
        _packageToggleLabel.text = CollectionPanelText.PackagesToggle();
        _packageToggleLabel.style.flexShrink = 0f;
        button.Add(_packageToggleLabel);

        _packageSwitchTrack = new VisualElement { pickingMode = PickingMode.Ignore };
        UiStyle.FixedSize(
            _packageSwitchTrack.style,
            Sizes.PackageSwitchWidth,
            Sizes.PackageSwitchHeight
        );
        _packageSwitchTrack.style.position = Position.Relative;
        _packageSwitchTrack.style.marginLeft = UiSpacing.Sm;
        UiStyle.Radius(_packageSwitchTrack.style, Sizes.PackageSwitchHeight / 2f);
        UiStyle.Border(_packageSwitchTrack.style, Borders.Thin, Colors.HistoryStatusBorder);
        button.Add(_packageSwitchTrack);

        _packageSwitchKnob = new VisualElement { pickingMode = PickingMode.Ignore };
        _packageSwitchKnob.style.position = Position.Absolute;
        _packageSwitchKnob.style.left = Sizes.PackageSwitchKnobOffLeft;
        _packageSwitchKnob.style.top = 1f;
        UiStyle.FixedSize(
            _packageSwitchKnob.style,
            Sizes.PackageSwitchKnobSize,
            Sizes.PackageSwitchKnobSize
        );
        UiStyle.Radius(_packageSwitchKnob.style, Sizes.PackageSwitchKnobSize / 2f);
        _packageSwitchTrack.Add(_packageSwitchKnob);
        return button;
    }

    private void BuildGrid(VisualElement parent)
    {
        _gridViewport = new VisualElement();
        _gridViewport.style.flexGrow = 1f;
        _gridViewport.style.flexShrink = 1f;
        _gridViewport.style.minHeight = 0f;
        _gridViewport.style.minWidth = 0f;
        // Recessed "display case" base: darker than the surrounding panel so the slot grid and
        // native card frames read as a lit shelf inside a frame.
        _gridViewport.style.backgroundColor = Colors.CollectionGridCaseBackground;
        UiStyle.Radius(_gridViewport.style, Radii.Md);
        UiStyle.Border(_gridViewport.style, Borders.Thin, Colors.HistoryListFrameBorder);
        _gridViewport.style.overflow = Overflow.Hidden;
        parent.Add(_gridViewport);

        _gridScrollView = new ScrollView(ScrollViewMode.Vertical);
        _gridScrollView.style.flexGrow = 1f;
        _gridScrollView.style.flexShrink = 1f;
        _gridScrollView.style.minHeight = 0f;
        _gridScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        _gridScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
        _gridScrollView.mouseWheelScrollSize = CollectionGridConstants.MouseWheelScrollPoints;
        _gridScrollView.contentContainer.style.flexDirection = FlexDirection.Column;
        // Scroll offset is polled in CollectionPanel.Update via ReadScrollYPixels(): the
        // publicized UIElements Scroller exposes valueChanged ambiguously (field vs property),
        // so we avoid subscribing.
        _gridViewport.Add(_gridScrollView);

        _gridContentSpacer = new VisualElement();
        _gridContentSpacer.style.flexGrow = 0f;
        _gridContentSpacer.style.flexShrink = 0f;
        _gridContentSpacer.style.height = 1f;
        _gridContentSpacer.style.minHeight = 1f;
        _gridContentSpacer.style.width = Length.Percent(100f);
        _gridScrollView.contentContainer.Add(_gridContentSpacer);

        _emptyLabel = CreateLabel(
            Sizes.FontBody,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _emptyLabel.text = CollectionPanelText.NoMatches();
        _emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _emptyLabel.style.height = 80f;
        _emptyLabel.style.display = DisplayStyle.None;
        _gridViewport.Add(_emptyLabel);

        _loadingLabel = CreateLabel(
            Sizes.FontBody,
            FontStyle.Bold,
            Colors.HistoryFooterSecondaryText
        );
        _loadingLabel.pickingMode = PickingMode.Ignore;
        _loadingLabel.style.position = Position.Absolute;
        _loadingLabel.style.left = 0f;
        _loadingLabel.style.right = 0f;
        _loadingLabel.style.top = 0f;
        _loadingLabel.style.bottom = 0f;
        _loadingLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _loadingLabel.style.display = DisplayStyle.None;
        _gridViewport.Add(_loadingLabel);
    }
}
