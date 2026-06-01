#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelUiToolkitView
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

        BuildCoreArea(panel);
        BuildOperationRail(panel);
    }

    private void BuildCoreArea(VisualElement parent)
    {
        var core = new VisualElement();
        core.style.flexGrow = 1f;
        core.style.flexShrink = 1f;
        core.style.minWidth = 0f;
        core.style.minHeight = 0f;
        core.style.flexDirection = FlexDirection.Column;
        parent.Add(core);

        var selectorRow = new VisualElement();
        selectorRow.style.flexDirection = FlexDirection.Row;
        selectorRow.style.flexGrow = 1f;
        selectorRow.style.flexShrink = 1f;
        selectorRow.style.maxHeight = Length.Percent(Sizes.HistorySelectorRowHeightPercent);
        selectorRow.style.minHeight = Sizes.HistorySelectorRowMinHeight;
        selectorRow.style.minWidth = 0f;
        core.Add(selectorRow);

        BuildRunsSection(selectorRow);
        BuildBattlesSection(selectorRow);
        BuildPreview(core);
    }

    private void BuildRunsSection(VisualElement parent)
    {
        _runsSection = CreateSectionPanel(null);
        _runsSection.style.width = Length.Percent(Sizes.RunsColumnWidthPercent);
        _runsSection.style.flexGrow = 0f;
        _runsSection.style.flexShrink = 0f;
        _runsSection.style.minHeight = 0f;
        parent.Add(_runsSection);
        _runsSection.Add(CreateSectionTitle(HistoryPanelText.RunsTab()));
        _runsList = CreateRunList();
        _runsSection.Add(CreateListFrame(_runsList));
    }

    private void BuildBattlesSection(VisualElement parent)
    {
        _battlesSection = CreateSectionPanel(null);
        _battlesSection.style.flexGrow = 1f;
        _battlesSection.style.flexShrink = 1f;
        _battlesSection.style.minHeight = 0f;
        _battlesSection.style.minWidth = 0f;
        _battlesSection.style.marginLeft = UiSpacing.ColumnGap;
        parent.Add(_battlesSection);

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
    }

    private void BuildPreview(VisualElement parent)
    {
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
        parent.Add(_ghostOpponentEliminatedNotice);

        _previewContainer = new VisualElement();
        _previewContainer.style.flexGrow = 0f;
        _previewContainer.style.flexShrink = 0f;
        _previewContainer.style.height = Length.Percent(Sizes.PreviewHeightPercent);
        _previewContainer.style.minHeight = 0f;
        _previewContainer.style.backgroundColor = Colors.HistoryPreviewBackground;
        UiStyle.Radius(_previewContainer.style, Radii.Md);
        UiStyle.Border(_previewContainer.style, Borders.Thin, Colors.HistoryListFrameBorder);
        _previewContainer.style.position = Position.Relative;
        _previewContainer.style.overflow = Overflow.Hidden;
        _previewContainer.style.marginTop = UiSpacing.ColumnGap;
        parent.Add(_previewContainer);

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

    private void BuildOperationRail(VisualElement parent)
    {
        var rail = new VisualElement();
        rail.style.flexDirection = FlexDirection.Column;
        rail.style.flexGrow = 0f;
        rail.style.flexShrink = 0f;
        rail.style.flexBasis = Length.Percent(Sizes.OperationRailWidthPercent);
        rail.style.minWidth = Sizes.OperationRailMinWidth;
        rail.style.maxWidth = Sizes.OperationRailMaxWidth;
        rail.style.minHeight = 0f;
        rail.style.marginLeft = UiSpacing.ColumnGap;
        parent.Add(rail);

        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        rail.Add(titleRow);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Bold, Colors.HistoryTitleText);
        _title.style.flexGrow = 1f;
        _title.style.flexShrink = 1f;
        titleRow.Add(_title);

        _closeButton = CreateButton(
            HistoryPanelText.Close(),
            _close,
            Sizes.CloseButtonWidth,
            Sizes.ButtonStandardHeight
        );
        StyleButton(_closeButton, Colors.CloseBackground, Colors.CloseText);
        titleRow.Add(_closeButton);

        _subtitle = BPPSupporterAttributionRow.Create();
        rail.Add(_subtitle);

        var chipRow = new VisualElement();
        chipRow.style.flexDirection = FlexDirection.Row;
        chipRow.style.flexWrap = Wrap.NoWrap;
        chipRow.style.alignItems = Align.Center;
        chipRow.style.marginTop = UiSpacing.Lg;
        rail.Add(chipRow);

        _countChip = CreateChip();
        _battleChip = CreateChip();
        _databaseChip = CreateChip();
        chipRow.Add(_countChip);
        _battleChip.style.marginLeft = UiSpacing.Sm;
        chipRow.Add(_battleChip);
        _databaseChip.style.marginLeft = UiSpacing.Sm;
        chipRow.Add(_databaseChip);

        _statusLabel = CreateLabel(Sizes.FontCorner, FontStyle.Normal, Colors.HistoryStatusText);
        _statusLabel.style.display = DisplayStyle.None;
        _statusLabel.style.flexGrow = 0f;
        _statusLabel.style.flexShrink = 1f;
        _statusLabel.style.whiteSpace = WhiteSpace.Normal;
        _statusLabel.style.minHeight = Sizes.StatusHeight;
        _statusLabel.style.width = Length.Percent(100f);
        _statusLabel.style.marginTop = UiSpacing.Sm;
        _statusLabel.style.alignSelf = Align.Stretch;
        UiStyle.HorizontalPadding(_statusLabel.style, UiSpacing.Lg);
        _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        _statusLabel.style.backgroundColor = Colors.HistoryStatusBackground;
        UiStyle.Radius(_statusLabel.style, Radii.Status);
        UiStyle.Border(_statusLabel.style, Borders.Thin, Colors.HistoryStatusBorder);
        rail.Add(_statusLabel);

        var tabsRow = new VisualElement();
        tabsRow.style.flexDirection = FlexDirection.Row;
        tabsRow.style.flexWrap = Wrap.NoWrap;
        tabsRow.style.alignItems = Align.Center;
        tabsRow.style.marginTop = UiSpacing.Lg;
        rail.Add(tabsRow);

        _runsTabButton = CreateButton(
            HistoryPanelText.RunsTab(),
            () => _setSectionMode(HistorySectionMode.Runs),
            0f,
            Sizes.ButtonStandardHeight,
            fixedWidth: false
        );
        _ghostTabButton = CreateButton(
            HistoryPanelText.GhostTab(),
            () => _setSectionMode(HistorySectionMode.Ghost),
            0f,
            Sizes.ButtonStandardHeight,
            fixedWidth: false
        );
        _finalBuildRefreshButton = CreateButton(
            HistoryPanelText.RefreshFinalBuilds(),
            _refreshFinalBuilds,
            Sizes.FinalBuildRefreshButtonWidth,
            Sizes.ButtonStandardHeight
        );
        tabsRow.Add(_runsTabButton);
        _ghostTabButton.style.marginLeft = UiSpacing.Sm;
        tabsRow.Add(_ghostTabButton);
        _finalBuildRefreshButton.style.marginLeft = UiSpacing.Md;
        tabsRow.Add(_finalBuildRefreshButton);

        _ghostFilterRow = new VisualElement();
        _ghostFilterRow.style.flexDirection = FlexDirection.Row;
        _ghostFilterRow.style.flexWrap = Wrap.NoWrap;
        _ghostFilterRow.style.alignItems = Align.Center;
        _ghostFilterRow.style.display = DisplayStyle.None;
        _ghostFilterRow.style.marginTop = UiSpacing.Sm;
        rail.Add(_ghostFilterRow);

        _ghostAllButton = CreateButton(
            HistoryPanelText.FilterAll(),
            () => _setGhostFilter(GhostBattleFilter.All),
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _ghostWonButton = CreateButton(
            HistoryPanelText.FilterIWon(),
            () => _setGhostFilter(GhostBattleFilter.IWon),
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _ghostLostButton = CreateButton(
            HistoryPanelText.FilterILost(),
            () => _setGhostFilter(GhostBattleFilter.ILost),
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _ghostFilterRow.Add(_ghostAllButton);
        _ghostWonButton.style.marginLeft = UiSpacing.Sm;
        _ghostFilterRow.Add(_ghostWonButton);
        _ghostLostButton.style.marginLeft = UiSpacing.Sm;
        _ghostFilterRow.Add(_ghostLostButton);

        rail.Add(CreateSpacer());

        var footerBlock = new VisualElement();
        footerBlock.style.flexDirection = FlexDirection.Column;
        footerBlock.style.flexShrink = 0f;
        footerBlock.style.backgroundColor = Colors.HistoryFooterBackground;
        UiStyle.Radius(footerBlock.style, Radii.Md);
        UiStyle.Border(footerBlock.style, Borders.Thin, Colors.HistoryListFrameBorder);
        UiStyle.Padding(footerBlock.style, UiSpacing.Xl);
        rail.Add(footerBlock);

        _footerPrimary = CreateLabel(Sizes.FontFooterPrimary, FontStyle.Bold, Colors.White);
        _footerSecondary = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _footerPrimary.style.display = DisplayStyle.None;
        _footerPrimary.style.whiteSpace = WhiteSpace.Normal;
        footerBlock.Add(_footerPrimary);

        _footerSecondary.style.display = DisplayStyle.None;
        _footerSecondary.style.whiteSpace = WhiteSpace.Normal;
        _footerSecondary.style.marginTop = UiSpacing.Sm;
        footerBlock.Add(_footerSecondary);

        var actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Column;
        actions.style.flexShrink = 0f;
        actions.style.marginTop = UiSpacing.Md;
        rail.Add(actions);

        _deleteButton = CreateRailButton(HistoryPanelText.Delete(), _delete);
        _recordAndReplayButton = CreateRailButton(
            HistoryPanelText.RecordAndReplay(),
            _recordAndReplay
        );
        _replayButton = CreateRailButton(HistoryPanelText.Replay(), _replay);
        StyleButton(_deleteButton, Colors.DeleteBackground, Colors.DeleteText);
        StyleButton(_recordAndReplayButton, Colors.RecordReplayBackground, Colors.RecordReplayText);
        StyleButton(_replayButton, Colors.ReplayBackground, Colors.ReplayText);
        actions.Add(_replayButton);
        _recordAndReplayButton.style.marginTop = UiSpacing.Md;
        actions.Add(_recordAndReplayButton);
        _deleteButton.style.marginTop = UiSpacing.Md;
        actions.Add(_deleteButton);
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
