#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.GameInterop.Heroes;
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
        _previewStatusLabel.style.maxHeight = Sizes.PanelStatusMaxHeight;
        _previewStatusLabel.style.overflow = Overflow.Hidden;
        _previewContainer.Add(_previewStatusLabel);

        _previewDebugLabel = CreateLabel(
            Sizes.FontCorner,
            FontStyle.Bold,
            Colors.HistoryPreviewDebugText
        );
        _previewDebugLabel.style.position = Position.Absolute;
        _previewDebugLabel.style.right = UiSpacing.Xxl;
        _previewDebugLabel.style.top = UiSpacing.Xl;
        _previewDebugLabel.style.maxWidth = Sizes.StatusMaxWidth;
        _previewDebugLabel.style.whiteSpace = WhiteSpace.NoWrap;
        _previewDebugLabel.style.overflow = Overflow.Hidden;
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

        // ── Fixed header (never scrolls) ─────────────────────────────────────
        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        titleRow.style.flexShrink = 0f;
        rail.Add(titleRow);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Bold, Colors.HistoryTitleText);
        _title.style.flexGrow = 1f;
        _title.style.flexShrink = 1f;
        _title.style.minWidth = 0f;
        _title.style.minHeight = Sizes.ButtonStandardHeight; // VIS-7: lock row height
        _title.style.whiteSpace = WhiteSpace.NoWrap;
        _title.style.overflow = Overflow.Hidden;
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
        _subtitle.style.flexShrink = 0f;
        rail.Add(_subtitle); // VIS-2: no extra marginTop; component owns its Sm(6)

        BuildFilterSlot(rail);

        // ── Overview: read-only chips + the connectivity probe, as one fixed row ─────
        // Pinned directly under the filters so the run/battle counts, DB status, and the
        // one-shot "is my data path healthy?" probe stay visible without scrolling. The probe
        // still hits the remote /health endpoint and reports its result into the footer banner.
        var overviewGroup = new VisualElement();
        overviewGroup.style.flexDirection = FlexDirection.Column;
        overviewGroup.style.flexShrink = 0f;
        overviewGroup.style.marginTop = UiSpacing.Xl;
        rail.Add(overviewGroup);

        var statsChipRow = new VisualElement();
        statsChipRow.style.flexDirection = FlexDirection.Row;
        statsChipRow.style.flexWrap = Wrap.Wrap;
        statsChipRow.style.alignItems = Align.Center;
        overviewGroup.Add(statsChipRow);

        _countChip = CreateChip();
        _countChip.style.minWidth = Sizes.ChipMinWidth;
        _battleChip = CreateChip();
        _battleChip.style.minWidth = Sizes.ChipMinWidth;
        _battleChip.style.marginLeft = UiSpacing.Sm;
        _databaseChip = CreateChip();
        _databaseChip.style.minWidth = Sizes.ChipMinWidth;
        _databaseChip.style.marginLeft = UiSpacing.Sm;
        statsChipRow.Add(_countChip);
        statsChipRow.Add(_battleChip);
        statsChipRow.Add(_databaseChip);

        _checkServerHealthButton = CreateButton(
            HistoryPanelText.CheckServerHealth(),
            _checkServerHealth,
            Sizes.ServerHealthButtonWidth,
            Sizes.ButtonStandardHeight
        );
        StyleButton(_checkServerHealthButton, Colors.ReplayBackground, Colors.ReplayText);
        _checkServerHealthButton.style.marginLeft = UiSpacing.Sm;
        statsChipRow.Add(_checkServerHealthButton);

        // ── Flexible body. railBody (plain element, flexGrow=1) reliably fills the
        //    rail's vertical slack — unlike a ScrollView contentContainer, which
        //    collapses to content height (see CollectionPanelView.Tree.cs:283-315).
        var railBody = new VisualElement();
        railBody.style.flexDirection = FlexDirection.Column;
        railBody.style.flexGrow = 1f;
        railBody.style.flexShrink = 1f;
        railBody.style.minHeight = 0f;
        railBody.style.marginTop = UiSpacing.Xl;
        rail.Add(railBody);

        // Selected-battle detail card: the rail's primary flex-growing element.
        var selectedDetailCard = new VisualElement();
        selectedDetailCard.style.flexDirection = FlexDirection.Column;
        selectedDetailCard.style.flexGrow = 0f;
        selectedDetailCard.style.flexShrink = 0f;
        selectedDetailCard.style.minHeight = 0f;
        selectedDetailCard.style.overflow = Overflow.Hidden;
        selectedDetailCard.style.backgroundColor = Colors.HistoryFooterBackground;
        UiStyle.Radius(selectedDetailCard.style, Radii.Md);
        UiStyle.Border(selectedDetailCard.style, Borders.Thin, Colors.HistoryListFrameBorder);
        UiStyle.Padding(selectedDetailCard.style, UiSpacing.Lg);
        railBody.Add(selectedDetailCard);

        var resultRow = new VisualElement();
        resultRow.style.flexDirection = FlexDirection.Row;
        resultRow.style.flexWrap = Wrap.NoWrap;
        resultRow.style.alignItems = Align.Center;
        selectedDetailCard.Add(resultRow);

        _resultPill = CreateDetailPill(resultRow, Sizes.InlinePillMinWidth);
        _resultPill.style.display = DisplayStyle.None;

        _opponentName = CreateLabel(Sizes.FontFooterPrimary, FontStyle.Bold, Colors.White);
        _opponentName.style.flexGrow = 1f;
        _opponentName.style.flexShrink = 1f;
        _opponentName.style.minWidth = 0f;
        _opponentName.style.whiteSpace = WhiteSpace.NoWrap;
        _opponentName.style.overflow = Overflow.Hidden; // RESP-4: truncate, tooltip carries full name
        _opponentName.style.display = DisplayStyle.None;
        resultRow.Add(_opponentName);

        _dayPill = CreateDetailPill(resultRow, Sizes.RunProgressPillWidth);
        _dayPill.style.marginLeft = UiSpacing.Sm;
        _dayPill.style.display = DisplayStyle.None;

        _detailMeta = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _detailMeta.style.whiteSpace = WhiteSpace.Normal;
        _detailMeta.style.maxHeight = Sizes.DetailTextMaxHeight;
        _detailMeta.style.overflow = Overflow.Hidden;
        _detailMeta.style.marginTop = UiSpacing.Xs;
        _detailMeta.style.display = DisplayStyle.None;
        selectedDetailCard.Add(_detailMeta);

        _detailSnapshot = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _detailSnapshot.style.whiteSpace = WhiteSpace.Normal;
        _detailSnapshot.style.maxHeight = Sizes.DetailTextMaxHeight;
        _detailSnapshot.style.overflow = Overflow.Hidden;
        _detailSnapshot.style.marginTop = UiSpacing.Xxs;
        _detailSnapshot.style.display = DisplayStyle.None;
        selectedDetailCard.Add(_detailSnapshot);

        _detailPlaceholder = CreateLabel(
            Sizes.FontSmall,
            FontStyle.Normal,
            Colors.WithAlpha(Colors.HistoryFooterSecondaryText, 0.6f) // single 0.6 layer, no style.opacity
        );
        _detailPlaceholder.style.whiteSpace = WhiteSpace.Normal;
        _detailPlaceholder.style.maxHeight = Sizes.DetailTextMaxHeight;
        _detailPlaceholder.style.overflow = Overflow.Hidden;
        _detailPlaceholder.style.unityTextAlign = TextAnchor.MiddleCenter;
        _detailPlaceholder.style.marginTop = UiSpacing.Sm;
        _detailPlaceholder.style.display = DisplayStyle.None;
        selectedDetailCard.Add(_detailPlaceholder);

        _ghostOpponentEliminatedNotice = CreateLabel(
            Sizes.FontBody,
            FontStyle.Bold,
            Colors.HistoryEliminatedText
        );
        UiStyle.Padding(_ghostOpponentEliminatedNotice.style, UiSpacing.Md, UiSpacing.Sm);
        _ghostOpponentEliminatedNotice.style.unityTextAlign = TextAnchor.MiddleCenter;
        _ghostOpponentEliminatedNotice.style.whiteSpace = WhiteSpace.Normal; // was NoWrap
        _ghostOpponentEliminatedNotice.style.maxHeight = Sizes.DetailNoticeMaxHeight;
        _ghostOpponentEliminatedNotice.style.overflow = Overflow.Hidden;
        _ghostOpponentEliminatedNotice.style.backgroundColor = Colors.HistoryEliminatedBackground;
        UiStyle.Radius(_ghostOpponentEliminatedNotice.style, Radii.Row);
        UiStyle.Border(
            _ghostOpponentEliminatedNotice.style,
            Borders.Thin,
            Colors.HistoryEliminatedNoticeBorder
        );
        _ghostOpponentEliminatedNotice.style.display = DisplayStyle.None;
        selectedDetailCard.Add(_ghostOpponentEliminatedNotice);

        // ── Fixed footer: status banner directly above its actions ───────────
        _statusLabel = CreateLabel(Sizes.FontCorner, FontStyle.Normal, Colors.HistoryStatusText);
        _statusLabel.style.display = DisplayStyle.None;
        _statusLabel.style.flexGrow = 0f;
        _statusLabel.style.flexShrink = 0f;
        _statusLabel.style.whiteSpace = WhiteSpace.Normal;
        _statusLabel.style.minHeight = Sizes.StatusHeight;
        _statusLabel.style.maxHeight = Sizes.PanelStatusMaxHeight;
        _statusLabel.style.overflow = Overflow.Hidden;
        _statusLabel.style.width = Length.Percent(100f);
        _statusLabel.style.marginTop = UiSpacing.Md;
        _statusLabel.style.alignSelf = Align.Stretch;
        UiStyle.Padding(_statusLabel.style, UiSpacing.Xl, UiSpacing.Sm); // VIS-4
        _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        _statusLabel.style.backgroundColor = Colors.HistoryStatusBackground;
        UiStyle.Radius(_statusLabel.style, Radii.Md); // VIS-3: was Radii.Status
        UiStyle.Border(_statusLabel.style, Borders.Thin, Colors.HistoryStatusBorder);
        rail.Add(_statusLabel);

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

        var replayActionRow = new VisualElement();
        replayActionRow.style.flexDirection = FlexDirection.Row;
        replayActionRow.style.flexShrink = 0f;
        replayActionRow.style.width = Length.Percent(100f);
        actions.Add(replayActionRow);

        UseActionRowRatio(_replayButton, 3f);
        UseActionRowRatio(_recordAndReplayButton, 1f);
        replayActionRow.Add(_replayButton);
        _recordAndReplayButton.style.marginLeft = UiSpacing.Md;
        replayActionRow.Add(_recordAndReplayButton);

        _deleteButton.style.marginTop = UiSpacing.Md;
        actions.Add(_deleteButton);
    }

    private void BuildFilterSlot(VisualElement rail)
    {
        _filterSlot = new VisualElement();
        _filterSlot.style.flexDirection = FlexDirection.Column;
        _filterSlot.style.flexShrink = 0f;
        _filterSlot.style.marginTop = UiSpacing.Xl;
        _filterSlot.style.backgroundColor = Colors.HistorySectionBackground;
        UiStyle.Radius(_filterSlot.style, Radii.Md);
        UiStyle.Padding(_filterSlot.style, UiSpacing.Md);
        rail.Add(_filterSlot);

        var tabsRow = new VisualElement();
        tabsRow.style.flexDirection = FlexDirection.Row;
        tabsRow.style.flexWrap = Wrap.NoWrap;
        tabsRow.style.alignItems = Align.Center;
        _filterSlot.Add(tabsRow);

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
        tabsRow.Add(_runsTabButton);
        _ghostTabButton.style.marginLeft = UiSpacing.Sm;
        tabsRow.Add(_ghostTabButton);

        _runsFilterRow = new VisualElement();
        _runsFilterRow.style.flexDirection = FlexDirection.Row;
        _runsFilterRow.style.flexWrap = Wrap.NoWrap;
        _runsFilterRow.style.alignItems = Align.Center;
        _runsFilterRow.style.marginTop = UiSpacing.Sm;
        _filterSlot.Add(_runsFilterRow);

        _heroChips = new Button[HeroRoster.Length];
        for (var i = 0; i < HeroRoster.Length; i++)
        {
            var heroName = HeroRoster[i];
            var heroChip = CreateButton(
                HeroVisual.Resolve(heroName).ShortCode,
                () => _setRunHero(heroName),
                0f,
                Sizes.ButtonCompactHeight,
                fixedWidth: false
            );
            heroChip.tooltip = heroName;
            if (i > 0)
                heroChip.style.marginLeft = UiSpacing.Xs;
            _runsFilterRow.Add(heroChip);
            _heroChips[i] = heroChip;
        }

        _ghostFilterRow = new VisualElement();
        _ghostFilterRow.style.flexDirection = FlexDirection.Row;
        _ghostFilterRow.style.flexWrap = Wrap.NoWrap;
        _ghostFilterRow.style.alignItems = Align.Center;
        _ghostFilterRow.style.display = DisplayStyle.None;
        _ghostFilterRow.style.marginTop = UiSpacing.Sm;
        _filterSlot.Add(_ghostFilterRow);

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
        _ghostDayButton = CreateButton(
            HistoryPanelText.FilterDayMin10(),
            _toggleGhostDayMin10,
            0f,
            Sizes.ButtonCompactHeight,
            fixedWidth: false
        );
        _ghostFilterRow.Add(_ghostAllButton);
        _ghostWonButton.style.marginLeft = UiSpacing.Sm;
        _ghostFilterRow.Add(_ghostWonButton);
        _ghostLostButton.style.marginLeft = UiSpacing.Sm;
        _ghostFilterRow.Add(_ghostLostButton);
        _ghostDayButton.style.marginLeft = UiSpacing.Md;
        _ghostFilterRow.Add(_ghostDayButton);
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
