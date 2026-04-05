#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelUiToolkitView : IDisposable
{
    private static Font? _uiFont;

    private readonly Transform _parent;
    private readonly Action _close;
    private readonly Action _replay;
    private readonly Action _delete;
    private readonly Action<int> _selectRun;
    private readonly Action<int> _selectBattle;
    private readonly Action<HistorySectionMode> _setSectionMode;
    private readonly Action<GhostBattleFilter> _setGhostFilter;

    private GameObject? _rootObject;
    private UIDocument? _document;
    private PanelSettings? _panelSettings;
    private VisualElement? _root;
    private Label? _title;
    private Label? _subtitle;
    private Label? _countChip;
    private Label? _battleChip;
    private Label? _databaseChip;
    private Button? _runsTabButton;
    private Button? _ghostTabButton;
    private Label? _statusLabel;
    private VisualElement? _runsSection;
    private VisualElement? _battlesSection;
    private VisualElement? _ghostFilterRow;
    private Button? _ghostAllButton;
    private Button? _ghostWonButton;
    private Button? _ghostLostButton;
    private ListView? _runsList;
    private ListView? _battleList;
    private Label? _battlesTitle;
    private Label? _runsBattleSubtitle;
    private Image? _previewImage;
    private Label? _previewStatusLabel;
    private Label? _previewDebugLabel;
    private VisualElement? _previewContainer;
    private Label? _footerPrimary;
    private Label? _footerSecondary;
    private Button? _deleteButton;
    private Button? _replayButton;
    private bool _suppressSelectionCallbacks;

    public HistoryPanelUiToolkitView(
        Transform parent,
        Action close,
        Action replay,
        Action delete,
        Action<int> selectRun,
        Action<int> selectBattle,
        Action<HistorySectionMode> setSectionMode,
        Action<GhostBattleFilter> setGhostFilter
    )
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _delete = delete ?? throw new ArgumentNullException(nameof(delete));
        _selectRun = selectRun ?? throw new ArgumentNullException(nameof(selectRun));
        _selectBattle = selectBattle ?? throw new ArgumentNullException(nameof(selectBattle));
        _setSectionMode = setSectionMode ?? throw new ArgumentNullException(nameof(setSectionMode));
        _setGhostFilter = setGhostFilter ?? throw new ArgumentNullException(nameof(setGhostFilter));
    }

    public void EnsureCreated()
    {
        if (_rootObject != null)
            return;

        _rootObject = new GameObject("HistoryPanelUiToolkitRoot");
        _rootObject.transform.SetParent(_parent, false);
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.sortingOrder = 26;
        _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
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
        _root.style.unityFont = GetUiFont();
        _root.pickingMode = PickingMode.Position;

        BuildTree(_root);
    }

    public void SetVisible(bool visible)
    {
        if (_root != null)
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void Refresh(HistoryPanelUiToolkitModel model)
    {
        if (_root == null || _runsList == null || _battleList == null)
            return;

        _title!.text = model.Title;
        _subtitle!.text = model.Subtitle;
        _countChip!.text = model.CountChipText;
        _battleChip!.text = model.BattleChipText;
        _databaseChip!.text = model.DatabaseChipText;
        _statusLabel!.text = model.StatusMessage ?? string.Empty;
        _statusLabel.style.display = string.IsNullOrWhiteSpace(model.StatusMessage)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
        _runsSection!.style.display =
            model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.None : DisplayStyle.Flex;
        _battlesSection!.style.marginLeft =
            model.SectionMode == HistorySectionMode.Ghost ? 0f : 18f;
        _battlesTitle!.style.display =
            model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.None : DisplayStyle.Flex;
        _runsBattleSubtitle!.text = model.RunsBattleSubtitle;
        _runsBattleSubtitle.style.display = DisplayStyle.None;
        _footerPrimary!.text = model.FooterPrimaryText;
        _footerSecondary!.text = model.FooterSecondaryText;

        RefreshTabButton(_runsTabButton!, model.SectionMode == HistorySectionMode.Runs);
        RefreshTabButton(_ghostTabButton!, model.SectionMode == HistorySectionMode.Ghost);
        _ghostFilterRow!.style.display =
            model.SectionMode == HistorySectionMode.Ghost ? DisplayStyle.Flex : DisplayStyle.None;
        RefreshGhostFilterButton(_ghostAllButton!, model.GhostBattleFilter == GhostBattleFilter.All);
        RefreshGhostFilterButton(_ghostWonButton!, model.GhostBattleFilter == GhostBattleFilter.IWon);
        RefreshGhostFilterButton(_ghostLostButton!, model.GhostBattleFilter == GhostBattleFilter.ILost);

        _replayButton!.text = model.ReplayButtonText;
        _replayButton.SetEnabled(model.ReplayButtonEnabled);
        _deleteButton!.text = model.DeleteButtonText;
        _deleteButton.SetEnabled(model.DeleteButtonEnabled);
        RefreshDeleteButton(_deleteButton, model.DeleteButtonText, model.DeleteButtonEnabled);

        _runsList.itemsSource = model.Runs;
        _runsList.Rebuild();
        _battleList.itemsSource = model.VisibleBattles;
        _battleList.Rebuild();

        _suppressSelectionCallbacks = true;
        try
        {
            _runsList.selectedIndex = model.Runs.Count == 0 ? -1 : model.SelectedRunIndex;
            _battleList.selectedIndex = model.VisibleBattles.Count == 0 ? -1 : model.SelectedBattleIndex;
            _runsList.RefreshItems();
            _battleList.RefreshItems();
        }
        finally
        {
            _suppressSelectionCallbacks = false;
        }
    }

    public void SetPreviewTexture(Texture? texture)
    {
        if (_previewImage == null)
            return;

        _previewImage.image = texture;
        _previewImage.MarkDirtyRepaint();
    }

    public void SetPreviewStatus(string? message, bool visible)
    {
        if (_previewStatusLabel == null)
            return;

        _previewStatusLabel.text = message ?? string.Empty;
        _previewStatusLabel.style.display =
            visible && !string.IsNullOrWhiteSpace(message) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void SetPreviewDebug(string? message, bool visible)
    {
        if (_previewDebugLabel == null)
            return;

        _previewDebugLabel.text = message ?? string.Empty;
        _previewDebugLabel.style.display =
            visible && !string.IsNullOrWhiteSpace(message) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void SetPreviewDebugVisible(bool visible)
    {
        if (_previewDebugLabel == null)
            return;

        if (_previewDebugLabel.style.display != DisplayStyle.None)
            _previewDebugLabel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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

    private void BuildTree(VisualElement root)
    {
        var overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0f;
        overlay.style.right = 0f;
        overlay.style.top = 0f;
        overlay.style.bottom = 0f;
        overlay.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.82f);
        overlay.style.justifyContent = Justify.Center;
        overlay.style.alignItems = Align.Center;
        root.Add(overlay);

        var panel = new VisualElement();
        panel.style.width = 1280f;
        panel.style.height = 1020f;
        panel.style.backgroundColor = new Color(0.08f, 0.10f, 0.13f, 0.985f);
        panel.style.borderTopLeftRadius = 14f;
        panel.style.borderTopRightRadius = 14f;
        panel.style.borderBottomLeftRadius = 14f;
        panel.style.borderBottomRightRadius = 14f;
        panel.style.paddingLeft = 24f;
        panel.style.paddingRight = 24f;
        panel.style.paddingTop = 24f;
        panel.style.paddingBottom = 14f;
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

        _title = CreateLabel(28, FontStyle.Bold, new Color(0.97f, 0.85f, 0.57f, 1f));
        header.Add(_title);

        _subtitle = CreateLabel(14, FontStyle.Normal, new Color(0.82f, 0.86f, 0.91f, 0.94f));
        _subtitle.style.whiteSpace = WhiteSpace.Normal;
        _subtitle.style.marginTop = 8f;
        header.Add(_subtitle);

        var chipRow = new VisualElement();
        chipRow.style.flexDirection = FlexDirection.Row;
        chipRow.style.alignItems = Align.Center;
        chipRow.style.marginTop = 10f;
        header.Add(chipRow);

        _countChip = CreateChip();
        _battleChip = CreateChip();
        _databaseChip = CreateChip();
        chipRow.Add(_countChip);
        _battleChip.style.marginLeft = 8f;
        chipRow.Add(_battleChip);
        _databaseChip.style.marginLeft = 8f;
        chipRow.Add(_databaseChip);
        _statusLabel = CreateLabel(11, FontStyle.Normal, new Color(0.86f, 0.90f, 0.96f, 0.92f));
        _statusLabel.style.display = DisplayStyle.None;
        _statusLabel.style.marginLeft = 12f;
        _statusLabel.style.flexGrow = 0f;
        _statusLabel.style.flexShrink = 1f;
        _statusLabel.style.whiteSpace = WhiteSpace.NoWrap;
        _statusLabel.style.height = 24f;
        _statusLabel.style.maxWidth = 220f;
        _statusLabel.style.paddingLeft = 10f;
        _statusLabel.style.paddingRight = 10f;
        _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        _statusLabel.style.backgroundColor = new Color(0.16f, 0.20f, 0.26f, 0.72f);
        _statusLabel.style.borderTopLeftRadius = 12f;
        _statusLabel.style.borderTopRightRadius = 12f;
        _statusLabel.style.borderBottomLeftRadius = 12f;
        _statusLabel.style.borderBottomRightRadius = 12f;
        _statusLabel.style.borderLeftWidth = 1f;
        _statusLabel.style.borderRightWidth = 1f;
        _statusLabel.style.borderTopWidth = 1f;
        _statusLabel.style.borderBottomWidth = 1f;
        _statusLabel.style.borderLeftColor = new Color(0.34f, 0.40f, 0.49f, 0.36f);
        _statusLabel.style.borderRightColor = new Color(0.34f, 0.40f, 0.49f, 0.36f);
        _statusLabel.style.borderTopColor = new Color(0.34f, 0.40f, 0.49f, 0.36f);
        _statusLabel.style.borderBottomColor = new Color(0.34f, 0.40f, 0.49f, 0.36f);
        chipRow.Add(_statusLabel);
        chipRow.Add(CreateSpacer());

        _runsTabButton = CreateButton(HistoryPanelText.RunsTab(), () => _setSectionMode(HistorySectionMode.Runs), 72f, 32f);
        _ghostTabButton = CreateButton(HistoryPanelText.GhostTab(), () => _setSectionMode(HistorySectionMode.Ghost), 72f, 32f);
        chipRow.Add(_runsTabButton);
        _ghostTabButton.style.marginLeft = 8f;
        chipRow.Add(_ghostTabButton);
    }

    private void BuildContent(VisualElement parent)
    {
        var content = new VisualElement();
        content.style.height = 792f;
        content.style.flexGrow = 0f;
        content.style.flexShrink = 0f;
        content.style.minHeight = 792f;
        content.style.maxHeight = 792f;
        content.style.flexDirection = FlexDirection.Column;
        content.style.marginTop = 16f;
        parent.Add(content);

        var columns = new VisualElement();
        columns.style.flexGrow = 1f;
        columns.style.flexShrink = 1f;
        columns.style.minHeight = 0f;
        columns.style.flexDirection = FlexDirection.Row;
        content.Add(columns);

        _runsSection = CreateSectionPanel(500f);
        _runsSection.style.flexGrow = 0f;
        _runsSection.style.flexShrink = 0f;
        _runsSection.style.minHeight = 0f;
        _runsSection.style.minWidth = 500f;
        _runsSection.style.maxWidth = 500f;
        columns.Add(_runsSection);
        _runsSection.Add(CreateSectionTitle(HistoryPanelText.RunsTab()));
        _runsList = CreateRunList();
        _runsSection.Add(CreateListFrame(_runsList));

        _battlesSection = CreateSectionPanel(null);
        _battlesSection.style.flexGrow = 1f;
        _battlesSection.style.flexShrink = 1f;
        _battlesSection.style.minHeight = 0f;
        _battlesSection.style.marginLeft = 18f;
        columns.Add(_battlesSection);

        _ghostFilterRow = new VisualElement();
        _ghostFilterRow.style.flexDirection = FlexDirection.Row;
        _ghostFilterRow.style.display = DisplayStyle.None;
        _battlesSection.Add(_ghostFilterRow);

        _ghostAllButton = CreateButton(HistoryPanelText.FilterAll(), () => _setGhostFilter(GhostBattleFilter.All), 70f, 24f);
        _ghostWonButton = CreateButton(HistoryPanelText.FilterIWon(), () => _setGhostFilter(GhostBattleFilter.IWon), 78f, 24f);
        _ghostLostButton = CreateButton(HistoryPanelText.FilterILost(), () => _setGhostFilter(GhostBattleFilter.ILost), 78f, 24f);
        _ghostFilterRow.Add(_ghostAllButton);
        _ghostWonButton.style.marginLeft = 8f;
        _ghostFilterRow.Add(_ghostWonButton);
        _ghostLostButton.style.marginLeft = 8f;
        _ghostFilterRow.Add(_ghostLostButton);
        _ghostFilterRow.Add(CreateSpacer());

        _battlesTitle = CreateSectionTitle(HistoryPanelText.Battles());
        _battlesTitle.style.marginTop = 0f;
        _battlesSection.Add(_battlesTitle);
        _runsBattleSubtitle = CreateLabel(12, FontStyle.Normal, new Color(0.72f, 0.77f, 0.84f, 0.92f));
        _runsBattleSubtitle.style.marginTop = 4f;
        _runsBattleSubtitle.style.display = DisplayStyle.None;
        _battlesSection.Add(_runsBattleSubtitle);
        _battleList = CreateBattleList();
        _battleList.style.marginTop = 8f;
        _battlesSection.Add(CreateListFrame(_battleList));

        _previewContainer = new VisualElement();
        _previewContainer.style.height = 284f;
        _previewContainer.style.flexShrink = 0f;
        _previewContainer.style.minHeight = 284f;
        _previewContainer.style.maxHeight = 284f;
        _previewContainer.style.backgroundColor = new Color(0.07f, 0.09f, 0.12f, 0.99f);
        _previewContainer.style.borderTopLeftRadius = 10f;
        _previewContainer.style.borderTopRightRadius = 10f;
        _previewContainer.style.borderBottomLeftRadius = 10f;
        _previewContainer.style.borderBottomRightRadius = 10f;
        _previewContainer.style.position = Position.Relative;
        _previewContainer.style.overflow = Overflow.Hidden;
        _previewContainer.style.marginTop = 14f;
        content.Add(_previewContainer);

        _previewImage = new Image();
        _previewImage.scaleMode = ScaleMode.ScaleToFit;
        _previewImage.style.position = Position.Absolute;
        _previewImage.style.left = 4f;
        _previewImage.style.right = 4f;
        _previewImage.style.top = 10f;
        _previewImage.style.bottom = 10f;
        _previewContainer.Add(_previewImage);

        _previewStatusLabel = CreateLabel(13, FontStyle.Normal, new Color(0.82f, 0.87f, 0.93f, 0.96f));
        _previewStatusLabel.style.position = Position.Absolute;
        _previewStatusLabel.style.left = 28f;
        _previewStatusLabel.style.right = 28f;
        _previewStatusLabel.style.top = 18f;
        _previewStatusLabel.style.bottom = 18f;
        _previewStatusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _previewStatusLabel.style.whiteSpace = WhiteSpace.Normal;
        _previewContainer.Add(_previewStatusLabel);

        _previewDebugLabel = CreateLabel(11, FontStyle.Bold, new Color(0.97f, 0.85f, 0.57f, 0.96f));
        _previewDebugLabel.style.position = Position.Absolute;
        _previewDebugLabel.style.right = 14f;
        _previewDebugLabel.style.top = 12f;
        _previewDebugLabel.style.display = DisplayStyle.None;
        _previewContainer.Add(_previewDebugLabel);
    }

    private void BuildFooter(VisualElement parent)
    {
        var footer = new VisualElement();
        footer.style.height = 56f;
        footer.style.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 0.98f);
        footer.style.borderTopLeftRadius = 10f;
        footer.style.borderTopRightRadius = 10f;
        footer.style.borderBottomLeftRadius = 10f;
        footer.style.borderBottomRightRadius = 10f;
        footer.style.paddingLeft = 16f;
        footer.style.paddingRight = 16f;
        footer.style.paddingTop = 10f;
        footer.style.paddingBottom = 10f;
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.alignItems = Align.Center;
        footer.style.marginTop = 10f;
        parent.Add(footer);

        _footerPrimary = CreateLabel(15, FontStyle.Bold, Color.white);
        _footerSecondary = CreateLabel(12, FontStyle.Normal, new Color(0.72f, 0.77f, 0.84f, 0.94f));
        _footerPrimary.style.display = DisplayStyle.None;
        _footerSecondary.style.display = DisplayStyle.None;
        footer.Add(CreateSpacer());

        var actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;
        footer.Add(actions);

        _deleteButton = CreateButton(HistoryPanelText.Delete(), _delete, 130f, 36f);
        _replayButton = CreateButton(HistoryPanelText.Replay(), _replay, 140f, 36f);
        var closeButton = CreateButton(HistoryPanelText.Close(), _close, 96f, 36f);
        StyleButton(_deleteButton, new Color(0.40f, 0.24f, 0.20f, 0.98f), new Color(1f, 0.93f, 0.90f, 1f));
        StyleButton(_replayButton, new Color(0.19f, 0.31f, 0.39f, 0.98f), new Color(0.88f, 0.95f, 1f, 1f));
        StyleButton(closeButton, new Color(0.29f, 0.20f, 0.20f, 0.98f), new Color(0.98f, 0.92f, 0.90f, 1f));
        actions.Add(_deleteButton);
        _replayButton.style.marginLeft = 10f;
        actions.Add(_replayButton);
        closeButton.style.marginLeft = 10f;
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
        list.fixedItemHeight = 98f;
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
        list.fixedItemHeight = 96f;
        list.makeItem = MakeBattleRow;
        list.bindItem = BindBattleRow;
        return list;
    }

    private VisualElement MakeRunRow()
    {
        var row = CreateRowShell();
        row.style.marginTop = 4f;
        row.style.marginBottom = 4f;
        row.style.borderLeftWidth = 1f;
        row.style.borderRightWidth = 1f;
        row.style.borderTopWidth = 1f;
        row.style.borderBottomWidth = 1f;
        var accent = CreateAccentBar();
        row.Add(accent);
        var outcomeHost = new VisualElement();
        outcomeHost.style.width = 62f;
        outcomeHost.style.flexShrink = 0f;
        outcomeHost.style.alignItems = Align.Center;
        outcomeHost.style.justifyContent = Justify.Center;
        row.Add(outcomeHost);
        var outcomeBubble = CreateDayBubble(outcomeHost);
        var content = CreateRowContent();
        content.style.justifyContent = Justify.Center;
        content.style.paddingLeft = 4f;
        row.Add(content);

        var topRow = CreateRowTopRow();
        content.Add(topRow);
        var heroPill = CreateInlinePill(topRow, 64f);
        SetFixedPillWidth(heroPill, 60f);
        var rankPill = CreateInlinePill(topRow, 84f);
        SetFixedPillWidth(rankPill, 84f);
        var progressPill = CreateInlinePill(topRow, 72f);
        SetFixedPillWidth(progressPill, 50f);
        var statusPill = CreateInlinePill(topRow, 84f);
        SetFixedPillWidth(statusPill, 60f);
        topRow.Add(CreateSpacer());
        var timeLabel = CreateRowCornerLabel(topRow, 11);

        var statRow = CreateInfoChipRow(content, 6f, 6f);
        var healthChip = CreateInfoChip(statRow, HistoryPanelText.StatHealthShort(), 54f);
        SetEqualChipWidth(healthChip);
        var prestigeChip = CreateInfoChip(statRow, HistoryPanelText.StatPrestigeShort(), 54f);
        SetEqualChipWidth(prestigeChip);
        var levelChip = CreateInfoChip(statRow, HistoryPanelText.StatLevelShort(), 54f);
        SetEqualChipWidth(levelChip);
        var incomeChip = CreateInfoChip(statRow, HistoryPanelText.StatIncomeShort(), 54f);
        SetEqualChipWidth(incomeChip);
        var goldChip = CreateInfoChip(statRow, HistoryPanelText.StatGoldShort(), 54f);
        SetEqualChipWidth(goldChip, isLast: true);
        var refs = new RunRowRefs(
            row,
            accent,
            outcomeBubble,
            rankPill,
            heroPill,
            progressPill,
            statusPill,
            timeLabel,
            statRow,
            healthChip,
            prestigeChip,
            levelChip,
            incomeChip,
            goldChip
        );
        row.userData = refs;
        row.RegisterCallback<ClickEvent>(_ =>
        {
            if (!_suppressSelectionCallbacks && refs.Index >= 0)
                _selectRun(refs.Index);
        });
        return row;
    }

    private void BindRunRow(VisualElement element, int index)
    {
        if (_runsList?.itemsSource is not List<HistoryRunRecord> items || index < 0 || index >= items.Count)
            return;

        var run = items[index];
        var refs = (RunRowRefs)element.userData;
        refs.Index = index;
        BindRunOutcomeBubble(refs.OutcomeBubble, run);
        var timing = new List<string>();
        var duration = HistoryPanelFormatter.FormatRunDuration(run);
        if (!string.IsNullOrWhiteSpace(duration))
            timing.Add(duration);
        timing.Add(HistoryPanelFormatter.FormatTimestamp(run.LastSeenAtUtc));
        refs.Time.text = string.Join(" · ", timing);

        var rank = HistoryPanelFormatter.NormalizeRank(run.PlayerRank);
        if (string.Equals(run.GameMode?.Trim(), "Ranked", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(rank, "Legendary", StringComparison.OrdinalIgnoreCase))
            {
                ConfigurePill(
                    refs.RankPill,
                    HistoryPanelText.RankLabel(rank, run.PlayerRating),
                    ColorFromRgb(241, 54, 41),
                    Color.white,
                    true
                );
            }
            else if (!string.IsNullOrWhiteSpace(rank))
            {
                var palette = GetRankBadgePalette(rank);
                ConfigurePill(refs.RankPill, HistoryPanelText.RankLabel(rank), palette.Background, palette.Text, true);
            }
            else
            {
                ConfigurePill(
                    refs.RankPill,
                    HistoryPanelText.Unranked(),
                    new Color(0.22f, 0.24f, 0.29f, 0.98f),
                    new Color(0.90f, 0.94f, 1f, 1f),
                    true
                );
            }
        }
        else
        {
            ConfigurePill(
                refs.RankPill,
                HistoryPanelText.Unranked(),
                new Color(0.22f, 0.24f, 0.29f, 0.98f),
                new Color(0.90f, 0.94f, 1f, 1f),
                true
            );
        }

        ConfigurePill(
            refs.HeroPill,
            GetHeroBadgeStyle(run.Hero).ShortCode,
            GetHeroBadgeStyle(run.Hero).Background,
            GetHeroBadgeStyle(run.Hero).Text,
            true
        );

        ConfigureStatusPill(refs.StatusPill, run.RawStatus);

        ConfigurePill(
            refs.ProgressPill,
            $"{(run.Victories ?? 0)}/{(run.FinalDay?.ToString() ?? "?")}",
            new Color(0.20f, 0.24f, 0.31f, 0.98f),
            new Color(0.89f, 0.94f, 1f, 1f),
            true
        );
        ConfigureInfoChip(refs.HealthChip, HistoryPanelText.StatHealthShort(), run.MaxHealth?.ToString() ?? "--", new Color(0.63f, 0.98f, 0.35f, 1f));
        ConfigureInfoChip(refs.PrestigeChip, HistoryPanelText.StatPrestigeShort(), run.Prestige?.ToString() ?? "--", new Color(1f, 0.65f, 0.13f, 1f));
        ConfigureInfoChip(refs.LevelChip, HistoryPanelText.StatLevelShort(), run.Level?.ToString() ?? "--", new Color(0.36f, 0.79f, 1f, 1f));
        ConfigureInfoChip(refs.IncomeChip, HistoryPanelText.StatIncomeShort(), run.Income?.ToString() ?? "--", new Color(1f, 0.86f, 0.10f, 1f));
        ConfigureInfoChip(refs.GoldChip, HistoryPanelText.StatGoldShort(), run.Gold?.ToString() ?? "--", new Color(1f, 0.86f, 0.10f, 1f));
        ApplyRunRowState(refs, _runsList?.selectedIndex == index);
    }

    private VisualElement MakeBattleRow()
    {
        var row = CreateRowShell();
        row.style.marginTop = 4f;
        row.style.marginBottom = 4f;
        row.style.borderLeftWidth = 1f;
        row.style.borderRightWidth = 1f;
        row.style.borderTopWidth = 1f;
        row.style.borderBottomWidth = 1f;
        var accent = CreateAccentBar();
        row.Add(accent);
        var dayHost = new VisualElement();
        dayHost.style.width = 62f;
        dayHost.style.flexShrink = 0f;
        dayHost.style.alignItems = Align.Center;
        dayHost.style.justifyContent = Justify.Center;
        row.Add(dayHost);
        var dayBubble = CreateBattleDayBubble(dayHost);
        var content = CreateRowContent();
        content.style.paddingLeft = 4f;
        content.style.paddingTop = 8f;
        content.style.paddingBottom = 8f;
        content.style.justifyContent = Justify.Center;
        row.Add(content);

        var playerRow = CreateInfoChipRow(content, 6f, 0f);
        var opponentRankPill = CreateInlinePill(playerRow, 68f);
        SetFixedPillWidth(opponentRankPill, 80f);
        var playerSummaryChip = CreateInfoChip(playerRow, HistoryPanelText.PlayerSideShort(), 100f);
        playerSummaryChip.style.marginRight = 0f;
        playerSummaryChip.style.marginLeft = 8f;
        var playerSpacer = CreateSpacer();
        playerRow.Add(playerSpacer);
        var timeLabel = CreateRowCornerLabel(playerRow, 11);

        var opponentRow = CreateInfoChipRow(content, 6f, 6f);
        var opponentHeroPill = CreateInlinePill(opponentRow, 64f);
        SetFixedPillWidth(opponentHeroPill, 80f);
        var opponentSummaryChip = CreateInfoChip(opponentRow, HistoryPanelText.OpponentSideShort(), 100f);
        opponentSummaryChip.style.marginRight = 0f;
        opponentSummaryChip.style.marginLeft = 8f;
        var opponentName = CreateInlineText(opponentRow, 12, new Color(0.76f, 0.80f, 0.87f, 0.92f));
        opponentName.style.marginLeft = 10f;
        opponentName.style.flexGrow = 1f;
        opponentName.style.unityTextAlign = TextAnchor.MiddleRight;

        var refs = new BattleRowRefs(
            row,
            accent,
            dayBubble,
            timeLabel,
            opponentRankPill,
            playerSummaryChip,
            opponentHeroPill,
            opponentSummaryChip,
            opponentName
        );
        row.userData = refs;
        row.RegisterCallback<ClickEvent>(_ =>
        {
            if (!_suppressSelectionCallbacks && refs.Index >= 0)
                _selectBattle(refs.Index);
        });
        return row;
    }

    private void BindBattleRow(VisualElement element, int index)
    {
        if (_battleList?.itemsSource is not List<HistoryBattleRecord> items || index < 0 || index >= items.Count)
            return;

        var battle = items[index];
        var refs = (BattleRowRefs)element.userData;
        refs.Index = index;

        refs.DayBubble.text = battle.Day?.ToString() ?? "?";
        refs.Time.text = HistoryPanelFormatter.FormatTimestamp(battle.RecordedAtUtc);

        BindBattleRankPill(refs.OpponentRankPill, battle.OpponentRank, battle.OpponentRating);
        ConfigureInfoChip(
            refs.PlayerSummaryChip,
            HistoryPanelText.PlayerSideShort(),
            HistoryPanelText.BoardSummary(
                battle.PreviewData.PlayerBoard.ItemCards.Count,
                battle.PreviewData.PlayerBoard.SkillCards.Count
            ),
            new Color(0.44f, 0.76f, 1f, 1f)
        );

        BindHeroPill(refs.OpponentHeroPill, battle.OpponentHero);
        ConfigureInfoChip(
            refs.OpponentSummaryChip,
            HistoryPanelText.OpponentSideShort(),
            HistoryPanelText.BoardSummary(
                battle.PreviewData.OpponentBoard.ItemCards.Count,
                battle.PreviewData.OpponentBoard.SkillCards.Count
            ),
            new Color(0.96f, 0.77f, 0.39f, 1f)
        );
        refs.OpponentName.text = battle.OpponentName ?? string.Empty;
        refs.OpponentName.style.display =
            string.IsNullOrWhiteSpace(refs.OpponentName.text) ? DisplayStyle.None : DisplayStyle.Flex;
        ApplyBattleRowState(refs, _battleList?.selectedIndex == index, battle);
    }

    private static VisualElement CreateSectionPanel(float? width)
    {
        var panel = new VisualElement();
        panel.style.backgroundColor = new Color(0.11f, 0.13f, 0.18f, 0.98f);
        panel.style.borderTopLeftRadius = 10f;
        panel.style.borderTopRightRadius = 10f;
        panel.style.borderBottomLeftRadius = 10f;
        panel.style.borderBottomRightRadius = 10f;
        panel.style.paddingLeft = 14f;
        panel.style.paddingRight = 14f;
        panel.style.paddingTop = 14f;
        panel.style.paddingBottom = 14f;
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.overflow = Overflow.Hidden;
        if (width.HasValue)
            panel.style.width = width.Value;
        return panel;
    }

    private static VisualElement CreateListFrame(VisualElement content)
    {
        var frame = new VisualElement();
        frame.style.flexGrow = 1f;
        frame.style.flexShrink = 1f;
        frame.style.minHeight = 0f;
        frame.style.marginTop = 10f;
        frame.style.paddingLeft = 6f;
        frame.style.paddingRight = 6f;
        frame.style.paddingTop = 6f;
        frame.style.paddingBottom = 6f;
        frame.style.backgroundColor = new Color(0.09f, 0.11f, 0.15f, 0.96f);
        frame.style.borderTopLeftRadius = 10f;
        frame.style.borderTopRightRadius = 10f;
        frame.style.borderBottomLeftRadius = 10f;
        frame.style.borderBottomRightRadius = 10f;
        frame.style.borderLeftWidth = 1f;
        frame.style.borderRightWidth = 1f;
        frame.style.borderTopWidth = 1f;
        frame.style.borderBottomWidth = 1f;
        frame.style.borderLeftColor = new Color(0.24f, 0.29f, 0.38f, 0.55f);
        frame.style.borderRightColor = new Color(0.24f, 0.29f, 0.38f, 0.55f);
        frame.style.borderTopColor = new Color(0.24f, 0.29f, 0.38f, 0.55f);
        frame.style.borderBottomColor = new Color(0.24f, 0.29f, 0.38f, 0.55f);
        frame.style.overflow = Overflow.Hidden;

        content.style.marginTop = 0f;
        frame.Add(content);
        return frame;
    }

    private static Label CreateSectionTitle(string text)
    {
        var label = CreateLabel(20, FontStyle.Bold, new Color(0.76f, 0.91f, 1f, 1f));
        label.text = text.ToUpperInvariant();
        label.style.height = 32f;
        label.style.minHeight = 32f;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        return label;
    }

    private static Label CreateChip()
    {
        var chip = CreateLabel(12, FontStyle.Bold, new Color(0.95f, 0.96f, 0.98f, 1f));
        chip.style.backgroundColor = new Color(0.14f, 0.18f, 0.23f, 0.96f);
        chip.style.minWidth = 86f;
        chip.style.height = 32f;
        chip.style.paddingLeft = 8f;
        chip.style.paddingRight = 8f;
        chip.style.unityTextAlign = TextAnchor.MiddleCenter;
        chip.style.borderTopLeftRadius = 10f;
        chip.style.borderTopRightRadius = 10f;
        chip.style.borderBottomLeftRadius = 10f;
        chip.style.borderBottomRightRadius = 10f;
        return chip;
    }

    private static Label CreateLabel(int fontSize, FontStyle fontStyle, Color color)
    {
        var label = new Label();
        label.style.fontSize = fontSize;
        label.style.unityFont = GetUiFont();
        label.style.unityFontStyleAndWeight = fontStyle;
        label.style.color = color;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        return label;
    }

    private static Button CreateButton(string text, Action onClick, float width, float height)
    {
        var button = new Button(() => onClick())
        {
            text = text,
        };
        button.style.width = width;
        button.style.minWidth = width;
        button.style.maxWidth = width;
        button.style.height = height;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        button.style.unityFont = GetUiFont();
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        button.style.paddingLeft = 0f;
        button.style.paddingRight = 0f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.backgroundColor = new Color(0.23f, 0.27f, 0.32f, 0.98f);
        button.style.color = Color.white;
        button.style.borderLeftWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderTopWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftColor = new Color(0.34f, 0.40f, 0.48f, 0.55f);
        button.style.borderRightColor = new Color(0.34f, 0.40f, 0.48f, 0.55f);
        button.style.borderTopColor = new Color(0.34f, 0.40f, 0.48f, 0.55f);
        button.style.borderBottomColor = new Color(0.34f, 0.40f, 0.48f, 0.55f);
        button.style.borderTopLeftRadius = 10f;
        button.style.borderTopRightRadius = 10f;
        button.style.borderBottomLeftRadius = 10f;
        button.style.borderBottomRightRadius = 10f;
        var textElement = button.Q<TextElement>();
        if (textElement != null)
        {
            textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            textElement.style.flexGrow = 1f;
            textElement.style.unityFont = GetUiFont();
        }
        return button;
    }

    private static void StyleButton(Button button, Color background, Color textColor)
    {
        button.style.backgroundColor = background;
        button.style.color = textColor;
        var border = new Color(
            Mathf.Clamp01(background.r + 0.08f),
            Mathf.Clamp01(background.g + 0.08f),
            Mathf.Clamp01(background.b + 0.08f),
            0.58f
        );
        button.style.borderLeftColor = border;
        button.style.borderRightColor = border;
        button.style.borderTopColor = border;
        button.style.borderBottomColor = border;
    }

    private static Font GetUiFont()
    {
        _uiFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return _uiFont;
    }

    private static VisualElement CreateSpacer()
    {
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;
        return spacer;
    }

    private static VisualElement CreateRowShell()
    {
        var row = new VisualElement();
        row.style.height = Length.Percent(100);
        row.style.marginBottom = 6f;
        row.style.backgroundColor = new Color(0.11f, 0.14f, 0.18f, 0.98f);
        row.style.borderTopLeftRadius = 8f;
        row.style.borderTopRightRadius = 8f;
        row.style.borderBottomLeftRadius = 8f;
        row.style.borderBottomRightRadius = 8f;
        row.style.flexDirection = FlexDirection.Row;
        row.style.overflow = Overflow.Hidden;
        return row;
    }

    private static VisualElement CreateAccentBar()
    {
        var accent = new VisualElement();
        accent.style.width = 6f;
        accent.style.flexShrink = 0f;
        return accent;
    }

    private static VisualElement CreateRowContent()
    {
        var content = new VisualElement();
        content.style.flexGrow = 1f;
        content.style.paddingLeft = 12f;
        content.style.paddingRight = 12f;
        content.style.paddingTop = 9f;
        content.style.paddingBottom = 9f;
        content.style.flexDirection = FlexDirection.Column;
        return content;
    }

    private static VisualElement CreateRowTopRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        return row;
    }

    private static Label CreateRowTitle(VisualElement row)
    {
        var label = CreateLabel(14, FontStyle.Bold, Color.white);
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.flexGrow = 1f;
        row.Add(label);
        return label;
    }

    private static Label CreateRowCornerLabel(VisualElement row, int fontSize)
    {
        var label = CreateLabel(fontSize, FontStyle.Normal, new Color(0.72f, 0.78f, 0.85f, 0.92f));
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.flexShrink = 0f;
        label.style.marginLeft = 8f;
        row.Add(label);
        return label;
    }

    private static Label CreateInlineText(VisualElement row, int fontSize, Color color)
    {
        var label = CreateLabel(fontSize, FontStyle.Normal, color);
        label.style.whiteSpace = WhiteSpace.NoWrap;
        row.Add(label);
        return label;
    }

    private static Label CreateRowPill(VisualElement row)
    {
        var pill = CreateLabel(10, FontStyle.Bold, Color.white);
        pill.style.minWidth = 58f;
        pill.style.height = 20f;
        pill.style.paddingLeft = 8f;
        pill.style.paddingRight = 8f;
        pill.style.marginLeft = 8f;
        pill.style.unityTextAlign = TextAnchor.MiddleCenter;
        pill.style.borderTopLeftRadius = 10f;
        pill.style.borderTopRightRadius = 10f;
        pill.style.borderBottomLeftRadius = 10f;
        pill.style.borderBottomRightRadius = 10f;
        row.Add(pill);
        return pill;
    }

    private static Label CreateRowMeta(VisualElement row)
    {
        var label = CreateLabel(12, FontStyle.Normal, new Color(0.82f, 0.86f, 0.92f, 0.96f));
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.marginTop = 4f;
        row.Add(label);
        return label;
    }

    private static Label CreateRowDetail(VisualElement row)
    {
        var label = CreateLabel(11, FontStyle.Normal, new Color(0.70f, 0.75f, 0.83f, 0.90f));
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.marginTop = 4f;
        row.Add(label);
        return label;
    }

    private static VisualElement CreateInfoChipRow(VisualElement parent, float spacing, float marginTop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = marginTop;
        row.style.flexWrap = Wrap.NoWrap;
        parent.Add(row);
        return row;
    }

    private static Label CreateInfoChip(VisualElement row, string label, float minWidth)
    {
        var chip = CreateLabel(10, FontStyle.Bold, Color.white);
        chip.text = label;
        chip.style.minWidth = minWidth;
        chip.style.height = 22f;
        chip.style.marginRight = 6f;
        chip.style.paddingLeft = 8f;
        chip.style.paddingRight = 8f;
        chip.style.unityTextAlign = TextAnchor.MiddleCenter;
        chip.style.borderTopLeftRadius = 7f;
        chip.style.borderTopRightRadius = 7f;
        chip.style.borderBottomLeftRadius = 7f;
        chip.style.borderBottomRightRadius = 7f;
        row.Add(chip);
        return chip;
    }

    private static VisualElement CreateRunBadgeRow(VisualElement parent)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = 6f;
        parent.Add(row);
        return row;
    }

    private static Label CreateInlinePill(VisualElement row, float minWidth)
    {
        var pill = CreateLabel(10, FontStyle.Bold, Color.white);
        pill.style.minWidth = minWidth;
        pill.style.height = 20f;
        pill.style.paddingLeft = 8f;
        pill.style.paddingRight = 8f;
        pill.style.marginRight = 6f;
        pill.style.unityTextAlign = TextAnchor.MiddleCenter;
        pill.style.borderTopLeftRadius = 10f;
        pill.style.borderTopRightRadius = 10f;
        pill.style.borderBottomLeftRadius = 10f;
        pill.style.borderBottomRightRadius = 10f;
        row.Add(pill);
        return pill;
    }

    private static void SetFixedPillWidth(Label pill, float width)
    {
        pill.style.minWidth = width;
        pill.style.maxWidth = width;
        pill.style.width = width;
    }

    private static void SetEqualChipWidth(Label chip, bool isLast = false)
    {
        chip.style.flexGrow = 1f;
        chip.style.flexShrink = 1f;
        chip.style.flexBasis = 0f;
        chip.style.minWidth = 0f;
        chip.style.marginRight = isLast ? 0f : 6f;
    }

    private static Label CreateDayBubble(VisualElement parent)
    {
        var bubble = CreateLabel(11, FontStyle.Bold, Color.white);
        bubble.style.width = 40f;
        bubble.style.height = 40f;
        bubble.style.unityTextAlign = TextAnchor.MiddleCenter;
        bubble.style.borderTopLeftRadius = 20f;
        bubble.style.borderTopRightRadius = 20f;
        bubble.style.borderBottomLeftRadius = 20f;
        bubble.style.borderBottomRightRadius = 20f;
        bubble.style.backgroundColor = new Color(0.24f, 0.28f, 0.36f, 0.98f);
        bubble.style.borderLeftWidth = 1f;
        bubble.style.borderRightWidth = 1f;
        bubble.style.borderTopWidth = 1f;
        bubble.style.borderBottomWidth = 1f;
        bubble.style.borderLeftColor = new Color(0.52f, 0.60f, 0.72f, 0.45f);
        bubble.style.borderRightColor = new Color(0.52f, 0.60f, 0.72f, 0.45f);
        bubble.style.borderTopColor = new Color(0.52f, 0.60f, 0.72f, 0.45f);
        bubble.style.borderBottomColor = new Color(0.52f, 0.60f, 0.72f, 0.45f);
        parent.Add(bubble);
        return bubble;
    }

    private static Label CreateBattleDayBubble(VisualElement parent)
    {
        var bubble = CreateDayBubble(parent);
        bubble.style.fontSize = 14f;
        return bubble;
    }

    private static void ConfigurePill(
        Label pill,
        string text,
        Color background,
        Color textColor,
        bool visible
    )
    {
        pill.text = text;
        pill.style.backgroundColor = background;
        pill.style.color = textColor;
        pill.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void ConfigureInfoChip(Label chip, string label, string value, Color accent)
    {
        chip.text = $"{label} {value}";
        chip.style.backgroundColor = new Color(
            Mathf.Lerp(0.14f, accent.r, 0.10f),
            Mathf.Lerp(0.16f, accent.g, 0.10f),
            Mathf.Lerp(0.20f, accent.b, 0.10f),
            0.98f
        );
        chip.style.color = accent;
        chip.style.borderLeftWidth = 2f;
        chip.style.borderLeftColor = new Color(accent.r, accent.g, accent.b, 0.95f);
    }

    private static void ConfigureStatusPill(Label pill, string rawStatus)
    {
        var status = HistoryPanelFormatter.FormatRunStatus(rawStatus);
        var background =
            string.Equals(rawStatus, "completed", StringComparison.OrdinalIgnoreCase)
                ? new Color(0.16f, 0.30f, 0.24f, 0.74f)
                : string.Equals(rawStatus, "abandoned", StringComparison.OrdinalIgnoreCase)
                    ? new Color(0.31f, 0.22f, 0.15f, 0.72f)
                    : new Color(0.18f, 0.24f, 0.33f, 0.72f);
        var text =
            string.Equals(rawStatus, "completed", StringComparison.OrdinalIgnoreCase)
                ? new Color(0.82f, 0.98f, 0.90f, 0.90f)
                : string.Equals(rawStatus, "abandoned", StringComparison.OrdinalIgnoreCase)
                    ? new Color(0.99f, 0.90f, 0.85f, 0.88f)
                    : new Color(0.84f, 0.92f, 1f, 0.88f);
        ConfigurePill(pill, status, background, text, true);
    }

    private static void BindHeroPill(Label pill, string? rawHero)
    {
        var hero = HistoryPanelFormatter.FormatOpponentHero(rawHero);
        if (string.IsNullOrWhiteSpace(hero))
        {
            ConfigurePill(pill, string.Empty, Color.clear, Color.clear, false);
            return;
        }

        var heroStyle = GetHeroBadgeStyle(hero);
        ConfigurePill(pill, heroStyle.ShortCode, heroStyle.Background, heroStyle.Text, true);
    }

    private static void BindBattleRankPill(Label pill, string? rawRank, int? rating)
    {
        var rank = HistoryPanelFormatter.NormalizeRank(rawRank);
        if (string.Equals(rank, "Legendary", StringComparison.OrdinalIgnoreCase))
        {
            ConfigurePill(pill, HistoryPanelText.RankLabel(rank, rating), ColorFromRgb(241, 54, 41), Color.white, true);
            return;
        }

        if (string.IsNullOrWhiteSpace(rank))
        {
            ConfigurePill(pill, string.Empty, Color.clear, Color.clear, false);
            return;
        }

        var palette = GetRankBadgePalette(rank);
        ConfigurePill(pill, HistoryPanelText.RankLabel(rank), palette.Background, palette.Text, true);
    }

    private static void BindRunOutcomeBubble(Label bubble, HistoryRunRecord run)
    {
        var tier = HistoryPanelFormatter.GetRunOutcomeTier(run) ?? RunOutcomeTier.Misfortune;
        Color background;
        Color border;

        if (tier == RunOutcomeTier.Diamond)
        {
            background = new Color(0.15f, 0.34f, 0.46f, 0.98f);
            border = new Color(0.42f, 0.78f, 0.98f, 0.42f);
        }
        else if (tier == RunOutcomeTier.Gold)
        {
            background = new Color(0.37f, 0.28f, 0.10f, 0.98f);
            border = new Color(0.86f, 0.68f, 0.24f, 0.42f);
        }
        else if (tier == RunOutcomeTier.Silver)
        {
            background = new Color(0.31f, 0.34f, 0.40f, 0.98f);
            border = new Color(0.74f, 0.80f, 0.90f, 0.42f);
        }
        else if (tier == RunOutcomeTier.Bronze)
        {
            background = new Color(0.36f, 0.22f, 0.15f, 0.98f);
            border = new Color(0.78f, 0.52f, 0.36f, 0.42f);
        }
        else
        {
            background = new Color(0.25f, 0.18f, 0.18f, 0.98f);
            border = new Color(0.64f, 0.38f, 0.38f, 0.42f);
        }

        bubble.text = HistoryPanelText.RunOutcomeBubbleLabel(tier);
        bubble.style.backgroundColor = background;
        bubble.style.borderLeftColor = border;
        bubble.style.borderRightColor = border;
        bubble.style.borderTopColor = border;
        bubble.style.borderBottomColor = border;
    }

    private static void RefreshTabButton(Button button, bool selected)
    {
        if (selected)
        {
            StyleButton(button, new Color(0.78f, 0.60f, 0.24f, 0.98f), new Color(0.10f, 0.07f, 0.03f, 1f));
            return;
        }

        StyleButton(button, new Color(0.25f, 0.30f, 0.37f, 0.98f), Color.white);
    }

    private static void RefreshGhostFilterButton(Button button, bool selected)
    {
        if (selected)
        {
            StyleButton(button, new Color(0.78f, 0.60f, 0.24f, 0.98f), new Color(0.10f, 0.07f, 0.03f, 1f));
            return;
        }

        StyleButton(button, new Color(0.19f, 0.22f, 0.27f, 0.98f), Color.white);
    }

    private static void RefreshDeleteButton(Button button, string text, bool enabled)
    {
        var isConfirmState = string.Equals(text, HistoryPanelText.DeleteConfirm(), StringComparison.Ordinal);
        if (isConfirmState)
        {
            StyleButton(button, new Color(0.60f, 0.19f, 0.16f, 0.98f), new Color(1f, 0.94f, 0.92f, 1f));
            return;
        }

        if (!enabled)
        {
            StyleButton(button, new Color(0.28f, 0.20f, 0.19f, 0.88f), new Color(0.86f, 0.82f, 0.80f, 0.88f));
            return;
        }

        StyleButton(button, new Color(0.40f, 0.24f, 0.20f, 0.98f), new Color(1f, 0.93f, 0.90f, 1f));
    }

    private static string ShortenBattleId(string battleId)
    {
        return string.IsNullOrWhiteSpace(battleId)
            ? "-"
            : battleId.Length <= 12
                ? battleId
                : battleId[..12];
    }

    private static void ApplyRunRowState(RunRowRefs refs, bool selected)
    {
        refs.Root.style.backgroundColor = selected
            ? new Color(0.17f, 0.24f, 0.32f, 0.99f)
            : new Color(0.11f, 0.14f, 0.18f, 0.98f);
        refs.Accent.style.backgroundColor = selected
            ? new Color(0.46f, 0.70f, 0.92f, 0.94f)
            : new Color(0.24f, 0.31f, 0.39f, 0.96f);
        var borderColor = selected
            ? new Color(0.37f, 0.57f, 0.79f, 0.56f)
            : new Color(0.28f, 0.35f, 0.45f, 0.40f);
        refs.Root.style.borderLeftColor = borderColor;
        refs.Root.style.borderRightColor = borderColor;
        refs.Root.style.borderTopColor = borderColor;
        refs.Root.style.borderBottomColor = borderColor;
        refs.OutcomeBubble.style.borderLeftColor = borderColor;
        refs.OutcomeBubble.style.borderRightColor = borderColor;
        refs.OutcomeBubble.style.borderTopColor = borderColor;
        refs.OutcomeBubble.style.borderBottomColor = borderColor;
        refs.OutcomeBubble.style.opacity = selected ? 1f : 0.96f;
    }

    private static void ApplyBattleRowState(BattleRowRefs refs, bool selected, HistoryBattleRecord battle)
    {
        var isWin = HistoryPanelFormatter.IsBattleWin(battle);
        var isLoss = HistoryPanelFormatter.IsBattleLoss(battle);

        refs.Root.style.backgroundColor =
            selected
                ? isWin ? new Color(0.13f, 0.23f, 0.22f, 0.99f) :
                  isLoss ? new Color(0.24f, 0.18f, 0.16f, 0.99f) :
                  new Color(0.18f, 0.24f, 0.31f, 0.99f)
                : isWin ? new Color(0.10f, 0.15f, 0.16f, 0.98f) :
                  isLoss ? new Color(0.15f, 0.13f, 0.15f, 0.98f) :
                  new Color(0.13f, 0.15f, 0.18f, 0.98f);

        refs.Accent.style.backgroundColor =
            isWin ? new Color(0.23f, 0.54f, 0.47f, 0.95f) :
            isLoss ? new Color(0.63f, 0.36f, 0.24f, 0.95f) :
            new Color(0.34f, 0.47f, 0.64f, 0.95f);
        var borderColor =
            isWin ? new Color(0.22f, 0.44f, 0.40f, 0.42f) :
            isLoss ? new Color(0.44f, 0.27f, 0.20f, 0.42f) :
            new Color(0.24f, 0.31f, 0.41f, 0.42f);
        refs.Root.style.borderLeftColor = borderColor;
        refs.Root.style.borderRightColor = borderColor;
        refs.Root.style.borderTopColor = borderColor;
        refs.Root.style.borderBottomColor = borderColor;
        refs.DayBubble.style.backgroundColor =
            isWin ? new Color(0.13f, 0.28f, 0.23f, 0.98f) :
            isLoss ? new Color(0.33f, 0.20f, 0.15f, 0.98f) :
            new Color(0.18f, 0.23f, 0.31f, 0.98f);
        refs.DayBubble.style.borderLeftColor = borderColor;
        refs.DayBubble.style.borderRightColor = borderColor;
        refs.DayBubble.style.borderTopColor = borderColor;
        refs.DayBubble.style.borderBottomColor = borderColor;
    }

    private sealed class RowRefs
    {
        public RowRefs(
            VisualElement root,
            VisualElement accent,
            Label title,
            Label pill,
            Label meta,
            Label detail
        )
        {
            Root = root;
            Accent = accent;
            Title = title;
            Pill = pill;
            Meta = meta;
            Detail = detail;
            Index = -1;
        }

        public VisualElement Root { get; }

        public VisualElement Accent { get; }

        public Label Title { get; }

        public Label Pill { get; }

        public Label Meta { get; }

        public Label Detail { get; }

        public int Index { get; set; }
    }

    private sealed class RunRowRefs
    {
        public RunRowRefs(
            VisualElement root,
            VisualElement accent,
            Label outcomeBubble,
            Label rankPill,
            Label heroPill,
            Label progressPill,
            Label statusPill,
            Label time,
            VisualElement statRow,
            Label healthChip,
            Label prestigeChip,
            Label levelChip,
            Label incomeChip,
            Label goldChip
        )
        {
            Root = root;
            Accent = accent;
            OutcomeBubble = outcomeBubble;
            RankPill = rankPill;
            HeroPill = heroPill;
            ProgressPill = progressPill;
            StatusPill = statusPill;
            Time = time;
            StatRow = statRow;
            HealthChip = healthChip;
            PrestigeChip = prestigeChip;
            LevelChip = levelChip;
            IncomeChip = incomeChip;
            GoldChip = goldChip;
            Index = -1;
        }

        public VisualElement Root { get; }

        public VisualElement Accent { get; }

        public Label OutcomeBubble { get; }

        public Label RankPill { get; }

        public Label HeroPill { get; }

        public Label ProgressPill { get; }

        public Label StatusPill { get; }

        public Label Time { get; }

        public VisualElement StatRow { get; }

        public Label HealthChip { get; }

        public Label PrestigeChip { get; }

        public Label LevelChip { get; }

        public Label IncomeChip { get; }

        public Label GoldChip { get; }

        public int Index { get; set; }
    }

    private sealed class BattleRowRefs
    {
        public BattleRowRefs(
            VisualElement root,
            VisualElement accent,
            Label dayBubble,
            Label time,
            Label opponentRankPill,
            Label playerSummaryChip,
            Label opponentHeroPill,
            Label opponentSummaryChip,
            Label opponentName
        )
        {
            Root = root;
            Accent = accent;
            DayBubble = dayBubble;
            Time = time;
            OpponentRankPill = opponentRankPill;
            PlayerSummaryChip = playerSummaryChip;
            OpponentHeroPill = opponentHeroPill;
            OpponentSummaryChip = opponentSummaryChip;
            OpponentName = opponentName;
            Index = -1;
        }

        public VisualElement Root { get; }

        public VisualElement Accent { get; }

        public Label DayBubble { get; }

        public Label Time { get; }

        public Label OpponentRankPill { get; }

        public Label PlayerSummaryChip { get; }

        public Label OpponentHeroPill { get; }

        public Label OpponentSummaryChip { get; }

        public Label OpponentName { get; }

        public int Index { get; set; }
    }

    private readonly struct HeroBadgeStyle
    {
        public HeroBadgeStyle(string shortCode, Color background, Color text)
        {
            ShortCode = shortCode;
            Background = background;
            Text = text;
        }

        public string ShortCode { get; }

        public Color Background { get; }

        public Color Text { get; }
    }

    private static Color GetRunAchievementBackground(string achievement)
    {
        return achievement switch
        {
            "PERFECT" => new Color(0.36f, 0.28f, 0.10f, 0.98f),
            "GOLD" => new Color(0.41f, 0.31f, 0.12f, 0.98f),
            "SILVER" => new Color(0.36f, 0.38f, 0.44f, 0.98f),
            "BRONZE" => new Color(0.38f, 0.25f, 0.18f, 0.98f),
            _ => new Color(0.24f, 0.18f, 0.18f, 0.98f),
        };
    }

    private static Color GetRunAchievementText(string achievement)
    {
        return achievement switch
        {
            "PERFECT" => new Color(1f, 0.94f, 0.71f, 1f),
            "GOLD" => new Color(0.99f, 0.90f, 0.66f, 1f),
            "SILVER" => new Color(0.94f, 0.97f, 1f, 1f),
            "BRONZE" => new Color(0.98f, 0.88f, 0.80f, 1f),
            _ => new Color(0.96f, 0.84f, 0.84f, 1f),
        };
    }

    private static string? FormatRank(string? rawRank)
    {
        if (string.IsNullOrWhiteSpace(rawRank))
            return null;

        var trimmed = rawRank.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
    }

    private static (Color Background, Color Text) GetRankBadgePalette(string rank)
    {
        return rank switch
        {
            "Bronze" => (new Color(0.39f, 0.24f, 0.17f, 0.98f), new Color(0.98f, 0.88f, 0.80f, 1f)),
            "Silver" => (new Color(0.34f, 0.37f, 0.43f, 0.98f), new Color(0.94f, 0.97f, 1f, 1f)),
            "Gold" => (new Color(0.41f, 0.31f, 0.12f, 0.98f), new Color(0.99f, 0.90f, 0.66f, 1f)),
            "Diamond" => (new Color(0.18f, 0.35f, 0.47f, 0.98f), new Color(0.84f, 0.97f, 1f, 1f)),
            _ => (new Color(0.24f, 0.28f, 0.36f, 0.98f), new Color(0.89f, 0.94f, 1f, 1f)),
        };
    }

    private static HeroBadgeStyle GetHeroBadgeStyle(string? heroName)
    {
        if (string.IsNullOrWhiteSpace(heroName))
            return new HeroBadgeStyle("UNK", new Color(0.20f, 0.29f, 0.38f, 0.95f), Color.white);

        return heroName.Trim() switch
        {
            "Vanessa" => BuildHeroBadgeStyle("VAN", 192, 33, 33),
            "Pygmalien" => BuildHeroBadgeStyle("PYG", 39, 103, 192),
            "Dooley" => BuildHeroBadgeStyle("DOO", 225, 154, 8),
            "Mak" => BuildHeroBadgeStyle("MAK", 190, 230, 91),
            "Jules" => BuildHeroBadgeStyle("JUL", 180, 52, 236),
            "Karnok" => BuildHeroBadgeStyle("KAR", 59, 136, 156),
            "Stelle" => BuildHeroBadgeStyle("STE", 255, 235, 24),
            _ => BuildHeroBadgeStyle(
                heroName.Length <= 3
                    ? heroName.ToUpperInvariant()
                    : heroName[..3].ToUpperInvariant(),
                57,
                73,
                97
            ),
        };
    }

    private static HeroBadgeStyle BuildHeroBadgeStyle(string shortCode, int r, int g, int b)
    {
        var background = ColorFromRgb(r, g, b);
        var luminance = (0.299f * background.r) + (0.587f * background.g) + (0.114f * background.b);
        var text = luminance > 0.62f ? new Color(0.10f, 0.12f, 0.15f, 1f) : Color.white;
        return new HeroBadgeStyle(shortCode, background, text);
    }

    private static Color ColorFromRgb(int r, int g, int b)
    {
        return new Color(r / 255f, g / 255f, b / 255f, 0.98f);
    }
}
