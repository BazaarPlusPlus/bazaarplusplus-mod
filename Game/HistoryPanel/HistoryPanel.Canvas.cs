#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private const int CanvasSortingOrder = 26;
    private const int PillMaxVisibleCharacters = 10;
    private const float PanelWidth = 1280f;
    private const float PanelHeight = 960f;
    private const float ListColumnWidth = 392f;
    private const float PreviewSectionHeight = 256f;
    private const float GhostFilterButtonHeight = 22f;

    private static Sprite? _roundedSprite;
    private static TMP_FontAsset? _uiFont;

    private readonly List<ListItemView> _runItemViews = new();
    private readonly List<ListItemView> _battleItemViews = new();

    private GameObject? _canvasObject;
    private RectTransform? _panelRoot;
    private RectTransform? _runSectionPanel;
    private RectTransform? _ghostModeRoot;
    private RectTransform? _ghostBattleListContent;
    private RectTransform? _runsModeRoot;
    private RectTransform? _runListContent;
    private RectTransform? _runsBattleListContent;
    private TextMeshProUGUI? _runsBattleSectionSubtitle;
    private RawImage? _previewSurface;
    private TextMeshProUGUI? _previewStatusText;
    private TextMeshProUGUI? _previewDebugText;
    private TextMeshProUGUI? _countChipText;
    private TextMeshProUGUI? _battleChipText;
    private TextMeshProUGUI? _databaseChipText;
    private TextMeshProUGUI? _statusText;
    private TextMeshProUGUI? _runSectionTitle;
    private TextMeshProUGUI? _footerPrimaryText;
    private TextMeshProUGUI? _footerSecondaryText;
    private Button? _runsTabButton;
    private Image? _runsTabButtonBackground;
    private TextMeshProUGUI? _runsTabButtonLabel;
    private Button? _ghostTabButton;
    private Image? _ghostTabButtonBackground;
    private TextMeshProUGUI? _ghostTabButtonLabel;
    private Button? _syncGhostButton;
    private Image? _syncGhostButtonBackground;
    private TextMeshProUGUI? _syncGhostButtonLabel;
    private Button? _ghostFilterAllButton;
    private Image? _ghostFilterAllButtonBackground;
    private TextMeshProUGUI? _ghostFilterAllButtonLabel;
    private Button? _ghostFilterIWonButton;
    private Image? _ghostFilterIWonButtonBackground;
    private TextMeshProUGUI? _ghostFilterIWonButtonLabel;
    private Button? _ghostFilterILostButton;
    private Image? _ghostFilterILostButtonBackground;
    private TextMeshProUGUI? _ghostFilterILostButtonLabel;
    private Button? _dynamicPreviewButton;
    private Image? _dynamicPreviewButtonBackground;
    private TextMeshProUGUI? _dynamicPreviewButtonLabel;
    private Button? _replayButton;
    private Image? _replayButtonBackground;
    private TextMeshProUGUI? _replayButtonLabel;
    private Button? _deleteRunButton;
    private Image? _deleteRunButtonBackground;
    private TextMeshProUGUI? _deleteRunButtonLabel;

    private sealed class ListItemView
    {
        public int Index;
        public Image? Background;
    }

    private readonly struct BattlePalette
    {
        public BattlePalette(
            Color normal,
            Color selected,
            Color accent,
            Color badgeBg,
            Color badgeText
        )
        {
            Normal = normal;
            Selected = selected;
            Accent = accent;
            BadgeBg = badgeBg;
            BadgeText = badgeText;
        }

        public Color Normal { get; }
        public Color Selected { get; }
        public Color Accent { get; }
        public Color BadgeBg { get; }
        public Color BadgeText { get; }
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

    private void EnsureUi()
    {
        if (_canvasObject != null)
            return;

        _canvasObject = new GameObject(
            "HistoryPanelCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        _canvasObject.transform.SetParent(transform, false);

        var canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;

        var scaler = _canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.55f;

        var canvasRect = (RectTransform)_canvasObject.transform;
        StretchToParent(canvasRect, 0f, 0f, 0f, 0f);

        var backdrop = CreateRect("Backdrop", _canvasObject.transform);
        StretchToParent(backdrop, 0f, 0f, 0f, 0f);
        var backdropImage = AddImage(backdrop.gameObject, new Color(0.02f, 0.03f, 0.05f, 0.82f));
        backdropImage.raycastTarget = true;

        _panelRoot = CreateRect("PanelRoot", _canvasObject.transform);
        _panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRoot.pivot = new Vector2(0.5f, 0.5f);
        _panelRoot.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        var bg = AddImage(_panelRoot.gameObject, new Color(0.08f, 0.10f, 0.13f, 0.985f));
        bg.raycastTarget = true;

        var shadow = CreateRect("Shadow", _panelRoot);
        StretchToParent(shadow, -14f, -14f, -14f, -14f);
        shadow.SetAsFirstSibling();
        AddImage(shadow.gameObject, new Color(0.01f, 0.01f, 0.02f, 0.35f));

        var glow = CreateRect("HeaderGlow", _panelRoot);
        glow.anchorMin = new Vector2(0f, 1f);
        glow.anchorMax = new Vector2(1f, 1f);
        glow.pivot = new Vector2(0.5f, 1f);
        glow.sizeDelta = new Vector2(0f, 170f);
        AddImage(glow.gameObject, new Color(0.79f, 0.61f, 0.22f, 0.08f));

        BuildHeader();
        BuildContent();
        BuildFooter();
    }

    private void DisposeUi()
    {
        if (_canvasObject == null)
            return;

        Destroy(_canvasObject);
        _canvasObject = null;
        _panelRoot = null;
        _runSectionPanel = null;
        _ghostModeRoot = null;
        _ghostBattleListContent = null;
        _runsModeRoot = null;
        _runListContent = null;
        _runsBattleListContent = null;
        _runsBattleSectionSubtitle = null;
        _previewSurface = null;
        _previewStatusText = null;
        _previewDebugText = null;
        _countChipText = null;
        _battleChipText = null;
        _databaseChipText = null;
        _statusText = null;
        _runSectionTitle = null;
        _footerPrimaryText = null;
        _footerSecondaryText = null;
        _runsTabButton = null;
        _runsTabButtonBackground = null;
        _runsTabButtonLabel = null;
        _ghostTabButton = null;
        _ghostTabButtonBackground = null;
        _ghostTabButtonLabel = null;
        _syncGhostButton = null;
        _syncGhostButtonBackground = null;
        _syncGhostButtonLabel = null;
        _ghostFilterAllButton = null;
        _ghostFilterAllButtonBackground = null;
        _ghostFilterAllButtonLabel = null;
        _ghostFilterIWonButton = null;
        _ghostFilterIWonButtonBackground = null;
        _ghostFilterIWonButtonLabel = null;
        _ghostFilterILostButton = null;
        _ghostFilterILostButtonBackground = null;
        _ghostFilterILostButtonLabel = null;
        _dynamicPreviewButton = null;
        _dynamicPreviewButtonBackground = null;
        _dynamicPreviewButtonLabel = null;
        _replayButton = null;
        _replayButtonBackground = null;
        _replayButtonLabel = null;
        _deleteRunButton = null;
        _deleteRunButtonBackground = null;
        _deleteRunButtonLabel = null;
        _runItemViews.Clear();
        _battleItemViews.Clear();
    }

    private void SetUiVisible(bool visible)
    {
        if (_canvasObject != null && _canvasObject.activeSelf != visible)
            _canvasObject.SetActive(visible);
    }

    private void RefreshUi()
    {
        if (_panelRoot == null)
            return;

        var canReplaySelectedBattle = CanReplaySelectedBattle(out var replayUnavailableReason);

        if (_countChipText != null)
            _countChipText.text =
                _sectionMode == HistorySectionMode.Ghost
                    ? $"{FilteredGhostBattles.Count} Ghost"
                    : $"{_runs.Count} Runs";
        if (_battleChipText != null)
            _battleChipText.text =
                _sectionMode == HistorySectionMode.Ghost
                    ? $"{FilteredGhostBattles.Count} Battles"
                    : $"{_battles.Count} Battles";
        if (_databaseChipText != null)
            _databaseChipText.text = $"DB {GetDatabaseChipText()}";

        if (_runSectionTitle != null)
            _runSectionTitle.text = (
                _sectionMode == HistorySectionMode.Ghost ? "Ghost" : "Runs"
            ).ToUpperInvariant();

        if (_statusText != null)
        {
            _statusText.text = _statusMessage ?? string.Empty;
            _statusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(_statusText.text));
        }

        if (_runSectionPanel != null)
            _runSectionPanel.gameObject.SetActive(_sectionMode != HistorySectionMode.Ghost);

        if (_ghostModeRoot != null)
            _ghostModeRoot.gameObject.SetActive(_sectionMode == HistorySectionMode.Ghost);

        if (_runsModeRoot != null)
        {
            _runsModeRoot.gameObject.SetActive(_sectionMode != HistorySectionMode.Ghost);
            if (_runsBattleSectionSubtitle != null)
            {
                _runsBattleSectionSubtitle.text =
                    SelectedRun == null
                        ? "Select a run to inspect its recorded battles."
                        : $"{SelectedRun.Hero} | {HistoryPanelFormatter.FormatDayOnly(SelectedRun.FinalDay)}";
            }
        }

        if (_footerPrimaryText != null)
        {
            _footerPrimaryText.text =
                ActiveSelectedBattle == null
                    ? "No battle selected"
                    : $"{HistoryPanelFormatter.FormatBattleResult(ActiveSelectedBattle)} | {HistoryPanelFormatter.FormatDayOnly(ActiveSelectedBattle.Day)} | {ActiveSelectedBattle.OpponentName ?? "Unknown Opponent"}";
        }

        if (_footerSecondaryText != null)
        {
            var selectedBattleTimestamp =
                ActiveSelectedBattle == null
                    ? null
                    : HistoryPanelFormatter.FormatTimestamp(ActiveSelectedBattle.RecordedAtUtc);
            var selectedBattleTimestampText = selectedBattleTimestamp ?? string.Empty;
            var battleSummary =
                ActiveSelectedBattle == null
                    ? "Select one battle to inspect it, then use Replay when you want to jump back into it."
                : canReplaySelectedBattle
                    ? string.IsNullOrWhiteSpace(ActiveSelectedBattle.SnapshotSummary)
                            ? selectedBattleTimestampText
                        : $"{selectedBattleTimestampText} | {ActiveSelectedBattle.SnapshotSummary}"
                : $"{selectedBattleTimestampText} | Replay unavailable: {replayUnavailableReason}";
            _footerSecondaryText.text = string.IsNullOrWhiteSpace(_statusMessage)
                ? battleSummary
                : $"{_statusMessage} | {battleSummary}";
        }

        if (_previewStatusText != null && ActiveSelectedBattle == null)
        {
            _previewStatusText.text = "Select a battle to preview its recorded cards.";
            _previewStatusText.gameObject.SetActive(true);
        }

        RefreshActionButton(
            _runsTabButton,
            _runsTabButtonBackground,
            _runsTabButtonLabel,
            true,
            _sectionMode == HistorySectionMode.Runs
                ? new Color(0.78f, 0.60f, 0.24f, 0.98f)
                : new Color(0.23f, 0.27f, 0.32f, 0.98f),
            new Color(0.92f, 0.72f, 0.30f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            _sectionMode == HistorySectionMode.Runs
                ? new Color(0.10f, 0.07f, 0.03f, 1f)
                : Color.white
        );
        RefreshActionButton(
            _ghostTabButton,
            _ghostTabButtonBackground,
            _ghostTabButtonLabel,
            true,
            _sectionMode == HistorySectionMode.Ghost
                ? new Color(0.78f, 0.60f, 0.24f, 0.98f)
                : new Color(0.23f, 0.27f, 0.32f, 0.98f),
            new Color(0.92f, 0.72f, 0.30f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            _sectionMode == HistorySectionMode.Ghost
                ? new Color(0.10f, 0.07f, 0.03f, 1f)
                : Color.white
        );
        RefreshActionButton(
            _syncGhostButton,
            _syncGhostButtonBackground,
            _syncGhostButtonLabel,
            _dataService.CanSyncGhostBattles,
            new Color(0.23f, 0.27f, 0.32f, 0.98f),
            new Color(0.35f, 0.39f, 0.44f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            Color.white
        );
        RefreshGhostFilterButton(
            _ghostFilterAllButton,
            _ghostFilterAllButtonBackground,
            _ghostFilterAllButtonLabel,
            GhostBattleFilter.All
        );
        RefreshGhostFilterButton(
            _ghostFilterIWonButton,
            _ghostFilterIWonButtonBackground,
            _ghostFilterIWonButtonLabel,
            GhostBattleFilter.IWon
        );
        RefreshGhostFilterButton(
            _ghostFilterILostButton,
            _ghostFilterILostButtonBackground,
            _ghostFilterILostButtonLabel,
            GhostBattleFilter.ILost
        );

        if (_dynamicPreviewButtonLabel != null)
        {
            _dynamicPreviewButtonLabel.text = GetDynamicPreviewButtonLabel(
                HistoryPanelPreviewSettings.DynamicPreviewEnabled
            );
        }

        if (_replayButtonLabel != null)
            _replayButtonLabel.text = _replayService.GetReplayActionLabel(ActiveSelectedBattle);

        RefreshActionButton(
            _dynamicPreviewButton,
            _dynamicPreviewButtonBackground,
            _dynamicPreviewButtonLabel,
            true,
            HistoryPanelPreviewSettings.DynamicPreviewEnabled
                ? new Color(0.30f, 0.47f, 0.29f, 0.98f)
                : new Color(0.23f, 0.27f, 0.32f, 0.98f),
            HistoryPanelPreviewSettings.DynamicPreviewEnabled
                ? new Color(0.39f, 0.59f, 0.37f, 1f)
                : new Color(0.35f, 0.39f, 0.44f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            Color.white
        );

        RefreshActionButton(
            _replayButton,
            _replayButtonBackground,
            _replayButtonLabel,
            canReplaySelectedBattle,
            new Color(0.78f, 0.60f, 0.24f, 0.98f),
            new Color(0.92f, 0.72f, 0.30f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            new Color(0.10f, 0.07f, 0.03f, 1f)
        );

        var canDeleteSelectedRun = CanDeleteSelectedRun(out _);
        if (_deleteRunButtonLabel != null)
        {
            _deleteRunButtonLabel.text = GetDeleteRunButtonLabel(
                _sectionMode == HistorySectionMode.Runs
                    && SelectedRun != null
                    && IsDeleteRunConfirmationActive(SelectedRun.RunId)
            );
        }

        RefreshActionButton(
            _deleteRunButton,
            _deleteRunButtonBackground,
            _deleteRunButtonLabel,
            canDeleteSelectedRun,
            _sectionMode == HistorySectionMode.Runs
            && SelectedRun != null
            && IsDeleteRunConfirmationActive(SelectedRun.RunId)
                ? new Color(0.75f, 0.23f, 0.20f, 0.98f)
                : new Color(0.46f, 0.19f, 0.18f, 0.98f),
            new Color(0.86f, 0.29f, 0.25f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            new Color(1f, 0.95f, 0.94f, 1f)
        );

        RebuildRunList();
        RebuildBattleList();
    }

    private void BuildHeader()
    {
        var header = CreateRect("Header", _panelRoot!);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = new Vector2(0f, -20f);
        header.sizeDelta = new Vector2(-48f, 112f);

        var headerLayout = CreateVerticalGroup(
            "HeaderLayout",
            header,
            6f,
            CreatePadding(0f, 0f, 0f, 0f),
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        StretchToParent(headerLayout, 0f, 0f, 0f, 0f);

        var title = CreateText("Title", headerLayout, 28, FontStyle.Bold, TextAnchor.UpperLeft);
        title.text = "Game History";
        title.color = new Color(0.97f, 0.85f, 0.57f, 1f);
        ConfigureLayoutElement(title.gameObject, preferredHeight: 32f, minHeight: 32f);

        var subtitle = CreateText(
            "Subtitle",
            headerLayout,
            14,
            FontStyle.Normal,
            TextAnchor.UpperLeft
        );
        subtitle.text =
            "Review your game history and replay any battle you want. Support BazaarPlusPlus at bazaarplusplus.com. -- Xinyu YANG";
        subtitle.color = new Color(0.82f, 0.86f, 0.91f, 0.94f);
        subtitle.textWrappingMode = TextWrappingModes.Normal;
        subtitle.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(subtitle.gameObject, preferredHeight: 28f, minHeight: 28f);

        var chipsRow = CreateHorizontalGroup(
            "ChipsRow",
            headerLayout,
            8f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(chipsRow.gameObject, preferredHeight: 34f, minHeight: 34f);

        _countChipText = CreateChip(chipsRow, 86f);
        _battleChipText = CreateChip(chipsRow, 96f);
        _databaseChipText = CreateChip(chipsRow, 110f);
        CreateFlexibleSpacer("Spacer", chipsRow);
        (_runsTabButton, _runsTabButtonBackground, _runsTabButtonLabel) = CreateStyledButton(
            "RunsTabButton",
            chipsRow,
            "Runs",
            92f,
            32f
        );
        _runsTabButton.onClick.AddListener(() => SetSectionMode(HistorySectionMode.Runs));
        (_ghostTabButton, _ghostTabButtonBackground, _ghostTabButtonLabel) = CreateStyledButton(
            "GhostTabButton",
            chipsRow,
            "Ghost",
            92f,
            32f
        );
        _ghostTabButton.onClick.AddListener(() => SetSectionMode(HistorySectionMode.Ghost));
        (_syncGhostButton, _syncGhostButtonBackground, _syncGhostButtonLabel) = CreateStyledButton(
            "SyncGhostButton",
            chipsRow,
            "Sync Ghost",
            114f,
            32f
        );
        _syncGhostButton.onClick.AddListener(TrySyncGhostBattles);
        (_dynamicPreviewButton, _dynamicPreviewButtonBackground, _dynamicPreviewButtonLabel) =
            CreateStyledButton(
                "DynamicPreviewButton",
                chipsRow,
                GetDynamicPreviewButtonLabel(false),
                120f,
                32f
            );
        _dynamicPreviewButton.onClick.AddListener(ToggleDynamicPreviewFromUi);
        CreateActionButton("CloseButton", chipsRow, "Close", 86f, () => SetHistoryVisible(false));

        _statusText = CreateText(
            "Status",
            headerLayout,
            12,
            FontStyle.Normal,
            TextAnchor.UpperLeft
        );
        _statusText.color = new Color(0.93f, 0.79f, 0.51f, 0.98f);
        _statusText.textWrappingMode = TextWrappingModes.NoWrap;
        _statusText.overflowMode = TextOverflowModes.Ellipsis;
        _statusText.gameObject.SetActive(false);
        ConfigureLayoutElement(_statusText.gameObject, preferredHeight: 18f, minHeight: 18f);
    }

    private void BuildContent()
    {
        var content = CreateRect("Content", _panelRoot!);
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(1f, 1f);
        content.offsetMin = new Vector2(24f, 96f);
        content.offsetMax = new Vector2(-24f, -164f);

        var outerLayout = CreateVerticalGroup(
            "OuterLayout",
            content,
            10f,
            null,
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        StretchToParent(outerLayout, 0f, 0f, 0f, 0f);

        var columnsRow = CreateHorizontalGroup(
            "ColumnsRow",
            outerLayout,
            18f,
            null,
            TextAnchor.UpperLeft,
            true,
            true,
            false,
            true
        );
        ConfigureLayoutElement(columnsRow.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

        _runSectionPanel = CreateSectionPanel(columnsRow, "RunsPanel");
        ConfigureLayoutElement(
            _runSectionPanel.gameObject,
            preferredWidth: ListColumnWidth,
            minWidth: ListColumnWidth,
            preferredHeight: 0f,
            flexibleHeight: 1f
        );
        var leftLayout = CreateVerticalGroup(
            "RunsLayout",
            _runSectionPanel,
            10f,
            CreatePadding(14f, 14f, 14f, 14f),
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        StretchToParent(leftLayout, 0f, 0f, 0f, 0f);
        BuildSectionHeader(
            leftLayout,
            "Runs",
            "Choose one run to see its recorded battles.",
            out _,
            out _runSectionTitle
        );
        _runListContent = CreateScrollSection(leftLayout, "RunScroll");

        var right = CreateSectionPanel(columnsRow, "BattlesPanel");
        ConfigureLayoutElement(
            right.gameObject,
            flexibleWidth: 1f,
            preferredHeight: 0f,
            flexibleHeight: 1f
        );
        BuildGhostBattleSection(right);
        BuildRunsBattleSection(right);

        BuildPreviewSection(outerLayout);
    }

    private void BuildGhostBattleSection(RectTransform parent)
    {
        var layout = CreateVerticalGroup(
            "GhostBattleLayout",
            parent,
            2f,
            CreatePadding(14f, 14f, 4f, 4f),
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        StretchToParent(layout, 0f, 0f, 0f, 0f);
        _ghostModeRoot = layout;

        var filterRow = CreateHorizontalGroup(
            "GhostFilterRow",
            layout,
            8f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(
            filterRow.gameObject,
            preferredHeight: GhostFilterButtonHeight,
            minHeight: GhostFilterButtonHeight
        );
        (_ghostFilterAllButton, _ghostFilterAllButtonBackground, _ghostFilterAllButtonLabel) =
            CreateStyledButton(
                "GhostFilterAllButton",
                filterRow,
                "All",
                70f,
                GhostFilterButtonHeight
            );
        ConfigureCompactGhostFilterLabel(_ghostFilterAllButtonLabel);
        _ghostFilterAllButton.onClick.AddListener(() =>
            SetGhostBattleFilter(GhostBattleFilter.All)
        );
        (_ghostFilterIWonButton, _ghostFilterIWonButtonBackground, _ghostFilterIWonButtonLabel) =
            CreateStyledButton(
                "GhostFilterIWonButton",
                filterRow,
                "I Won",
                78f,
                GhostFilterButtonHeight
            );
        ConfigureCompactGhostFilterLabel(_ghostFilterIWonButtonLabel);
        _ghostFilterIWonButton.onClick.AddListener(() =>
            SetGhostBattleFilter(GhostBattleFilter.IWon)
        );
        (_ghostFilterILostButton, _ghostFilterILostButtonBackground, _ghostFilterILostButtonLabel) =
            CreateStyledButton(
                "GhostFilterILostButton",
                filterRow,
                "I Lost",
                78f,
                GhostFilterButtonHeight
            );
        ConfigureCompactGhostFilterLabel(_ghostFilterILostButtonLabel);
        _ghostFilterILostButton.onClick.AddListener(() =>
            SetGhostBattleFilter(GhostBattleFilter.ILost)
        );
        CreateFlexibleSpacer("GhostFilterSpacer", filterRow);

        _ghostBattleListContent = CreateScrollSection(layout, "GhostBattleScroll");
    }

    private void BuildRunsBattleSection(RectTransform parent)
    {
        var layout = CreateVerticalGroup(
            "RunsBattleLayout",
            parent,
            10f,
            CreatePadding(14f, 14f, 14f, 14f),
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        StretchToParent(layout, 0f, 0f, 0f, 0f);
        _runsModeRoot = layout;

        BuildSectionHeader(layout, "Battles", string.Empty, out _runsBattleSectionSubtitle, out _);
        _runsBattleListContent = CreateScrollSection(layout, "RunsBattleScroll");
    }

    private void BuildFooter()
    {
        var footer = CreateRect("Footer", _panelRoot!);
        footer.anchorMin = new Vector2(0f, 0f);
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.anchoredPosition = new Vector2(0f, 20f);
        footer.sizeDelta = new Vector2(-48f, 68f);
        AddImage(footer.gameObject, new Color(0.10f, 0.12f, 0.16f, 0.98f));

        var divider = CreateRect("Divider", footer);
        divider.anchorMin = new Vector2(0f, 1f);
        divider.anchorMax = new Vector2(1f, 1f);
        divider.pivot = new Vector2(0.5f, 1f);
        divider.sizeDelta = new Vector2(0f, 1f);
        divider.gameObject.AddComponent<Image>().color = new Color(0.76f, 0.65f, 0.36f, 0.28f);

        var footerLayout = CreateHorizontalGroup(
            "FooterLayout",
            footer,
            12f,
            CreatePadding(16f, 16f, 8f, 8f),
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        StretchToParent(footerLayout, 0f, 0f, 0f, 0f);

        var textArea = CreateVerticalGroup(
            "TextArea",
            footerLayout,
            2f,
            null,
            TextAnchor.UpperLeft,
            true,
            false,
            true,
            false
        );
        ConfigureLayoutElement(textArea.gameObject, flexibleWidth: 1f);

        _footerPrimaryText = CreateText(
            "Primary",
            textArea,
            15,
            FontStyle.Bold,
            TextAnchor.UpperLeft
        );
        _footerPrimaryText.color = Color.white;
        _footerPrimaryText.textWrappingMode = TextWrappingModes.NoWrap;
        _footerPrimaryText.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(_footerPrimaryText.gameObject, preferredHeight: 22f, minHeight: 22f);

        _footerSecondaryText = CreateText(
            "Secondary",
            textArea,
            12,
            FontStyle.Normal,
            TextAnchor.UpperLeft
        );
        _footerSecondaryText.color = new Color(0.72f, 0.77f, 0.84f, 0.94f);
        _footerSecondaryText.textWrappingMode = TextWrappingModes.NoWrap;
        _footerSecondaryText.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(
            _footerSecondaryText.gameObject,
            preferredHeight: 22f,
            minHeight: 22f
        );

        var actions = CreateHorizontalGroup(
            "Actions",
            footerLayout,
            10f,
            null,
            TextAnchor.MiddleRight,
            false,
            true,
            false,
            false
        );
        ConfigureLayoutElement(actions.gameObject, preferredWidth: 390f, minWidth: 390f);

        (_deleteRunButton, _deleteRunButtonBackground, _deleteRunButtonLabel) = CreateStyledButton(
            "DeleteRunButton",
            actions,
            GetDeleteRunButtonLabel(false),
            130f,
            36f
        );
        _deleteRunButton.onClick.AddListener(TryDeleteSelectedRun);

        (_replayButton, _replayButtonBackground, _replayButtonLabel) = CreateStyledButton(
            "ReplayButton",
            actions,
            "Replay",
            120f,
            36f
        );
        _replayButton.onClick.AddListener(TryReplaySelectedBattle);
        CreateActionButton(
            "FooterCloseButton",
            actions,
            "Close",
            120f,
            () => SetHistoryVisible(false)
        );
    }

    private void RebuildRunList()
    {
        if (_runListContent == null)
            return;

        ClearContainer(_runListContent, _runItemViews);

        if (_runs.Count == 0)
        {
            CreatePlaceholder(_runListContent, "No runs found yet.");
            return;
        }

        for (var i = 0; i < _runs.Count; i++)
            _runItemViews.Add(CreateRunItem(_runListContent, i, _runs[i], i == _selectedRunIndex));
    }

    private void RebuildBattleList()
    {
        if (_sectionMode == HistorySectionMode.Ghost)
            RebuildGhostBattleList();
        else
            RebuildRunsBattleList();
    }

    private void RebuildGhostBattleList()
    {
        if (_ghostBattleListContent == null)
            return;

        ClearContainer(_ghostBattleListContent, _battleItemViews);

        if (FilteredGhostBattles.Count == 0)
        {
            CreatePlaceholder(_ghostBattleListContent, "No ghost battles synced yet.");
            return;
        }

        for (var i = 0; i < FilteredGhostBattles.Count; i++)
            _battleItemViews.Add(
                CreateBattleItem(
                    _ghostBattleListContent,
                    i,
                    FilteredGhostBattles[i],
                    i == _selectedGhostBattleIndex
                )
            );
    }

    private void RebuildRunsBattleList()
    {
        if (_runsBattleListContent == null)
            return;

        ClearContainer(_runsBattleListContent, _battleItemViews);

        if (SelectedRun == null)
        {
            CreatePlaceholder(_runsBattleListContent, "Select a run first.");
            return;
        }

        if (_battles.Count == 0)
        {
            CreatePlaceholder(_runsBattleListContent, "No recorded battles for this run.");
            return;
        }

        for (var i = 0; i < _battles.Count; i++)
            _battleItemViews.Add(
                CreateBattleItem(_runsBattleListContent, i, _battles[i], i == _selectedBattleIndex)
            );
    }

    private ListItemView CreateRunItem(
        Transform parent,
        int index,
        HistoryRunRecord run,
        bool selected
    )
    {
        var (button, background) = CreateCardButtonShell($"RunItem_{index}", parent, 130f);
        button.onClick.AddListener(() => SelectRun(index));

        var (_, body) = BuildCardShell(
            button.transform,
            selected
                ? new Color(0.46f, 0.70f, 0.92f, 0.94f)
                : new Color(0.24f, 0.31f, 0.39f, 0.96f),
            4f,
            CreatePadding(12f, 12f, 10f, 10f)
        );

        // --- pill row ---
        var topRow = CreateHorizontalGroup(
            "TopRow",
            body,
            8f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(topRow.gameObject, preferredHeight: 22f, minHeight: 22f);

        var pillRow = CreateHorizontalGroup(
            "PillRow",
            topRow,
            5f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(pillRow.gameObject, flexibleWidth: 1f);

        var runHeroStyle = GetHeroBadgeStyle(run.Hero);
        AddPill(
            pillRow,
            "Hero",
            runHeroStyle.ShortCode,
            runHeroStyle.Background,
            runHeroStyle.Text,
            60f
        );
        BuildRunRankBadge(pillRow, run);
        var achievement = HistoryPanelFormatter.FormatRunAchievement(run);
        if (!string.IsNullOrWhiteSpace(achievement))
        {
            AddPill(
                pillRow,
                "Achievement",
                achievement,
                GetRunAchievementBackground(achievement),
                GetRunAchievementText(achievement),
                72f
            );
        }

        var time = CreateText("Time", topRow, 11, FontStyle.Normal, TextAnchor.UpperRight);
        time.text = HistoryPanelFormatter.FormatTimestamp(run.LastSeenAtUtc);
        time.color = new Color(0.72f, 0.78f, 0.85f, 0.9f);
        time.textWrappingMode = TextWrappingModes.NoWrap;
        time.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(
            time.gameObject,
            preferredWidth: 96f,
            minWidth: 84f,
            preferredHeight: 16f
        );

        var metaParts = new List<string>
        {
            HistoryPanelFormatter.FormatDayOnly(run.FinalDay),
            $"{run.BattleCount} battles",
        };
        if (run.Victories.HasValue)
            metaParts.Add($"{run.Victories.Value} wins");
        var duration = HistoryPanelFormatter.FormatRunDuration(run);
        if (!string.IsNullOrWhiteSpace(duration))
            metaParts.Add(duration);

        AddDetailLine(
            body,
            string.Join("  |  ", metaParts),
            13,
            FontStyle.Normal,
            new Color(0.82f, 0.86f, 0.92f, 0.96f)
        );
        BuildRunStatStrip(body, run);

        ApplyItemState(
            background,
            selected,
            new Color(0.11f, 0.14f, 0.18f, 0.98f),
            new Color(0.17f, 0.24f, 0.32f, 0.99f)
        );
        return new ListItemView { Index = index, Background = background };
    }

    private ListItemView CreateBattleItem(
        Transform parent,
        int index,
        HistoryBattleRecord battle,
        bool selected
    )
    {
        var isGhostBattle = battle.Source == HistoryBattleSource.Ghost;
        var palette = GetBattlePalette(battle);
        var (button, background) = CreateCardButtonShell($"BattleItem_{index}", parent, 88f);
        button.onClick.AddListener(() => SelectBattle(index));

        var (_, body) = BuildCardShell(
            button.transform,
            palette.Accent,
            3f,
            CreatePadding(12f, 12f, 8f, 8f)
        );

        // --- top row: pills + trailing timestamp ---
        var topRow = CreateHorizontalGroup(
            "TopRow",
            body,
            8f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(topRow.gameObject, preferredHeight: 22f, minHeight: 22f);

        var pillRow = CreateHorizontalGroup(
            "PillRow",
            topRow,
            6f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(pillRow.gameObject, flexibleWidth: 1f);

        AddPill(
            pillRow,
            "Day",
            HistoryPanelFormatter.FormatDayOnly(battle.Day),
            new Color(0.18f, 0.21f, 0.27f, 0.94f),
            new Color(0.92f, 0.95f, 1f, 1f),
            72f
        );
        var playerHero = HistoryPanelFormatter.FormatOpponentHero(battle.PlayerHero);
        if (isGhostBattle && !string.IsNullOrWhiteSpace(playerHero))
        {
            var playerHeroStyle = GetHeroBadgeStyle(playerHero);
            AddPill(
                pillRow,
                "PlayerHero",
                $"YOU {playerHeroStyle.ShortCode}",
                playerHeroStyle.Background,
                playerHeroStyle.Text,
                88f
            );
        }
        var opponentHero = HistoryPanelFormatter.FormatOpponentHero(battle.OpponentHero);
        if (!string.IsNullOrWhiteSpace(opponentHero))
        {
            var opponentHeroStyle = GetHeroBadgeStyle(opponentHero);
            AddPill(
                pillRow,
                "OpponentHero",
                opponentHeroStyle.ShortCode,
                opponentHeroStyle.Background,
                opponentHeroStyle.Text,
                60f
            );
        }

        var time = CreateText("Time", topRow, 11, FontStyle.Normal, TextAnchor.UpperRight);
        time.text = HistoryPanelFormatter.FormatTimestamp(battle.RecordedAtUtc);
        time.color = new Color(0.72f, 0.78f, 0.85f, 0.9f);
        time.textWrappingMode = TextWrappingModes.NoWrap;
        time.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(
            time.gameObject,
            preferredWidth: 120f,
            minWidth: 80f,
            preferredHeight: 16f
        );

        // --- detail lines ---
        BuildBattleNameRow(body, battle);
        var participantSummary = isGhostBattle ? BuildBattleParticipantSummary(battle) : null;
        if (!string.IsNullOrWhiteSpace(participantSummary))
        {
            AddDetailLine(
                body,
                participantSummary,
                12,
                FontStyle.Normal,
                new Color(0.82f, 0.87f, 0.93f, 0.95f)
            );
        }
        AddDetailLine(
            body,
            $"ID: {ShortenBattleId(battle.BattleId)}",
            11,
            FontStyle.Normal,
            new Color(0.70f, 0.75f, 0.83f, 0.90f)
        );
        if (!string.IsNullOrWhiteSpace(battle.SnapshotSummary))
        {
            AddDetailLine(
                body,
                battle.SnapshotSummary,
                12,
                FontStyle.Normal,
                new Color(0.74f, 0.80f, 0.87f, 0.95f)
            );
        }

        ApplyItemState(background, selected, palette.Normal, palette.Selected);
        return new ListItemView { Index = index, Background = background };
    }

    /// <summary>
    /// Builds the shared accent-bar + body shell inside a card button.
    /// Returns (accent, body) so each item can populate body freely.
    /// </summary>
    private (RectTransform accent, RectTransform body) BuildCardShell(
        Transform buttonTransform,
        Color accentColor,
        float bodySpacing,
        RectOffset bodyPadding
    )
    {
        var rootLayout = CreateHorizontalGroup(
            "RootLayout",
            buttonTransform,
            0f,
            null,
            TextAnchor.UpperLeft,
            true,
            true,
            false,
            false
        );
        StretchToParent(rootLayout, 0f, 0f, 0f, 0f);

        var accent = CreateRect("Accent", rootLayout);
        ConfigureLayoutElement(
            accent.gameObject,
            preferredWidth: 6f,
            minWidth: 6f,
            flexibleHeight: 1f
        );
        AddImage(accent.gameObject, accentColor);

        var body = CreateVerticalGroup(
            "Body",
            rootLayout,
            bodySpacing,
            bodyPadding,
            TextAnchor.UpperLeft,
            true,
            true,
            true,
            false
        );
        ConfigureLayoutElement(body.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

        return (accent, body);
    }

    private void AddPill(
        RectTransform parent,
        string name,
        string label,
        Color bg,
        Color textColor,
        float minWidth
    )
    {
        var displayLabel = FormatPillLabel(label);
        var width = Mathf.Max(minWidth, MeasurePillWidth(displayLabel));
        var pill = CreatePill(parent, name, displayLabel, bg, textColor);
        ConfigureLayoutElement(
            pill.gameObject,
            preferredWidth: width,
            minWidth: width,
            preferredHeight: 22f,
            minHeight: 22f
        );
    }

    private void AddDetailLine(
        RectTransform parent,
        string text,
        int fontSize,
        FontStyle style,
        Color color
    )
    {
        var line = CreateText("Detail", parent, fontSize, style, TextAnchor.UpperLeft);
        line.text = text;
        line.color = color;
        line.textWrappingMode = TextWrappingModes.NoWrap;
        line.overflowMode = TextOverflowModes.Ellipsis;
        var height = fontSize <= 12 ? 16f : 20f;
        ConfigureLayoutElement(line.gameObject, preferredHeight: height, minHeight: height);
    }

    private static void ApplyItemState(
        Image background,
        bool selected,
        Color normal,
        Color selectedColor
    )
    {
        background.color = selected ? selectedColor : normal;
    }

    private static void ClearContainer<TView>(RectTransform container, List<TView> views)
    {
        foreach (Transform child in container)
            Destroy(child.gameObject);

        views.Clear();
    }

    private void CreatePlaceholder(Transform parent, string message)
    {
        var placeholder = CreateRect("Placeholder", parent);
        ConfigureLayoutElement(placeholder.gameObject, preferredHeight: 96f, minHeight: 96f);
        AddImage(placeholder.gameObject, new Color(0.12f, 0.14f, 0.18f, 0.98f));

        var text = CreateText("Text", placeholder, 14, FontStyle.Normal, TextAnchor.MiddleCenter);
        text.text = message;
        text.color = new Color(0.72f, 0.77f, 0.84f, 0.95f);
        StretchToParent(text.rectTransform, 14f, 14f, 0f, 0f);
    }

    private RectTransform CreateSectionPanel(Transform parent, string name)
    {
        var panel = CreateRect(name, parent);
        AddImage(panel.gameObject, new Color(0.11f, 0.13f, 0.18f, 0.98f));

        var border = CreateRect("Border", panel);
        StretchToParent(border, 0f, 0f, 0f, 0f);
        AddImage(border.gameObject, new Color(0.77f, 0.83f, 0.91f, 0.08f));
        return panel;
    }

    private void BuildSectionHeader(
        Transform parent,
        string titleText,
        string subtitleText,
        out TextMeshProUGUI subtitle,
        out TextMeshProUGUI title
    )
    {
        title = CreateText("SectionTitle", parent, 20, FontStyle.Bold, TextAnchor.UpperLeft);
        title.text = titleText.ToUpperInvariant();
        title.color = new Color(0.76f, 0.91f, 1f, 1f);
        ConfigureLayoutElement(title.gameObject, preferredHeight: 24f, minHeight: 24f);

        subtitle = CreateText(
            "SectionSubtitle",
            parent,
            12,
            FontStyle.Normal,
            TextAnchor.UpperLeft
        );
        subtitle.text = subtitleText;
        subtitle.color = new Color(0.72f, 0.77f, 0.84f, 0.92f);
        subtitle.textWrappingMode = TextWrappingModes.Normal;
        subtitle.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(subtitle.gameObject, preferredHeight: 30f, minHeight: 18f);
    }

    private RectTransform CreateScrollSection(Transform parent, string name)
    {
        var root = CreateRect(name, parent);
        ConfigureLayoutElement(root.gameObject, flexibleHeight: 1f, flexibleWidth: 1f);

        var viewport = CreateRect("Viewport", root);
        StretchToParent(viewport, 0f, 14f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
        group.spacing = 10f;
        group.childControlWidth = true;
        group.childControlHeight = false;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 20f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.verticalScrollbar = CreateScrollbar(root);
        scroll.verticalScrollbarVisibility = ScrollRect
            .ScrollbarVisibility
            .AutoHideAndExpandViewport;
        return content;
    }

    private void BuildPreviewSection(Transform parent)
    {
        var preview = CreateRect("PreviewPanel", parent);
        ConfigureLayoutElement(
            preview.gameObject,
            preferredHeight: PreviewSectionHeight,
            minHeight: PreviewSectionHeight
        );
        AddImage(preview.gameObject, new Color(0.07f, 0.09f, 0.12f, 0.99f));

        var surfaceFrame = CreateRect("PreviewSurfaceFrame", preview);
        StretchToParent(surfaceFrame, 10f, 10f, 10f, 10f);
        AddImage(surfaceFrame.gameObject, new Color(0.03f, 0.04f, 0.06f, 0.98f));

        var rawImageRect = CreateRect("PreviewRawImage", surfaceFrame);
        StretchToParent(rawImageRect, 1f, 1f, 1f, 1f);
        _previewSurface = rawImageRect.gameObject.AddComponent<RawImage>();
        _previewSurface.color = new Color(1f, 1f, 1f, 0.10f);
        _previewSurface.raycastTarget = false;

        _previewStatusText = CreateText(
            "PreviewStatus",
            surfaceFrame,
            13,
            FontStyle.Normal,
            TextAnchor.MiddleCenter
        );
        _previewStatusText.text = "Select a battle to preview its recorded cards.";
        _previewStatusText.color = new Color(0.82f, 0.87f, 0.93f, 0.96f);
        _previewStatusText.textWrappingMode = TextWrappingModes.Normal;
        _previewStatusText.overflowMode = TextOverflowModes.Ellipsis;
        StretchToParent(_previewStatusText.rectTransform, 28f, 28f, 18f, 18f);

        _previewDebugText = CreateText(
            "PreviewDebug",
            surfaceFrame,
            11,
            FontStyle.Bold,
            TextAnchor.UpperRight
        );
        _previewDebugText.color = new Color(0.97f, 0.85f, 0.57f, 0.96f);
        _previewDebugText.gameObject.SetActive(false);
        _previewDebugText.textWrappingMode = TextWrappingModes.NoWrap;
        _previewDebugText.overflowMode = TextOverflowModes.Overflow;
        _previewDebugText.rectTransform.anchorMin = new Vector2(1f, 1f);
        _previewDebugText.rectTransform.anchorMax = new Vector2(1f, 1f);
        _previewDebugText.rectTransform.pivot = new Vector2(1f, 1f);
        _previewDebugText.rectTransform.anchoredPosition = new Vector2(-14f, -12f);
        _previewDebugText.rectTransform.sizeDelta = new Vector2(560f, 36f);
    }

    private Scrollbar CreateScrollbar(Transform parent)
    {
        var root = CreateRect("Scrollbar", parent);
        root.anchorMin = new Vector2(1f, 0f);
        root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(1f, 0.5f);
        root.sizeDelta = new Vector2(10f, 0f);

        var track = AddImage(root.gameObject, new Color(0.16f, 0.18f, 0.22f, 0.88f));
        track.raycastTarget = true;
        var area = CreateRect("Area", root);
        StretchToParent(area, 0f, 0f, 0f, 0f);
        var handle = CreateRect("Handle", area);
        handle.anchorMin = new Vector2(0f, 1f);
        handle.anchorMax = new Vector2(1f, 1f);
        handle.pivot = new Vector2(0.5f, 1f);
        handle.sizeDelta = new Vector2(0f, 56f);
        var handleImage = AddImage(handle.gameObject, new Color(0.74f, 0.62f, 0.31f, 0.96f));
        handleImage.raycastTarget = true;
        var scrollbar = root.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.colors = BuildColorBlock(
            new Color(0.74f, 0.62f, 0.31f, 0.96f),
            new Color(0.86f, 0.73f, 0.38f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.45f)
        );
        return scrollbar;
    }

    private TextMeshProUGUI CreateChip(Transform parent, float width)
    {
        var chip = CreateRect("Chip", parent);
        ConfigureLayoutElement(
            chip.gameObject,
            preferredWidth: width,
            minWidth: width,
            preferredHeight: 32f,
            minHeight: 32f
        );
        AddImage(chip.gameObject, new Color(0.14f, 0.18f, 0.23f, 0.96f));
        var text = CreateText("Label", chip, 12, FontStyle.Bold, TextAnchor.MiddleCenter);
        text.color = new Color(0.95f, 0.96f, 0.98f, 1f);
        StretchToParent(text.rectTransform, 8f, 8f, 0f, 0f);
        return text;
    }

    private RectTransform CreatePill(
        Transform parent,
        string name,
        string labelText,
        Color backgroundColor,
        Color textColor
    )
    {
        var pill = CreateRect(name, parent);
        AddImage(pill.gameObject, backgroundColor);
        var text = CreateText("Label", pill, 12, FontStyle.Bold, TextAnchor.MiddleCenter);
        text.text = labelText;
        text.color = textColor;
        text.enableAutoSizing = true;
        text.fontSizeMin = 10f;
        text.fontSizeMax = 12f;
        text.maxVisibleCharacters = PillMaxVisibleCharacters;
        text.overflowMode = TextOverflowModes.Ellipsis;
        StretchToParent(text.rectTransform, 10f, 10f, 0f, 0f);
        return pill;
    }

    private void CreateActionButton(
        string name,
        Transform parent,
        string label,
        float width,
        UnityEngine.Events.UnityAction onClick
    )
    {
        var (button, background, text) = CreateStyledButton(name, parent, label, width, 38f);
        button.onClick.AddListener(onClick);
        RefreshActionButton(
            button,
            background,
            text,
            true,
            new Color(0.23f, 0.27f, 0.32f, 0.98f),
            new Color(0.35f, 0.39f, 0.44f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            Color.white
        );
    }

    private (Button button, Image background, TextMeshProUGUI label) CreateStyledButton(
        string name,
        Transform parent,
        string labelText,
        float width,
        float preferredHeight = 36f
    )
    {
        var rect = CreateRect(name, parent);
        if (width > 0f)
        {
            ConfigureLayoutElement(
                rect.gameObject,
                preferredWidth: width,
                minWidth: width,
                preferredHeight: preferredHeight,
                minHeight: preferredHeight
            );
        }
        else
        {
            ConfigureLayoutElement(
                rect.gameObject,
                flexibleWidth: 1f,
                preferredHeight: preferredHeight,
                minHeight: preferredHeight
            );
        }

        var background = AddImage(rect.gameObject, Color.white);
        background.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = BuildColorBlock(Color.white, Color.white, Color.white);

        var label = CreateText("Label", rect, 13, FontStyle.Bold, TextAnchor.MiddleCenter);
        label.text = labelText;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = 13f;
        label.overflowMode = TextOverflowModes.Ellipsis;
        StretchToParent(label.rectTransform, 10f, 10f, 0f, 0f);
        return (button, background, label);
    }

    private static void ConfigureCompactGhostFilterLabel(TextMeshProUGUI? label)
    {
        if (label == null)
            return;

        label.enableAutoSizing = false;
        label.fontSize = 14f;
        label.margin = Vector4.zero;
        label.extraPadding = false;
    }

    private (Button button, Image background) CreateCardButtonShell(
        string name,
        Transform parent,
        float preferredHeight
    )
    {
        var rect = CreateRect(name, parent);
        ConfigureLayoutElement(
            rect.gameObject,
            flexibleWidth: 1f,
            preferredHeight: preferredHeight,
            minHeight: preferredHeight
        );
        var background = AddImage(rect.gameObject, Color.white);
        background.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = BuildColorBlock(Color.white, Color.white, Color.white);
        return (button, background);
    }

    private static void RefreshActionButton(
        Button? button,
        Image? background,
        TextMeshProUGUI? label,
        bool interactable,
        Color normalColor,
        Color pressedColor,
        Color disabledColor,
        Color textColor
    )
    {
        if (button == null || background == null || label == null)
            return;

        button.interactable = interactable;
        button.colors = BuildColorBlock(normalColor, pressedColor, disabledColor);
        background.color = interactable ? normalColor : disabledColor;
        label.color = interactable
            ? textColor
            : new Color(textColor.r, textColor.g, textColor.b, 0.55f);
    }

    private void RefreshGhostFilterButton(
        Button? button,
        Image? background,
        TextMeshProUGUI? label,
        GhostBattleFilter filter
    )
    {
        var isGhostMode = _sectionMode == HistorySectionMode.Ghost;
        var selected = _ghostBattleFilter == filter;
        RefreshActionButton(
            button,
            background,
            label,
            isGhostMode,
            selected
                ? new Color(0.78f, 0.60f, 0.24f, 0.98f)
                : new Color(0.19f, 0.22f, 0.27f, 0.98f),
            new Color(0.92f, 0.72f, 0.30f, 1f),
            new Color(0.24f, 0.26f, 0.30f, 0.50f),
            selected ? new Color(0.10f, 0.07f, 0.03f, 1f) : Color.white
        );
    }

    private static string GetGhostFilterLabel(GhostBattleFilter filter)
    {
        return filter switch
        {
            GhostBattleFilter.IWon => "I Won",
            GhostBattleFilter.ILost => "I Lost",
            _ => "All",
        };
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static RectTransform CreateVerticalGroup(
        string name,
        Transform parent,
        float spacing,
        RectOffset? padding,
        TextAnchor alignment,
        bool controlWidth,
        bool controlHeight,
        bool forceExpandWidth,
        bool forceExpandHeight
    )
    {
        var rect = CreateRect(name, parent);
        var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding ?? new RectOffset();
        group.childAlignment = alignment;
        group.childControlWidth = controlWidth;
        group.childControlHeight = controlHeight;
        group.childForceExpandWidth = forceExpandWidth;
        group.childForceExpandHeight = forceExpandHeight;
        return rect;
    }

    private static RectTransform CreateHorizontalGroup(
        string name,
        Transform parent,
        float spacing,
        RectOffset? padding,
        TextAnchor alignment,
        bool controlWidth,
        bool controlHeight,
        bool forceExpandWidth,
        bool forceExpandHeight
    )
    {
        var rect = CreateRect(name, parent);
        var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding ?? new RectOffset();
        group.childAlignment = alignment;
        group.childControlWidth = controlWidth;
        group.childControlHeight = controlHeight;
        group.childForceExpandWidth = forceExpandWidth;
        group.childForceExpandHeight = forceExpandHeight;
        return rect;
    }

    private static void CreateFlexibleSpacer(string name, Transform parent)
    {
        var spacer = CreateRect(name, parent);
        ConfigureLayoutElement(spacer.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);
    }

    private static RectOffset CreatePadding(float left, float right, float top, float bottom)
    {
        return new RectOffset(
            Mathf.RoundToInt(left),
            Mathf.RoundToInt(right),
            Mathf.RoundToInt(top),
            Mathf.RoundToInt(bottom)
        );
    }

    private static void ConfigureLayoutElement(
        GameObject gameObject,
        float preferredWidth = -1f,
        float minWidth = -1f,
        float flexibleWidth = -1f,
        float preferredHeight = -1f,
        float minHeight = -1f,
        float flexibleHeight = -1f
    )
    {
        var element =
            gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        if (preferredWidth >= 0f)
            element.preferredWidth = preferredWidth;
        if (minWidth >= 0f)
            element.minWidth = minWidth;
        if (flexibleWidth >= 0f)
            element.flexibleWidth = flexibleWidth;
        if (preferredHeight >= 0f)
            element.preferredHeight = preferredHeight;
        if (minHeight >= 0f)
            element.minHeight = minHeight;
        if (flexibleHeight >= 0f)
            element.flexibleHeight = flexibleHeight;
    }

    private static void StretchToParent(
        RectTransform rect,
        float left,
        float right,
        float top,
        float bottom
    )
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static Image AddImage(GameObject gameObject, Color color)
    {
        var image = gameObject.AddComponent<Image>();
        image.sprite = GetRoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        int fontSize,
        FontStyle fontStyle,
        TextAnchor alignment
    )
    {
        var rect = CreateRect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = GetUiFont();
        text.fontSize = fontSize;
        text.fontStyle = MapFontStyle(fontStyle);
        text.alignment = MapAlignment(alignment);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Truncate;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }

    private static ColorBlock BuildColorBlock(Color normal, Color pressed, Color disabled)
    {
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = normal;
        colors.highlightedColor = normal;
        colors.selectedColor = normal;
        colors.pressedColor = pressed;
        colors.disabledColor = disabled;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.05f;
        return colors;
    }

    private static TMP_FontAsset GetUiFont()
    {
        _uiFont ??= TMP_Settings.defaultFontAsset;
        if (_uiFont == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (candidate != null && candidate.font != null)
                {
                    _uiFont = candidate.font;
                    break;
                }
            }
        }

        _uiFont ??= TMP_FontAsset.CreateFontAsset(
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
        );
        return _uiFont;
    }

    private static Sprite GetRoundedSprite()
    {
        if (_roundedSprite != null)
            return _roundedSprite;

        const int size = 32;
        const int radius = 12;
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                texture.SetPixel(
                    x,
                    y,
                    new Color(1f, 1f, 1f, IsInsideRoundedRect(x, y, size, radius) ? 1f : 0f)
                );
            }
        }

        texture.Apply();
        _roundedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius)
        );
        return _roundedSprite;
    }

    private static bool IsInsideRoundedRect(int x, int y, int size, int radius)
    {
        var clampedX = Mathf.Clamp(x, radius, size - radius - 1);
        var clampedY = Mathf.Clamp(y, radius, size - radius - 1);
        var dx = x - clampedX;
        var dy = y - clampedY;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private static FontStyles MapFontStyle(FontStyle fontStyle) =>
        fontStyle switch
        {
            FontStyle.Bold => FontStyles.Bold,
            FontStyle.Italic => FontStyles.Italic,
            FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
            _ => FontStyles.Normal,
        };

    private static TextAlignmentOptions MapAlignment(TextAnchor alignment) =>
        alignment switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.MidlineLeft,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Midline,
            TextAnchor.MiddleRight => TextAlignmentOptions.MidlineRight,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.TopLeft,
        };

    private static float MeasurePillWidth(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? 64f : Mathf.Max(64f, (text.Length * 7f) + 24f);
    }

    private static string FormatPillLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return label;

        var trimmed = label.Trim();
        return trimmed.ToUpperInvariant() switch
        {
            "PERFECT" => "PERFCT",
            "UNFORTUNE" => "UNFRT",
            _ => trimmed,
        };
    }

    private static string GetDynamicPreviewButtonLabel(bool enabled)
    {
        return enabled ? "Live" : "Still";
    }

    private static string GetDeleteRunButtonLabel(bool confirming)
    {
        return confirming ? "Sure?" : "Delete";
    }

    private static string BuildBattleExtraLine(HistoryBattleRecord battle)
    {
        return string.IsNullOrWhiteSpace(battle.EncounterId)
            ? $"Battle ID {ShortenBattleId(battle.BattleId)}"
            : $"Encounter {battle.EncounterId}  |  Battle ID {ShortenBattleId(battle.BattleId)}";
    }

    private static string ShortenBattleId(string battleId)
    {
        return string.IsNullOrWhiteSpace(battleId) ? "-"
            : battleId.Length <= 12 ? battleId
            : battleId[..12];
    }

    private static BattlePalette GetBattlePalette(HistoryBattleRecord battle)
    {
        var result = HistoryPanelFormatter.FormatBattleResult(battle);
        if (string.Equals(result, "Win", StringComparison.OrdinalIgnoreCase))
        {
            return new BattlePalette(
                new Color(0.10f, 0.15f, 0.16f, 0.98f),
                new Color(0.13f, 0.23f, 0.22f, 0.99f),
                new Color(0.23f, 0.54f, 0.47f, 0.95f),
                new Color(0.13f, 0.28f, 0.23f, 0.98f),
                new Color(0.80f, 0.98f, 0.91f, 1f)
            );
        }

        if (string.Equals(result, "Loss", StringComparison.OrdinalIgnoreCase))
        {
            return new BattlePalette(
                new Color(0.15f, 0.13f, 0.15f, 0.98f),
                new Color(0.24f, 0.18f, 0.16f, 0.99f),
                new Color(0.63f, 0.36f, 0.24f, 0.95f),
                new Color(0.33f, 0.20f, 0.15f, 0.98f),
                new Color(0.99f, 0.90f, 0.85f, 1f)
            );
        }

        return new BattlePalette(
            new Color(0.13f, 0.15f, 0.18f, 0.98f),
            new Color(0.18f, 0.24f, 0.31f, 0.99f),
            new Color(0.34f, 0.47f, 0.64f, 0.95f),
            new Color(0.18f, 0.23f, 0.31f, 0.98f),
            new Color(0.89f, 0.94f, 1f, 1f)
        );
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

    private void BuildBattleNameRow(RectTransform parent, HistoryBattleRecord battle)
    {
        var row = CreateHorizontalGroup(
            "BattleNameRow",
            parent,
            6f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(row.gameObject, preferredHeight: 20f, minHeight: 20f);

        BuildRankBadge(row, battle.OpponentRank, battle.OpponentRating, "OpponentRank");

        var nameText = CreateText("OpponentName", row, 15, FontStyle.Bold, TextAnchor.MiddleLeft);
        nameText.text = battle.OpponentName ?? "Unknown Opponent";
        nameText.color = Color.white;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        ConfigureLayoutElement(
            nameText.gameObject,
            flexibleWidth: 1f,
            preferredHeight: 20f,
            minHeight: 20f
        );
    }

    private static string? BuildBattleParticipantSummary(HistoryBattleRecord battle)
    {
        var playerHero = HistoryPanelFormatter.FormatOpponentHero(battle.PlayerHero) ?? "?";
        var opponentHero = HistoryPanelFormatter.FormatOpponentHero(battle.OpponentHero) ?? "?";
        var playerLevel = battle.PlayerLevel?.ToString() ?? "?";
        var opponentLevel = battle.OpponentLevel?.ToString() ?? "?";
        if (playerHero == "?" && opponentHero == "?" && playerLevel == "?" && opponentLevel == "?")
            return null;

        return $"YOU {playerHero} Lv{playerLevel}  |  OPP {opponentHero} Lv{opponentLevel}";
    }

    private void BuildRankBadge(
        RectTransform parent,
        string? rawRank,
        int? rating,
        string badgeName
    )
    {
        var rank = FormatRank(rawRank);
        if (string.IsNullOrWhiteSpace(rank))
            return;

        if (string.Equals(rank, "Legendary", StringComparison.OrdinalIgnoreCase))
        {
            AddPill(
                parent,
                badgeName,
                rating.HasValue ? rating.Value.ToString() : "LEG",
                ColorFromRgb(241, 54, 41),
                Color.white,
                68f
            );
            return;
        }

        var palette = GetRankBadgePalette(rank);
        AddPill(parent, badgeName, rank.ToUpperInvariant(), palette.Background, palette.Text, 68f);
    }

    private void BuildRunRankBadge(RectTransform parent, HistoryRunRecord run)
    {
        if (string.Equals(run.GameMode?.Trim(), "Ranked", StringComparison.OrdinalIgnoreCase))
        {
            BuildRankBadge(parent, run.PlayerRank, run.PlayerRating, "PlayerRank");
            return;
        }

        AddPill(
            parent,
            "PlayerRank",
            "Unrank",
            new Color(0.22f, 0.24f, 0.29f, 0.98f),
            new Color(0.90f, 0.94f, 1f, 1f),
            84f
        );
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

    private void BuildRunStatStrip(Transform parent, HistoryRunRecord run)
    {
        var row = CreateHorizontalGroup(
            "RunStatsRow",
            parent,
            6f,
            null,
            TextAnchor.MiddleLeft,
            true,
            true,
            false,
            false
        );
        ConfigureLayoutElement(row.gameObject, preferredHeight: 40f, minHeight: 40f);

        CreateRunStatChip(row, "HP", run.MaxHealth, new Color(0.63f, 0.98f, 0.35f, 1f));
        CreateRunStatChip(row, "PRE", run.Prestige, new Color(1f, 0.65f, 0.13f, 1f));
        CreateRunStatChip(row, "LVL", run.Level, new Color(0.36f, 0.79f, 1f, 1f));
        CreateRunStatChip(row, "INC", run.Income, new Color(1f, 0.86f, 0.10f, 1f));
        CreateRunStatChip(row, "GLD", run.Gold, new Color(1f, 0.86f, 0.10f, 1f));
    }

    private static void CreateRunStatChip(
        Transform parent,
        string labelText,
        int? value,
        Color valueColor
    )
    {
        var chip = CreateRect(labelText, parent);
        ConfigureLayoutElement(
            chip.gameObject,
            flexibleWidth: 1f,
            preferredHeight: 40f,
            minHeight: 40f
        );
        AddImage(chip.gameObject, BuildRunStatChipBackground(valueColor));

        var accent = CreateRect("Accent", chip);
        accent.anchorMin = new Vector2(0f, 0f);
        accent.anchorMax = new Vector2(0f, 1f);
        accent.pivot = new Vector2(0f, 0.5f);
        accent.sizeDelta = new Vector2(3f, 0f);
        AddImage(accent.gameObject, new Color(valueColor.r, valueColor.g, valueColor.b, 0.95f));

        var label = CreateText("Label", chip, 9, FontStyle.Bold, TextAnchor.UpperLeft);
        label.text = labelText;
        label.color = new Color(0.86f, 0.90f, 0.96f, 0.86f);
        label.rectTransform.anchorMin = new Vector2(0f, 1f);
        label.rectTransform.anchorMax = new Vector2(1f, 1f);
        label.rectTransform.pivot = new Vector2(0f, 1f);
        label.rectTransform.offsetMin = new Vector2(9f, -16f);
        label.rectTransform.offsetMax = new Vector2(-6f, -5f);

        var valueText = CreateText("Value", chip, 14, FontStyle.Bold, TextAnchor.UpperLeft);
        valueText.text = value?.ToString() ?? "--";
        valueText.color = valueColor;
        valueText.rectTransform.anchorMin = new Vector2(0f, 0f);
        valueText.rectTransform.anchorMax = new Vector2(1f, 0f);
        valueText.rectTransform.pivot = new Vector2(0f, 0f);
        valueText.rectTransform.offsetMin = new Vector2(9f, 5f);
        valueText.rectTransform.offsetMax = new Vector2(-6f, 21f);
    }

    private static Color BuildRunStatChipBackground(Color accent)
    {
        return new Color(
            Mathf.Lerp(0.16f, accent.r, 0.12f),
            Mathf.Lerp(0.15f, accent.g, 0.12f),
            Mathf.Lerp(0.14f, accent.b, 0.12f),
            0.98f
        );
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
