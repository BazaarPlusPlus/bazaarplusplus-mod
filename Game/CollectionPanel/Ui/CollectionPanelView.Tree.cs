#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed partial class CollectionPanelView
{
    private void BuildTree(VisualElement root)
    {
        // Two columns side by side: the operation area (title, close, tabs, search, filters) on
        // the left, the preview grid filling the right at full height. No top header bar, so the
        // grid owns the whole vertical space.
        var panel = new VisualElement();
        panel.style.flexGrow = 1f;
        panel.style.backgroundColor = Colors.HistoryPanelBackground;
        panel.style.paddingLeft = UiSpacing.PanelPadding;
        panel.style.paddingRight = UiSpacing.PanelPadding;
        panel.style.paddingTop = UiSpacing.PanelPadding;
        panel.style.paddingBottom = UiSpacing.Xxl;
        panel.style.flexDirection = FlexDirection.Row;
        root.Add(panel);

        BuildOperationColumn(panel);
        BuildGrid(panel);
    }

    // Left operation column: everything the user interacts with. Stacked vertically so the grid
    // can keep the full panel height to its right.
    private void BuildOperationColumn(VisualElement parent)
    {
        var column = new VisualElement();
        column.style.flexDirection = FlexDirection.Column;
        column.style.flexGrow = 0f;
        column.style.flexShrink = 0f;
        column.style.flexBasis = Length.Percent(24f);
        column.style.minWidth = 300f;
        column.style.maxWidth = 560f;
        parent.Add(column);

        // Title + Close (Close lives here in the operation area, not a top bar).
        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        column.Add(titleRow);

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

        _subtitle = CreateLabel(Sizes.FontBody, FontStyle.Normal, Colors.HistorySubtitleText);
        _subtitle.style.whiteSpace = WhiteSpace.Normal;
        _subtitle.style.marginTop = UiSpacing.Md;
        column.Add(_subtitle);

        _countLabel = CreateLabel(Sizes.FontBody, FontStyle.Bold, Colors.HistoryChipText);
        _countLabel.style.backgroundColor = Colors.HistoryChipBackground;
        _countLabel.style.height = Sizes.ChipHeight;
        _countLabel.style.minWidth = Sizes.ChipMinWidth;
        _countLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _countLabel.style.alignSelf = Align.FlexStart;
        _countLabel.style.marginTop = UiSpacing.Md;
        UiStyle.HorizontalPadding(_countLabel.style, UiSpacing.Md);
        UiStyle.Radius(_countLabel.style, Radii.Md);
        column.Add(_countLabel);

        // Item / Skill tabs.
        var tabsRow = new VisualElement();
        tabsRow.style.flexDirection = FlexDirection.Row;
        tabsRow.style.alignItems = Align.Center;
        tabsRow.style.marginTop = UiSpacing.Xl;
        column.Add(tabsRow);

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

        // Search field (stretches to the column width).
        _searchField = new TextField();
        _searchField.style.marginTop = UiSpacing.Md;
        _searchField.style.height = Sizes.ButtonStandardHeight;
        _searchField.style.flexGrow = 0f;
        _searchField.style.flexShrink = 0f;
        _searchField.style.unityFont = BppUiFont.Default;
        _searchField.style.color = Colors.White;
        _searchField.RegisterValueChangedCallback(evt => _setSearch(evt.newValue ?? string.Empty));
        var inputField = _searchField.Q<VisualElement>(TextField.textInputUssName);
        if (inputField != null)
        {
            inputField.style.unityFont = BppUiFont.Default;
            inputField.style.backgroundColor = Colors.HistoryStatusBackground;
            inputField.style.color = Colors.White;
            UiStyle.Border(inputField.style, Borders.Thin, Colors.HistoryStatusBorder);
        }
        column.Add(_searchField);

        // Clear + merchant placeholder.
        var actionsRow = new VisualElement();
        actionsRow.style.flexDirection = FlexDirection.Row;
        actionsRow.style.alignItems = Align.Center;
        actionsRow.style.marginTop = UiSpacing.Md;
        column.Add(actionsRow);

        _clearButton = CreateButton(
            CollectionPanelText.All(),
            _clearFilters,
            Sizes.GhostAllButtonWidth,
            Sizes.ButtonStandardHeight
        );
        actionsRow.Add(_clearButton);

        _merchantPlaceholderButton = CreateButton(
            CollectionPanelText.MerchantHeader(),
            () => { },
            Sizes.FinalBuildRefreshButtonWidth,
            Sizes.ButtonStandardHeight
        );
        _merchantPlaceholderButton.style.marginLeft = UiSpacing.Md;
        _merchantPlaceholderButton.SetEnabled(false);
        actionsRow.Add(_merchantPlaceholderButton);

        // Hero filter.
        _heroChipRow = new VisualElement();
        _heroChipRow.style.flexDirection = FlexDirection.Row;
        _heroChipRow.style.flexWrap = Wrap.Wrap;
        _heroChipRow.style.alignItems = Align.Center;
        _heroChipRow.style.marginTop = UiSpacing.Xl;
        var heroLabel = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistorySubtitleText);
        heroLabel.text = CollectionPanelText.HeroHeader();
        heroLabel.style.marginRight = UiSpacing.Md;
        heroLabel.style.marginBottom = UiSpacing.Xs;
        _heroChipRow.Add(heroLabel);
        column.Add(_heroChipRow);

        // Tier filter.
        _tierChipRow = new VisualElement();
        _tierChipRow.style.flexDirection = FlexDirection.Row;
        _tierChipRow.style.flexWrap = Wrap.Wrap;
        _tierChipRow.style.alignItems = Align.Center;
        _tierChipRow.style.marginTop = UiSpacing.Lg;
        var tierLabel = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistorySubtitleText);
        tierLabel.text = CollectionPanelText.TierHeader();
        tierLabel.style.marginRight = UiSpacing.Md;
        tierLabel.style.marginBottom = UiSpacing.Xs;
        _tierChipRow.Add(tierLabel);
        column.Add(_tierChipRow);

        // Size filter (Items only — Refresh hides this row on the Skill tab).
        _sizeChipRow = new VisualElement();
        _sizeChipRow.style.flexDirection = FlexDirection.Row;
        _sizeChipRow.style.flexWrap = Wrap.Wrap;
        _sizeChipRow.style.alignItems = Align.Center;
        _sizeChipRow.style.marginTop = UiSpacing.Lg;
        var sizeLabel = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistorySubtitleText);
        sizeLabel.text = CollectionPanelText.SizeHeader();
        sizeLabel.style.marginRight = UiSpacing.Md;
        sizeLabel.style.marginBottom = UiSpacing.Xs;
        _sizeChipRow.Add(sizeLabel);
        column.Add(_sizeChipRow);

        _statusLabel = CreateLabel(Sizes.FontSmall, FontStyle.Normal, Colors.HistoryStatusText);
        _statusLabel.style.marginTop = UiSpacing.Lg;
        _statusLabel.style.whiteSpace = WhiteSpace.Normal;
        _statusLabel.style.display = DisplayStyle.None;
        column.Add(_statusLabel);
    }

    private void BuildGrid(VisualElement parent)
    {
        _gridViewport = new VisualElement();
        _gridViewport.style.flexGrow = 1f;
        _gridViewport.style.flexShrink = 1f;
        _gridViewport.style.minHeight = 0f;
        _gridViewport.style.minWidth = 0f;
        _gridViewport.style.marginLeft = UiSpacing.ColumnGap;
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
    }
}
