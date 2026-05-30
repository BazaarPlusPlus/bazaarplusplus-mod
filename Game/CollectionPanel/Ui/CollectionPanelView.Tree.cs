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
        var panel = new VisualElement();
        panel.style.flexGrow = 1f;
        panel.style.backgroundColor = Colors.HistoryPanelBackground;
        panel.style.paddingLeft = UiSpacing.PanelPadding;
        panel.style.paddingRight = UiSpacing.PanelPadding;
        panel.style.paddingTop = UiSpacing.PanelPadding;
        panel.style.paddingBottom = UiSpacing.Xxl;
        panel.style.flexDirection = FlexDirection.Column;
        root.Add(panel);

        BuildHeader(panel);
        BuildFilterBar(panel);
        BuildGrid(panel);
    }

    private void BuildHeader(VisualElement parent)
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Column;
        parent.Add(header);

        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        header.Add(titleRow);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Bold, Colors.HistoryTitleText);
        titleRow.Add(_title);

        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;
        titleRow.Add(spacer);

        _countLabel = CreateLabel(Sizes.FontBody, FontStyle.Bold, Colors.HistoryChipText);
        _countLabel.style.backgroundColor = Colors.HistoryChipBackground;
        _countLabel.style.height = Sizes.ChipHeight;
        _countLabel.style.minWidth = Sizes.ChipMinWidth;
        _countLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        UiStyle.HorizontalPadding(_countLabel.style, UiSpacing.Md);
        UiStyle.Radius(_countLabel.style, Radii.Md);
        _countLabel.style.marginRight = UiSpacing.Md;
        titleRow.Add(_countLabel);

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
        header.Add(_subtitle);

        _statusLabel = CreateLabel(Sizes.FontSmall, FontStyle.Normal, Colors.HistoryStatusText);
        _statusLabel.style.marginTop = UiSpacing.Sm;
        _statusLabel.style.display = DisplayStyle.None;
        header.Add(_statusLabel);
    }

    private void BuildFilterBar(VisualElement parent)
    {
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Column;
        bar.style.marginTop = UiSpacing.Xl;
        parent.Add(bar);

        var topRow = new VisualElement();
        topRow.style.flexDirection = FlexDirection.Row;
        topRow.style.alignItems = Align.Center;
        bar.Add(topRow);

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
        topRow.Add(_itemTabButton);
        _skillTabButton.style.marginLeft = UiSpacing.Md;
        topRow.Add(_skillTabButton);

        _searchField = new TextField();
        _searchField.style.marginLeft = UiSpacing.Xl;
        _searchField.style.height = Sizes.ButtonStandardHeight;
        _searchField.style.minWidth = 220f;
        _searchField.style.maxWidth = 320f;
        _searchField.style.flexGrow = 0f;
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
        topRow.Add(_searchField);

        _clearButton = CreateButton(
            CollectionPanelText.All(),
            _clearFilters,
            Sizes.GhostAllButtonWidth,
            Sizes.ButtonStandardHeight
        );
        _clearButton.style.marginLeft = UiSpacing.Md;
        topRow.Add(_clearButton);

        var rightSpacer = new VisualElement();
        rightSpacer.style.flexGrow = 1f;
        topRow.Add(rightSpacer);

        _merchantPlaceholderButton = CreateButton(
            CollectionPanelText.MerchantHeader(),
            () => { },
            Sizes.FinalBuildRefreshButtonWidth,
            Sizes.ButtonStandardHeight
        );
        _merchantPlaceholderButton.SetEnabled(false);
        topRow.Add(_merchantPlaceholderButton);

        _heroChipRow = new VisualElement();
        _heroChipRow.style.flexDirection = FlexDirection.Row;
        _heroChipRow.style.flexWrap = Wrap.Wrap;
        _heroChipRow.style.alignItems = Align.Center;
        _heroChipRow.style.marginTop = UiSpacing.Md;
        var heroLabel = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistorySubtitleText);
        heroLabel.text = CollectionPanelText.HeroHeader();
        heroLabel.style.marginRight = UiSpacing.Md;
        _heroChipRow.Add(heroLabel);
        bar.Add(_heroChipRow);

        _tierChipRow = new VisualElement();
        _tierChipRow.style.flexDirection = FlexDirection.Row;
        _tierChipRow.style.flexWrap = Wrap.Wrap;
        _tierChipRow.style.alignItems = Align.Center;
        _tierChipRow.style.marginTop = UiSpacing.Sm;
        var tierLabel = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistorySubtitleText);
        tierLabel.text = CollectionPanelText.TierHeader();
        tierLabel.style.marginRight = UiSpacing.Md;
        _tierChipRow.Add(tierLabel);
        bar.Add(_tierChipRow);
    }

    private void BuildGrid(VisualElement parent)
    {
        _gridViewport = new VisualElement();
        _gridViewport.style.flexGrow = 1f;
        _gridViewport.style.flexShrink = 1f;
        _gridViewport.style.minHeight = 0f;
        _gridViewport.style.marginTop = UiSpacing.Xl;
        _gridViewport.style.backgroundColor = Colors.HistoryListFrameBackground;
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
