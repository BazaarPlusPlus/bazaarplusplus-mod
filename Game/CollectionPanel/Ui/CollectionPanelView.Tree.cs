#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.Supporters.Ui;
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

        // Title + count + Close (Close lives here in the operation area, not a top bar).
        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        rail.Add(titleRow);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Bold, Colors.HistoryTitleText);
        _title.style.flexGrow = 1f;
        _title.style.flexShrink = 1f;
        titleRow.Add(_title);

        _countLabel = CreateCountLabel();
        _countLabel.style.marginRight = UiSpacing.Sm;
        titleRow.Add(_countLabel);

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

        var primaryControlsRow = CreateOperationRow(UiSpacing.Xl);
        rail.Add(primaryControlsRow);

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
        primaryControlsRow.Add(_itemTabButton);
        _skillTabButton.style.marginLeft = UiSpacing.Md;
        primaryControlsRow.Add(_skillTabButton);

        primaryControlsRow.Add(CreateOperationSpacer());

        var sortGroup = new VisualElement();
        sortGroup.style.flexDirection = FlexDirection.Row;
        sortGroup.style.alignItems = Align.Center;
        sortGroup.style.flexShrink = 0f;
        sortGroup.style.marginTop = UiSpacing.Xs;
        primaryControlsRow.Add(sortGroup);

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

        // Compact day-number icon toggle, just left of the package toggle.
        _dayToggleButton = CreateDayToggleButton();
        _dayToggleButton.style.marginLeft = UiSpacing.Sm;
        _dayToggleButton.style.marginTop = UiSpacing.Xs;
        primaryControlsRow.Add(_dayToggleButton);

        _packageToggleButton = CreatePackageToggleButton();
        _packageToggleButton.style.marginLeft = UiSpacing.Sm;
        _packageToggleButton.style.marginTop = UiSpacing.Xs;
        primaryControlsRow.Add(_packageToggleButton);

        // Hero filter.
        CreateFilterSection(rail, CollectionPanelText.HeroHeader(), UiSpacing.Xl, out _heroChipRow);
        _heroChipRow.style.flexWrap = Wrap.NoWrap;
        _heroChipRow.style.justifyContent = Justify.SpaceBetween;

        // Tier filter.
        CreateFilterSection(rail, CollectionPanelText.TierHeader(), UiSpacing.Lg, out _tierChipRow);
        _tierChipRow.style.flexWrap = Wrap.NoWrap;
        _tierChipRow.style.justifyContent = Justify.SpaceBetween;

        // Size filter (Items only — Refresh hides the chips but keeps this row's layout slot).
        _sizeFilterSection = CreateFilterSection(
            rail,
            CollectionPanelText.SizeHeader(),
            UiSpacing.Lg,
            out _sizeChipRow
        );
        _sizeFilterSection.style.minHeight = Sizes.CollectionSizeFilterSectionMinHeight;
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

    private static VisualElement CreateOperationRow(float marginTop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.flexWrap = Wrap.Wrap;
        row.style.marginTop = marginTop;
        return row;
    }

    private static VisualElement CreateOperationSpacer()
    {
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;
        spacer.style.flexShrink = 1f;
        spacer.style.minWidth = UiSpacing.Md;
        return spacer;
    }

    private static Label CreateCountLabel()
    {
        var label = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistoryStatusText);
        label.style.backgroundColor = Colors.HistoryStatusBackground;
        label.style.height = Sizes.ButtonCompactHeight;
        UiStyle.FixedWidth(label.style, Sizes.CollectionMatchCountWidth);
        label.style.flexShrink = 0f;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.alignSelf = Align.Center;
        UiStyle.HorizontalPadding(label.style, UiSpacing.Md);
        UiStyle.Radius(label.style, Radii.Md);
        UiStyle.Border(label.style, Borders.Thin, Colors.HistoryStatusBorder);
        return label;
    }

    private static Button CreateInlineSortButton(string text, Action onClick)
    {
        var button = CreateButton(text, onClick, Sizes.RunsTabWidth, Sizes.ButtonStandardHeight);
        button.style.flexShrink = 0f;
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

    // Compact day-number "icon": shows the effective day (current run day, or OutOfRunDay) and
    // tapping toggles whether the day participates in filtering. RefreshDayToggle sets the number
    // and the active highlight; the tooltip names the control since the face is just a number.
    private Button CreateDayToggleButton()
    {
        var button = CreateButton(
            string.Empty,
            _toggleDayFilter,
            Sizes.DayIconWidth,
            Sizes.ButtonStandardHeight
        );
        button.tooltip = CollectionPanelText.DayHeader();
        StyleButton(button, Colors.HistoryChipBackground, Colors.HistoryChipText);
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
