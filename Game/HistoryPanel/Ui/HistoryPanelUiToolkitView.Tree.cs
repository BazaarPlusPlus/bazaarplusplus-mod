#nullable enable
using UnityEngine;
using UnityEngine.UIElements;
using BazaarPlusPlus.Infrastructure.UiTokens;
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelUiToolkitView
{
    private void BuildTree(VisualElement root)
    {
        var overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0f;
        overlay.style.right = 0f;
        overlay.style.top = 0f;
        overlay.style.bottom = 0f;
        overlay.style.backgroundColor = Colors.HistoryOverlay;
        overlay.style.justifyContent = Justify.Center;
        overlay.style.alignItems = Align.Center;
        root.Add(overlay);

        var panel = new VisualElement();
        panel.style.width = Sizes.HistoryPanelWidth;
        panel.style.height = Sizes.HistoryPanelHeight;
        panel.style.backgroundColor = Colors.HistoryPanelBackground;
        UiStyle.Radius(panel.style, Radii.Panel);
        panel.style.paddingLeft = UiSpacing.PanelPadding;
        panel.style.paddingRight = UiSpacing.PanelPadding;
        panel.style.paddingTop = UiSpacing.PanelPadding;
        panel.style.paddingBottom = UiSpacing.Xxl;
        panel.style.flexDirection = FlexDirection.Column;
        overlay.Add(panel);

        BuildHeader(panel);
        BuildContent(panel);
        BuildFooter(panel);
    }

    private void BuildHeader(VisualElement parent)
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Column;
        parent.Add(header);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Bold, Colors.HistoryTitleText);
        header.Add(_title);

        _subtitle = CreateLabel(Sizes.FontBody, FontStyle.Normal, Colors.HistorySubtitleText);
        _subtitle.style.whiteSpace = WhiteSpace.Normal;
        _subtitle.style.marginTop = UiSpacing.Md;
        header.Add(_subtitle);

        var chipRow = new VisualElement();
        chipRow.style.flexDirection = FlexDirection.Row;
        chipRow.style.alignItems = Align.Center;
        chipRow.style.marginTop = UiSpacing.Lg;
        header.Add(chipRow);

        _countChip = CreateChip();
        _battleChip = CreateChip();
        _databaseChip = CreateChip();
        chipRow.Add(_countChip);
        _battleChip.style.marginLeft = UiSpacing.Md;
        chipRow.Add(_battleChip);
        _databaseChip.style.marginLeft = UiSpacing.Md;
        chipRow.Add(_databaseChip);
        _statusLabel = CreateLabel(Sizes.FontCorner, FontStyle.Normal, Colors.HistoryStatusText);
        _statusLabel.style.display = DisplayStyle.None;
        _statusLabel.style.marginLeft = UiSpacing.Xl;
        _statusLabel.style.flexGrow = 0f;
        _statusLabel.style.flexShrink = 1f;
        _statusLabel.style.whiteSpace = WhiteSpace.NoWrap;
        _statusLabel.style.height = Sizes.StatusHeight;
        _statusLabel.style.maxWidth = Sizes.StatusMaxWidth;
        UiStyle.HorizontalPadding(_statusLabel.style, UiSpacing.Lg);
        _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        _statusLabel.style.backgroundColor = Colors.HistoryStatusBackground;
        UiStyle.Radius(_statusLabel.style, Radii.Status);
        UiStyle.Border(_statusLabel.style, Borders.Thin, Colors.HistoryStatusBorder);
        chipRow.Add(_statusLabel);
        chipRow.Add(CreateSpacer());

        _runsTabButton = CreateButton(
            HistoryPanelText.RunsTab(),
            () => _setSectionMode(HistorySectionMode.Runs),
            Sizes.RunsTabWidth,
            Sizes.ButtonStandardHeight
        );
        _ghostTabButton = CreateButton(
            HistoryPanelText.GhostTab(),
            () => _setSectionMode(HistorySectionMode.Ghost),
            Sizes.RunsTabWidth,
            Sizes.ButtonStandardHeight
        );
        _finalBuildRefreshButton = CreateButton(
            HistoryPanelText.RefreshFinalBuilds(),
            _refreshFinalBuilds,
            Sizes.FinalBuildRefreshButtonWidth,
            Sizes.ButtonStandardHeight
        );
        chipRow.Add(_runsTabButton);
        _ghostTabButton.style.marginLeft = UiSpacing.Md;
        chipRow.Add(_ghostTabButton);
        _finalBuildRefreshButton.style.marginLeft = UiSpacing.Md;
        chipRow.Add(_finalBuildRefreshButton);
    }

    private void BuildContent(VisualElement parent)
    {
        var content = new VisualElement();
        content.style.height = Sizes.HistoryContentHeight;
        content.style.flexGrow = 0f;
        content.style.flexShrink = 0f;
        content.style.minHeight = Sizes.HistoryContentHeight;
        content.style.maxHeight = Sizes.HistoryContentHeight;
        content.style.flexDirection = FlexDirection.Column;
        content.style.marginTop = UiSpacing.Xxxl;
        parent.Add(content);

        var columns = new VisualElement();
        columns.style.flexGrow = 1f;
        columns.style.flexShrink = 1f;
        columns.style.minHeight = 0f;
        columns.style.flexDirection = FlexDirection.Row;
        content.Add(columns);

        _runsSection = CreateSectionPanel(Sizes.RunsColumnWidth);
        _runsSection.style.flexGrow = 0f;
        _runsSection.style.flexShrink = 0f;
        _runsSection.style.minHeight = 0f;
        _runsSection.style.minWidth = Sizes.RunsColumnWidth;
        _runsSection.style.maxWidth = Sizes.RunsColumnWidth;
        columns.Add(_runsSection);
        _runsSection.Add(CreateSectionTitle(HistoryPanelText.RunsTab()));
        _runsList = CreateRunList();
        _runsSection.Add(CreateListFrame(_runsList));

        _battlesSection = CreateSectionPanel(null);
        _battlesSection.style.flexGrow = 1f;
        _battlesSection.style.flexShrink = 1f;
        _battlesSection.style.minHeight = 0f;
        _battlesSection.style.marginLeft = UiSpacing.ColumnGap;
        columns.Add(_battlesSection);

        _ghostFilterRow = new VisualElement();
        _ghostFilterRow.style.flexDirection = FlexDirection.Row;
        _ghostFilterRow.style.display = DisplayStyle.None;
        _battlesSection.Add(_ghostFilterRow);

        _ghostAllButton = CreateButton(
            HistoryPanelText.FilterAll(),
            () => _setGhostFilter(GhostBattleFilter.All),
            Sizes.GhostAllButtonWidth,
            Sizes.ButtonCompactHeight
        );
        _ghostWonButton = CreateButton(
            HistoryPanelText.FilterIWon(),
            () => _setGhostFilter(GhostBattleFilter.IWon),
            Sizes.GhostFilterButtonWidth,
            Sizes.ButtonCompactHeight
        );
        _ghostLostButton = CreateButton(
            HistoryPanelText.FilterILost(),
            () => _setGhostFilter(GhostBattleFilter.ILost),
            Sizes.GhostFilterButtonWidth,
            Sizes.ButtonCompactHeight
        );
        _ghostFilterRow.Add(_ghostAllButton);
        _ghostWonButton.style.marginLeft = UiSpacing.Md;
        _ghostFilterRow.Add(_ghostWonButton);
        _ghostLostButton.style.marginLeft = UiSpacing.Md;
        _ghostFilterRow.Add(_ghostLostButton);
        _ghostFilterRow.Add(CreateSpacer());

        _battlesTitle = CreateSectionTitle(HistoryPanelText.Battles());
        _battlesTitle.style.marginTop = UiSpacing.None;
        _battlesSection.Add(_battlesTitle);
        _runsBattleSubtitle = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _runsBattleSubtitle.style.marginTop = UiSpacing.Xs;
        _runsBattleSubtitle.style.display = DisplayStyle.None;
        _battlesSection.Add(_runsBattleSubtitle);
        _battleList = CreateBattleList();
        _battleList.style.marginTop = UiSpacing.Md;
        _battlesSection.Add(CreateListFrame(_battleList));

        _ghostOpponentEliminatedNotice = CreateLabel(
            Sizes.FontBody,
            FontStyle.Bold,
            Colors.HistoryEliminatedText
        );
        UiStyle.FixedHeight(_ghostOpponentEliminatedNotice.style, Sizes.EliminatedNoticeHeight);
        _ghostOpponentEliminatedNotice.style.marginTop = UiSpacing.Lg;
        UiStyle.HorizontalPadding(_ghostOpponentEliminatedNotice.style, UiSpacing.Xxl);
        _ghostOpponentEliminatedNotice.style.unityTextAlign = TextAnchor.MiddleCenter;
        _ghostOpponentEliminatedNotice.style.whiteSpace = WhiteSpace.NoWrap;
        _ghostOpponentEliminatedNotice.style.backgroundColor = Colors.HistoryEliminatedBackground;
        UiStyle.Radius(_ghostOpponentEliminatedNotice.style, Radii.Row);
        UiStyle.Border(
            _ghostOpponentEliminatedNotice.style,
            Borders.Thin,
            Colors.HistoryEliminatedNoticeBorder
        );
        _ghostOpponentEliminatedNotice.style.display = DisplayStyle.None;
        content.Add(_ghostOpponentEliminatedNotice);

        _previewContainer = new VisualElement();
        _previewContainer.style.height = Sizes.PreviewHeight;
        _previewContainer.style.flexShrink = 0f;
        _previewContainer.style.minHeight = Sizes.PreviewHeight;
        _previewContainer.style.maxHeight = Sizes.PreviewHeight;
        _previewContainer.style.backgroundColor = Colors.HistoryPreviewBackground;
        UiStyle.Radius(_previewContainer.style, Radii.Md);
        _previewContainer.style.position = Position.Relative;
        _previewContainer.style.overflow = Overflow.Hidden;
        _previewContainer.style.marginTop = UiSpacing.Lg;
        content.Add(_previewContainer);

        _previewImage = new Image();
        _previewImage.scaleMode = ScaleMode.ScaleToFit;
        _previewImage.style.position = Position.Absolute;
        _previewImage.style.left = UiSpacing.Xs;
        _previewImage.style.right = UiSpacing.Xs;
        _previewImage.style.top = UiSpacing.Lg;
        _previewImage.style.bottom = UiSpacing.Lg;
        _previewContainer.Add(_previewImage);

        _previewStatusLabel = CreateLabel(
            Sizes.FontPreview,
            FontStyle.Normal,
            Colors.HistoryPreviewStatusText
        );
        _previewStatusLabel.style.position = Position.Absolute;
        _previewStatusLabel.style.left = UiSpacing.PanelPadding + UiSpacing.Xs;
        _previewStatusLabel.style.right = UiSpacing.PanelPadding + UiSpacing.Xs;
        _previewStatusLabel.style.top = UiSpacing.ColumnGap;
        _previewStatusLabel.style.bottom = UiSpacing.ColumnGap;
        _previewStatusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _previewStatusLabel.style.whiteSpace = WhiteSpace.Normal;
        _previewContainer.Add(_previewStatusLabel);

        _previewDebugLabel = CreateLabel(
            Sizes.FontCorner,
            FontStyle.Bold,
            Colors.HistoryPreviewDebugText
        );
        _previewDebugLabel.style.position = Position.Absolute;
        _previewDebugLabel.style.right = UiSpacing.Xxl;
        _previewDebugLabel.style.top = UiSpacing.Xl;
        _previewDebugLabel.style.display = DisplayStyle.None;
        _previewContainer.Add(_previewDebugLabel);
    }

    private void BuildFooter(VisualElement parent)
    {
        var footer = new VisualElement();
        footer.style.height = Sizes.FooterHeight;
        footer.style.backgroundColor = Colors.HistoryFooterBackground;
        UiStyle.Radius(footer.style, Radii.Md);
        UiStyle.Padding(footer.style, UiSpacing.Xxxl, UiSpacing.Lg);
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.alignItems = Align.Center;
        footer.style.marginTop = UiSpacing.Lg;
        parent.Add(footer);

        _footerPrimary = CreateLabel(Sizes.FontFooterPrimary, FontStyle.Bold, Colors.White);
        _footerSecondary = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _footerPrimary.style.display = DisplayStyle.None;
        _footerSecondary.style.display = DisplayStyle.None;
        footer.Add(CreateSpacer());

        var actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;
        footer.Add(actions);

        _deleteButton = CreateButton(
            HistoryPanelText.Delete(),
            _delete,
            Sizes.DeleteButtonWidth,
            Sizes.ButtonFooterHeight
        );
        _replayButton = CreateButton(
            HistoryPanelText.Replay(),
            _replay,
            Sizes.ReplayButtonWidth,
            Sizes.ButtonFooterHeight
        );
        var closeButton = CreateButton(
            HistoryPanelText.Close(),
            _close,
            Sizes.CloseButtonWidth,
            Sizes.ButtonFooterHeight
        );
        StyleButton(_deleteButton, Colors.DeleteBackground, Colors.DeleteText);
        StyleButton(_replayButton, Colors.ReplayBackground, Colors.ReplayText);
        StyleButton(closeButton, Colors.CloseBackground, Colors.CloseText);
        actions.Add(_deleteButton);
        _replayButton.style.marginLeft = UiSpacing.Lg;
        actions.Add(_replayButton);
        closeButton.style.marginLeft = UiSpacing.Lg;
        actions.Add(closeButton);
    }

    private ListView CreateRunList()
    {
        var list = new ListView();
        list.style.flexGrow = 1f;
        list.style.flexShrink = 1f;
        list.style.minHeight = 0f;
        list.style.height = Length.Percent(100);
        list.selectionType = SelectionType.Single;
        list.fixedItemHeight = Sizes.RunRowHeight;
        list.makeItem = MakeRunRow;
        list.bindItem = BindRunRow;
        return list;
    }

    private ListView CreateBattleList()
    {
        var list = new ListView();
        list.style.flexGrow = 1f;
        list.style.flexShrink = 1f;
        list.style.minHeight = 0f;
        list.style.height = Length.Percent(100);
        list.selectionType = SelectionType.Single;
        list.fixedItemHeight = Sizes.BattleRowHeight;
        list.makeItem = MakeBattleRow;
        list.bindItem = BindBattleRow;
        return list;
    }
}
