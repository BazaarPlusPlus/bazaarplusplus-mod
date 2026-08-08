#nullable enable
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
        panel.style.backgroundColor = Colors.CollectionPanelBackground;
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
        rail.style.minWidth = Sizes.CollectionOperationRailMinWidth;
        rail.style.maxWidth = Sizes.OperationRailMaxWidth;
        rail.style.minHeight = 0f;
        rail.style.overflow = Overflow.Hidden;
        rail.style.marginLeft = UiSpacing.ColumnGap;
        parent.Add(rail);

        // The top controls read as one control deck: navigation and context first, followed by
        // the search tools. This keeps the catalog's controls from looking like separate rows.
        var controlDeck = new VisualElement();
        controlDeck.style.flexDirection = FlexDirection.Column;
        controlDeck.style.flexShrink = 0f;
        controlDeck.style.backgroundColor = Colors.CollectionFilterCardBackground;
        UiStyle.Radius(controlDeck.style, Radii.Md);
        UiStyle.Padding(controlDeck.style, UiSpacing.Lg);
        rail.Add(controlDeck);

        // Title + tabs + Close (Close lives here in the operation area, not a top bar).
        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        titleRow.style.minHeight = Sizes.CollectionTabToggleHeight;
        controlDeck.Add(titleRow);

        _title = CreateLabel(Sizes.FontTitle, FontStyle.Normal, Colors.White);
        _title.style.flexGrow = 1f;
        _title.style.flexShrink = 1f;
        _title.style.minWidth = 0f;
        _title.style.whiteSpace = WhiteSpace.NoWrap;
        _title.style.overflow = Overflow.Hidden;
        titleRow.Add(_title);

        var resetButton = CreateResetButton(_commands.ResetFilters);
        resetButton.style.marginLeft = UiSpacing.Sm;
        titleRow.Add(resetButton);

        _tabModeControl = CreateFacetChoiceControl(
            CollectionPanelText.ItemsTab(),
            CollectionPanelText.SkillsTab(),
            () => _commands.SetActiveTab(CollectionTabKind.Items),
            () => _commands.SetActiveTab(CollectionTabKind.Skills),
            Sizes.CollectionTabToggleWidth,
            Sizes.CollectionTabToggleHeight,
            Sizes.FontBody,
            slanted: false
        );
        _tabModeControl.style.marginLeft = UiSpacing.Sm;
        _tabModeControl.style.marginRight = UiSpacing.Md;
        titleRow.Add(_tabModeControl);

        _closeButton = CreateCloseButton(_commands.Close);
        titleRow.Add(_closeButton);

        _subtitle = BPPSupporterAttributionRow.Create();
        _subtitle.style.flexWrap = Wrap.NoWrap;
        _subtitle.style.overflow = Overflow.Hidden;
        _subtitle.style.marginTop = UiSpacing.Xl;
        _subtitle.style.backgroundColor = Colors.CollectionPanelBackground;
        UiStyle.FixedHeight(_subtitle.style, Sizes.CollectionTabToggleHeight);
        UiStyle.HorizontalPadding(_subtitle.style, UiSpacing.Md);
        UiStyle.Radius(_subtitle.style, Radii.CollectionChip);
        controlDeck.Add(_subtitle);

        if (_stagingItemIdCopyEnabled)
        {
            _stagingIdCopyLabel = CreateLabel(
                Sizes.FontCorner,
                FontStyle.Normal,
                Colors.HistoryStatusText
            );
            _stagingIdCopyLabel.text = StagingIdCopyHint;
            _stagingIdCopyLabel.style.marginTop = UiSpacing.Xs;
            _stagingIdCopyLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _stagingIdCopyLabel.style.overflow = Overflow.Hidden;
            controlDeck.Add(_stagingIdCopyLabel);
        }

        var primaryControlsRow = CreateOperationRow(UiSpacing.Md);
        controlDeck.Add(primaryControlsRow);

        // The search field is deliberately persistent. Sorting is attached to its right edge so
        // the entire operation row reads as one compact search-and-sort control.
        _searchInputContainer = CreateSearchField();
        primaryControlsRow.Add(_searchInputContainer);
        _searchInputContainer.Add(CreateSortButtonGroup());

        _standardOperationControls = new VisualElement();
        _standardOperationControls.style.flexDirection = FlexDirection.Row;
        _standardOperationControls.style.alignItems = Align.Center;
        _standardOperationControls.style.flexWrap = Wrap.NoWrap;
        _standardOperationControls.style.flexShrink = 0f;
        _standardOperationControls.style.marginLeft = UiSpacing.Sm;
        primaryControlsRow.Add(_standardOperationControls);

        // Compact day-number icon toggle.
        _dayToggleButton = CreateDayToggleButton();
        _standardOperationControls.Add(_dayToggleButton);

        var controlsViewport = new VisualElement();
        controlsViewport.style.flexGrow = 1f;
        controlsViewport.style.flexShrink = 1f;
        controlsViewport.style.minHeight = 0f;
        controlsViewport.style.position = Position.Relative;
        controlsViewport.style.overflow = Overflow.Hidden;
        rail.Add(controlsViewport);

        var controlsScroll = new ScrollView(ScrollViewMode.Vertical);
        controlsScroll.style.flexGrow = 1f;
        controlsScroll.style.flexShrink = 1f;
        controlsScroll.style.minHeight = 0f;
        controlsScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        controlsScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
        controlsScroll.mouseWheelScrollSize = CollectionGridConstants.MouseWheelScrollPoints;
        controlsScroll.contentContainer.style.flexDirection = FlexDirection.Column;
        controlsScroll.contentContainer.style.minHeight = 0f;
        _controlsScrollView = controlsScroll;
        _controlsDragScroller = new ScrollViewDragScroller(controlsScroll);
        controlsViewport.Add(controlsScroll);

        _controlsScrollShadow = CreateControlsScrollShadow();
        controlsViewport.Add(_controlsScrollShadow);

        // All filter cards, including the foundational hero/size/quality card, scroll below the
        // fixed control deck above.
        _heroFilterSection = CreateFilterSection(
            controlsScroll,
            CollectionPanelText.HeroHeader(),
            UiSpacing.Xl,
            out _heroChipRow,
            out _heroFilterLabel,
            out var heroHeaderRow
        );
        _allHeroesButton = CreateTextMatchModeButton(
            "bpp-collection-all-heroes",
            _commands.ToggleAllHeroes
        );
        _allHeroesButton.style.marginLeft = UiSpacing.Sm;
        heroHeaderRow.Add(_allHeroesButton);
        _heroChipRow.style.flexWrap = Wrap.NoWrap;
        _heroChipRow.style.justifyContent = Justify.FlexStart;
        _heroChipRow.RegisterCallback<GeometryChangedEvent>(OnHeroChipRowGeometryChanged);

        // Keep hero, size, and quality in one visual card. The child section contributes only its
        // chip row, without introducing a nested card or a second heading.
        _tierFilterSection = CreateFilterSection(
            _heroFilterSection,
            string.Empty,
            UiSpacing.Lg,
            out var tierSizeChipRow,
            out _tierFilterLabel,
            out _,
            card: false
        );
        tierSizeChipRow.style.flexWrap = Wrap.NoWrap;
        tierSizeChipRow.style.justifyContent = Justify.FlexStart;
        _sizeChipRow = CreateCombinedFilterChipSegment();
        _tierChipRow = CreateCombinedFilterChipSegment();
        tierSizeChipRow.Add(_sizeChipRow);
        tierSizeChipRow.Add(_tierChipRow);
        _tierChipRow.style.marginLeft = UiSpacing.Sm;
        _tierChipRow.style.flexWrap = Wrap.NoWrap;
        _tierChipRow.style.justifyContent = Justify.FlexStart;

        // Keyword filter (EHiddenTag gameplay keywords). This is the common secondary filter for
        // Items and Skills, so keep it directly below Quality.
        _keywordFilterSection = CreateFilterSection(
            controlsScroll,
            CollectionPanelText.KeywordHeader(),
            UiSpacing.Lg,
            out _keywordChipRow,
            out _keywordFilterLabel,
            out var keywordHeaderRow
        );
        _keywordMatchModeButton = CreateTextMatchModeControl(mode =>
            _commands.SetKeywordMatchMode(mode)
        );
        keywordHeaderRow.Add(_keywordMatchModeButton);
        _keywordChipRow.style.flexWrap = Wrap.Wrap;
        _keywordChipRow.style.justifyContent = Justify.FlexStart;

        // Player-facing type chips get their own titled card; the chip flow itself is unchanged.
        _tagFilterSection = CreateFilterChipSection(
            controlsScroll,
            CollectionPanelText.TagHeader(),
            UiSpacing.Lg,
            out _tagChipRow,
            out var tagHeaderRow
        );
        _tagMatchModeButton = CreateTextMatchModeControl(mode => _commands.SetTagMatchMode(mode));
        tagHeaderRow.Add(_tagMatchModeButton);
        _tagChipRow.style.flexWrap = Wrap.Wrap;
        _tagChipRow.style.justifyContent = Justify.FlexStart;

        // Source filter (merchant portraits on Items, trainer portraits on Skills).
        _sourceFilterSection = CreateFilterSection(
            controlsScroll,
            CollectionPanelText.SourceHeader(ECardType.Item),
            UiSpacing.Lg,
            out _sourceChipRow,
            out _sourceFilterLabel
        );
        _sourceChipRow.style.flexDirection = FlexDirection.Column;
        _sourceChipRow.style.flexWrap = Wrap.NoWrap;
        _sourceChipRow.style.justifyContent = Justify.FlexStart;
        _sourceChipRow.RegisterCallback<GeometryChangedEvent>(OnSourceChipRowGeometryChanged);

        _disclaimerLabel = CreateLabel(
            Sizes.FontCorner,
            FontStyle.Normal,
            Colors.HistoryFooterSecondaryText
        );
        _disclaimerLabel.text = CollectionPanelText.SourceDisclaimer();
        _disclaimerLabel.tooltip = _disclaimerLabel.text;
        _disclaimerLabel.style.marginTop = UiSpacing.Md;
        _disclaimerLabel.style.flexShrink = 0f;
        _disclaimerLabel.style.width = Length.Percent(100f);
        _disclaimerLabel.style.whiteSpace = WhiteSpace.Normal;
        _disclaimerLabel.style.maxHeight = Sizes.DetailTextMaxHeight;
        _disclaimerLabel.style.overflow = Overflow.Hidden;
        controlsScroll.Add(_disclaimerLabel);

        _statusLabel = CreateLabel(Sizes.FontSmall, FontStyle.Normal, Colors.HistoryStatusText);
        _statusLabel.style.marginTop = UiSpacing.Md;
        _statusLabel.style.flexShrink = 0f;
        _statusLabel.style.minHeight = Sizes.StatusHeight;
        _statusLabel.style.maxHeight = Sizes.CollectionStatusMaxHeight;
        _statusLabel.style.width = Length.Percent(100f);
        _statusLabel.style.whiteSpace = WhiteSpace.Normal;
        _statusLabel.style.overflow = Overflow.Hidden;
        _statusLabel.style.display = DisplayStyle.None;
        rail.Add(_statusLabel);
    }

    private static VisualElement CreateOperationRow(float marginTop)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.flexWrap = Wrap.NoWrap;
        row.style.flexShrink = 0f;
        row.style.marginTop = marginTop;
        return row;
    }

    private static VisualElement CreateControlsScrollShadow()
    {
        var shadow = new VisualElement { pickingMode = PickingMode.Ignore };
        shadow.style.position = Position.Absolute;
        shadow.style.left = 0f;
        shadow.style.right = 0f;
        shadow.style.top = 0f;
        UiStyle.FixedHeight(shadow.style, Sizes.CollectionScrollShadowHeight);
        shadow.style.display = DisplayStyle.None;
        shadow.generateVisualContent += context => DrawControlsScrollShadow(context, shadow);
        return shadow;
    }

    private static void DrawControlsScrollShadow(
        MeshGenerationContext context,
        VisualElement shadow
    )
    {
        var rect = shadow.contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        var top = Colors.WithAlpha(Colors.CollectionPanelBackground, 0.92f);
        var bottom = Colors.Clear;
        var mesh = context.Allocate(4, 6);
        mesh.SetNextVertex(
            new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = top }
        );
        mesh.SetNextVertex(
            new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = top }
        );
        mesh.SetNextVertex(
            new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = bottom }
        );
        mesh.SetNextVertex(
            new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = bottom }
        );
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
    }

    private static VisualElement CreateOperationSpacer()
    {
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;
        spacer.style.flexShrink = 1f;
        spacer.style.minWidth = UiSpacing.Md;
        return spacer;
    }

    private static Button CreateCloseButton(Action onClick)
    {
        var button = CreateButton(
            string.Empty,
            onClick,
            Sizes.ButtonStandardHeight,
            Sizes.ButtonStandardHeight
        );
        button.tooltip = CollectionPanelText.Close();
        StyleButton(button, Colors.HistoryButtonBackground, Colors.CollectionChipText);
        UiStyle.Radius(button.style, Sizes.ButtonStandardHeight / 2f);
        UiStyle.Border(button.style, Borders.Thin, Colors.CollectionChipBorder);

        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        UiStyle.FixedSize(
            icon.style,
            Sizes.CollectionSearchIconSize,
            Sizes.CollectionSearchIconSize
        );
        icon.style.color = Colors.CollectionChipText;
        icon.generateVisualContent += context => DrawCloseIcon(context, icon);
        button.Add(icon);
        return button;
    }

    private static Button CreateResetButton(Action onClick)
    {
        var button = CreateButton(
            string.Empty,
            onClick,
            Sizes.CollectionTabToggleHeight,
            Sizes.CollectionTabToggleHeight
        );
        button.tooltip = CollectionPanelText.ResetFilters();
        StyleButton(button, Colors.CollectionChipBackground, Colors.CollectionChipText);
        UiStyle.Radius(button.style, Radii.CollectionChip);
        UiStyle.Border(button.style, Borders.Thin, Colors.CollectionChipBorder);

        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        UiStyle.FixedSize(icon.style, 16f, 16f);
        icon.style.marginTop = -0.5f;
        icon.style.color = Colors.WithAlpha(Colors.CollectionChipText, 0.84f);
        icon.generateVisualContent += context => DrawResetIcon(context, icon);
        button.Add(icon);
        return button;
    }

    private static void DrawCloseIcon(MeshGenerationContext context, VisualElement icon)
    {
        var rect = icon.contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        var inset = Mathf.Min(rect.width, rect.height) * 0.22f;
        var painter = context.painter2D;
        painter.lineWidth = Mathf.Max(1.5f, rect.width * 0.14f);
        painter.lineCap = LineCap.Round;
        painter.strokeColor = icon.resolvedStyle.color;

        painter.BeginPath();
        painter.MoveTo(new Vector2(rect.xMin + inset, rect.yMin + inset));
        painter.LineTo(new Vector2(rect.xMax - inset, rect.yMax - inset));
        painter.Stroke();

        painter.BeginPath();
        painter.MoveTo(new Vector2(rect.xMax - inset, rect.yMin + inset));
        painter.LineTo(new Vector2(rect.xMin + inset, rect.yMax - inset));
        painter.Stroke();
    }

    private static void DrawResetIcon(MeshGenerationContext context, VisualElement icon)
    {
        var rect = icon.contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        var center = rect.center;
        var radius = Mathf.Min(rect.width, rect.height) * 0.32f;
        var painter = context.painter2D;
        painter.lineWidth = Mathf.Max(1.35f, rect.width * 0.115f);
        painter.lineCap = LineCap.Round;
        painter.lineJoin = LineJoin.Round;
        painter.strokeColor = icon.resolvedStyle.color;
        painter.BeginPath();
        painter.MoveTo(new Vector2(center.x - radius * 0.82f, center.y - radius * 0.18f));
        painter.LineTo(new Vector2(center.x - radius * 0.44f, center.y - radius * 0.76f));
        painter.LineTo(new Vector2(center.x + radius * 0.26f, center.y - radius * 0.88f));
        painter.LineTo(new Vector2(center.x + radius * 0.82f, center.y - radius * 0.40f));
        painter.LineTo(new Vector2(center.x + radius * 0.82f, center.y + radius * 0.36f));
        painter.LineTo(new Vector2(center.x + radius * 0.26f, center.y + radius * 0.84f));
        painter.LineTo(new Vector2(center.x - radius * 0.46f, center.y + radius * 0.68f));
        painter.Stroke();

        painter.BeginPath();
        painter.MoveTo(new Vector2(center.x - radius * 0.16f, center.y - radius * 0.92f));
        painter.LineTo(new Vector2(center.x - radius * 0.86f, center.y - radius * 0.18f));
        painter.LineTo(new Vector2(center.x - radius * 0.02f, center.y - radius * 0.10f));
        painter.Stroke();
    }

    private VisualElement CreateSearchField()
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.flexGrow = 1f;
        container.style.flexShrink = 1f;
        container.style.minWidth = 0f;
        container.style.height = Sizes.CollectionSearchRowHeight;
        container.pickingMode = PickingMode.Position;

        var frame = new VisualElement();
        frame.style.flexDirection = FlexDirection.Row;
        frame.style.alignItems = Align.Center;
        frame.style.flexGrow = 1f;
        frame.style.flexShrink = 1f;
        frame.style.minWidth = 0f;
        frame.style.height = Sizes.CollectionSearchRowHeight;
        frame.style.backgroundColor = Colors.CollectionChipBackground;
        UiStyle.Border(frame.style, Borders.Thin, Colors.CollectionChipBorder);
        UiStyle.Radius(frame.style, Radii.CollectionChip);
        UiStyle.HorizontalPadding(frame.style, UiSpacing.Md);
        _searchFrame = frame;
        container.Add(frame);

        var field = new TextField { label = string.Empty };
        _searchField = field;
        field.textSelection.selectAllOnFocus = false;
        field.textSelection.selectAllOnMouseUp = false;
        field.tooltip = CollectionPanelText.SearchTooltip();
        field.style.flexGrow = 1f;
        field.style.flexShrink = 1f;
        field.style.minWidth = 0f;
        field.style.height = Length.Percent(100f);
        field.style.backgroundColor = Color.clear;
        field.style.color = Colors.CollectionChipText;
        _typography!.Apply(field);
        field.style.fontSize = Sizes.CollectionTagFontSize;
        field.style.borderLeftWidth = 0f;
        field.style.borderRightWidth = 0f;
        field.style.borderTopWidth = 0f;
        field.style.borderBottomWidth = 0f;
        UiStyle.Padding(field.style, UiSpacing.None, UiSpacing.None);
        frame.Add(field);

        _searchPlaceholderLabel = CreateLabel(
            Sizes.CollectionTagFontSize,
            FontStyle.Normal,
            Colors.WithAlpha(Colors.CollectionChipText, 0.58f)
        );
        _searchPlaceholderLabel.pickingMode = PickingMode.Ignore;
        _searchPlaceholderLabel.style.position = Position.Absolute;
        _searchPlaceholderLabel.style.left = UiSpacing.Md;
        _searchPlaceholderLabel.style.right = UiSpacing.Md;
        _searchPlaceholderLabel.style.top = 0f;
        _searchPlaceholderLabel.style.bottom = 0f;
        _searchPlaceholderLabel.style.whiteSpace = WhiteSpace.NoWrap;
        _searchPlaceholderLabel.style.overflow = Overflow.Hidden;
        _searchPlaceholderLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        frame.Add(_searchPlaceholderLabel);

        field.RegisterValueChangedCallback(evt =>
        {
            RefreshSearchPlaceholder(evt.newValue);
            _commands.SetSearchQuery(evt.newValue);
        });
        var hovered = false;
        var focused = false;
        void RefreshFrame()
        {
            var background = Colors.CollectionChipBackground;
            var border = Colors.CollectionChipBorder;
            if (focused)
            {
                ApplySearchFocusPulse();
                return;
            }
            else if (hovered)
            {
                background = Colors.CollectionChipHoverBackground;
                border = Colors.CollectionChipHoverBorder;
            }

            frame.style.backgroundColor = background;
            UiStyle.BorderColor(frame.style, border);
        }

        field.RegisterCallback<MouseEnterEvent>(_ =>
        {
            hovered = true;
            RefreshFrame();
        });
        field.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            hovered = false;
            RefreshFrame();
        });
        field.RegisterCallback<FocusInEvent>(_ =>
        {
            focused = true;
            _searchFocused = true;
            _searchFocusPulseElapsed = 0f;
            RefreshFrame();
        });
        field.RegisterCallback<FocusOutEvent>(_ =>
        {
            focused = false;
            _searchFocused = false;
            _searchFocusPulseElapsed = 0f;
            RefreshFrame();
        });
        RefreshFrame();

        field.RegisterCallback<GeometryChangedEvent>(_ => StyleSearchField(field));
        return container;
    }

    private void TickSearchFocusPulse(float deltaSeconds)
    {
        if (!_searchFocused || _searchFrame == null)
            return;

        _searchFocusPulseElapsed += deltaSeconds;
        ApplySearchFocusPulse();
    }

    private void ApplySearchFocusPulse()
    {
        if (_searchFrame == null)
            return;

        const float pulseSeconds = 2.4f;
        var phase = _searchFocusPulseElapsed * (Mathf.PI * 2f / pulseSeconds) - Mathf.PI * 0.5f;
        var pulse = Mathf.SmoothStep(0f, 1f, (Mathf.Sin(phase) + 1f) * 0.5f);
        _searchFrame.style.backgroundColor = Color.Lerp(
            Colors.CollectionChipSelectedBackground,
            Colors.CollectionChipSelectedHoverBackground,
            pulse
        );
        UiStyle.BorderColor(
            _searchFrame.style,
            Color.Lerp(
                Colors.CollectionChipSelectedBorder,
                Colors.CollectionChipSelectedHoverBorder,
                pulse
            )
        );
    }

    private VisualElement CreateSortButtonGroup()
    {
        var group = new VisualElement();
        group.style.flexDirection = FlexDirection.Row;
        group.style.alignItems = Align.Stretch;
        group.style.flexShrink = 0f;
        group.style.height = Sizes.CollectionSearchRowHeight;
        group.style.overflow = Overflow.Hidden;
        UiStyle.FixedWidth(group.style, Sizes.CollectionSortButtonWidth * 2f);
        UiStyle.Radius(group.style, Radii.CollectionChip);
        UiStyle.Border(group.style, Borders.Thin, Colors.CollectionChipBorder);
        group.style.marginLeft = UiSpacing.Sm;

        _sortQualityButton = CreateInlineSortButton(
            CollectionPanelText.SortQuality(),
            () => _commands.SetSortPriority(CollectionSortPriority.Quality)
        );
        _sortSizeButton = CreateInlineSortButton(
            CollectionPanelText.SortSize(),
            () => _commands.SetSortPriority(CollectionSortPriority.Size)
        );
        group.Add(_sortQualityButton);
        group.Add(_sortSizeButton);
        group.Add(CreateFacetChoiceDivider(Sizes.CollectionSortButtonWidth));
        return group;
    }

    private void StyleSearchField(TextField field)
    {
        var label = field.Q<Label>();
        if (label != null)
        {
            label.style.display = DisplayStyle.None;
        }

        var input = field.Q(TextField.textInputUssName);
        if (input != null)
        {
            input.style.flexGrow = 1f;
            input.style.height = Length.Percent(100f);
            input.style.alignSelf = Align.Stretch;
            input.style.backgroundColor = Color.clear;
            input.style.color = Colors.CollectionChipText;
            _typography!.Apply(input);
            input.style.fontSize = Sizes.CollectionTagFontSize;
            input.style.unityTextAlign = TextAnchor.MiddleLeft;
            input.style.borderLeftWidth = 0f;
            input.style.borderRightWidth = 0f;
            input.style.borderTopWidth = 0f;
            input.style.borderBottomWidth = 0f;
            input.style.marginLeft = UiSpacing.None;
            input.style.marginRight = UiSpacing.None;
            input.style.marginTop = UiSpacing.None;
            input.style.marginBottom = UiSpacing.None;
            UiStyle.Padding(input.style, UiSpacing.None, UiSpacing.None);
        }

        var text = input?.Q<TextElement>();
        if (text != null)
        {
            text.style.flexGrow = 1f;
            text.style.height = Length.Percent(100f);
            text.style.alignSelf = Align.Stretch;
            text.style.color = Colors.CollectionChipText;
            _typography!.Apply(text);
            text.style.fontSize = Sizes.CollectionTagFontSize;
            text.style.unityTextAlign = TextAnchor.MiddleLeft;
        }
    }

    private static Button CreateInlineSortButton(string text, Action onClick)
    {
        var button = CreateButton(
            text,
            onClick,
            Sizes.CollectionSortButtonWidth,
            Sizes.CollectionSearchRowHeight
        );
        button.style.flexShrink = 0f;
        StyleCollectionGroupChip(
            button,
            Colors.CollectionChipBackground,
            Colors.CollectionChipText
        );
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
        out Label label,
        bool card = true
    ) => CreateFilterSection(parent, title, marginTop, out chipRow, out label, out _, card);

    private static VisualElement CreateFilterSection(
        VisualElement parent,
        string title,
        float marginTop,
        out VisualElement chipRow,
        out Label label,
        out VisualElement headerRow,
        bool card = true
    )
    {
        var section = new VisualElement();
        section.style.flexDirection = FlexDirection.Column;
        section.style.flexShrink = 0f;
        section.style.marginTop = marginTop;
        if (card)
        {
            section.style.backgroundColor = Colors.CollectionFilterCardBackground;
            UiStyle.Radius(section.style, Radii.Md);
            UiStyle.Padding(section.style, UiSpacing.Md);
        }
        parent.Add(section);

        headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.alignSelf = Align.Stretch;
        headerRow.style.marginBottom = UiSpacing.Sm;
        if (string.IsNullOrEmpty(title))
            headerRow.style.display = DisplayStyle.None;
        section.Add(headerRow);

        label = CreateLabel(
            Sizes.CollectionFilterTitleFontSize,
            FontStyle.Bold,
            Colors.CollectionFilterTitleText
        );
        label.text = title;
        label.style.flexGrow = 1f;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.marginLeft = UiSpacing.Xs;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        headerRow.Add(label);

        chipRow = new VisualElement();
        chipRow.style.flexDirection = FlexDirection.Row;
        chipRow.style.flexWrap = Wrap.Wrap;
        chipRow.style.alignItems = Align.Center;
        chipRow.style.alignSelf = Align.Stretch;
        section.Add(chipRow);

        return section;
    }

    private VisualElement CreateTextMatchModeControl(Action<CollectionFacetMatchMode> onSelect)
    {
        var control = new VisualElement();
        control.style.flexDirection = FlexDirection.Row;
        control.style.flexShrink = 0f;
        control.style.alignItems = Align.Center;
        control.style.marginLeft = UiSpacing.Sm;
        control.Add(
            CreateTextMatchModeButton(
                FacetMatchAnyName,
                () => onSelect(CollectionFacetMatchMode.Any)
            )
        );
        control.Add(
            CreateTextMatchModeButton(
                FacetMatchAllName,
                () => onSelect(CollectionFacetMatchMode.All)
            )
        );
        return control;
    }

    private Button CreateTextMatchModeButton(string name, Action onClick)
    {
        var button = new Button(onClick) { name = name };
        _typography!.Apply(button);
        button.style.height = Sizes.FacetModeToggleHeight;
        button.style.flexShrink = 0f;
        button.style.fontSize = Sizes.FacetModeFontSize;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        UiStyle.Padding(button.style, UiSpacing.Xs, UiSpacing.None);
        button.style.backgroundColor = Color.clear;
        UiStyle.BorderWidth(button.style, Borders.None);
        UiStyle.Radius(button.style, 0f);
        return button;
    }

    private static VisualElement CreateFacetChoiceControl(
        string firstText,
        string secondText,
        Action onFirst,
        Action onSecond,
        float segmentWidth,
        float height,
        int fontSize,
        bool slanted
    )
    {
        var control = new VisualElement();
        control.style.flexDirection = FlexDirection.Row;
        control.style.flexShrink = 0f;
        control.style.height = height;
        UiStyle.FixedWidth(control.style, segmentWidth * 2f);
        control.style.marginLeft = UiSpacing.Sm;
        control.style.overflow = Overflow.Hidden;
        UiStyle.Radius(control.style, Radii.CollectionChip);
        if (!slanted)
            UiStyle.Border(control.style, Borders.Thin, Colors.CollectionChipBorder);

        var any = CreateFacetMatchModeSegment(
            FacetMatchAnyName,
            firstText,
            onFirst,
            left: true,
            segmentWidth,
            height,
            fontSize,
            slanted
        );
        var all = CreateFacetMatchModeSegment(
            FacetMatchAllName,
            secondText,
            onSecond,
            left: false,
            segmentWidth,
            height,
            fontSize,
            slanted
        );
        control.Add(any);
        control.Add(all);
        if (!slanted)
            control.Add(CreateFacetChoiceDivider(segmentWidth));
        return control;
    }

    private static Button CreateFacetMatchModeSegment(
        string name,
        string text,
        Action onClick,
        bool left,
        float width,
        float height,
        int fontSize,
        bool slanted
    )
    {
        var button = CreateButton(string.Empty, onClick, width, height);
        button.name = name;
        button.style.flexShrink = 0f;
        if (!slanted)
            UiStyle.Padding(button.style, UiSpacing.Xs, UiSpacing.Sm);

        var label = new Label(text)
        {
            name = FacetMatchLabelName,
            pickingMode = PickingMode.Ignore,
        };
        label.style.fontSize = fontSize;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.flexGrow = 1f;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        button.Add(label);

        StyleFacetMatchModeSegment(
            button,
            selected: false,
            left: left,
            fontSize: fontSize,
            slanted: slanted
        );
        return button;
    }

    private static VisualElement CreateFacetChoiceDivider(float left)
    {
        var divider = new VisualElement
        {
            name = FacetChoiceDividerName,
            pickingMode = PickingMode.Ignore,
        };
        divider.style.position = Position.Absolute;
        divider.style.left = left;
        divider.style.top = 0f;
        divider.style.bottom = 0f;
        divider.style.width = Borders.Thin;
        divider.style.backgroundColor = Colors.CollectionChipBorder;
        return divider;
    }

    private static VisualElement CreateCombinedFilterChipSegment()
    {
        var segment = new VisualElement();
        segment.style.flexDirection = FlexDirection.Row;
        segment.style.flexWrap = Wrap.NoWrap;
        segment.style.alignItems = Align.Center;
        segment.style.flexGrow = 0f;
        segment.style.flexShrink = 0f;
        segment.style.minWidth = 0f;
        segment.style.overflow = Overflow.Hidden;
        UiStyle.Radius(segment.style, Radii.CollectionChip);
        UiStyle.Border(segment.style, Borders.Thin, Colors.CollectionChipBorder);
        return segment;
    }

    private static VisualElement CreateChipGroupDivider()
    {
        var divider = new VisualElement { pickingMode = PickingMode.Ignore };
        divider.style.width = Borders.Thin;
        divider.style.height = Sizes.CollectionTagChipHeight;
        divider.style.flexShrink = 0f;
        divider.style.backgroundColor = Colors.CollectionChipBorder;
        return divider;
    }

    private static VisualElement CreateFilterChipSection(
        VisualElement parent,
        string title,
        float marginTop,
        out VisualElement chipRow,
        out VisualElement header
    )
    {
        var section = new VisualElement();
        section.style.flexDirection = FlexDirection.Column;
        section.style.flexShrink = 0f;
        section.style.marginTop = marginTop;
        section.style.backgroundColor = Colors.CollectionFilterCardBackground;
        UiStyle.Radius(section.style, Radii.Md);
        UiStyle.Padding(section.style, UiSpacing.Md);
        parent.Add(section);

        header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.marginBottom = UiSpacing.Sm;
        section.Add(header);

        var label = CreateLabel(
            Sizes.CollectionFilterTitleFontSize,
            FontStyle.Bold,
            Colors.CollectionFilterTitleText
        );
        label.text = title;
        label.style.flexGrow = 1f;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.marginLeft = UiSpacing.Xs;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        header.Add(label);

        chipRow = CreateFilterChipRow();
        section.Add(chipRow);
        return section;
    }

    private static VisualElement CreateFilterChipRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.alignItems = Align.Center;
        row.style.alignSelf = Align.Stretch;
        return row;
    }

    // Compact day-number "icon": shows the current run day (or an unavailable dash) and toggles
    // whether the day participates in filtering. The tooltip names the number-only control.
    private Button CreateDayToggleButton()
    {
        var button = CreateButton(
            string.Empty,
            _commands.ToggleRunDayFilter,
            Sizes.DayIconWidth,
            Sizes.CollectionSearchRowHeight
        );
        button.tooltip = CollectionPanelText.DayHeader();
        var content = new VisualElement { pickingMode = PickingMode.Ignore };
        content.style.flexDirection = FlexDirection.Column;
        content.style.alignItems = Align.Center;
        content.style.justifyContent = Justify.Center;
        content.style.flexGrow = 1f;
        var caption = new Label(CollectionPanelText.DayCaption())
        {
            pickingMode = PickingMode.Ignore,
        };
        caption.style.fontSize = Sizes.FontTiny;
        caption.style.marginBottom = -2f;
        caption.style.unityTextAlign = TextAnchor.MiddleCenter;
        caption.style.color = Colors.CollectionChipText;
        var value = new Label { pickingMode = PickingMode.Ignore };
        value.style.fontSize = Sizes.FontButton;
        value.style.unityTextAlign = TextAnchor.MiddleCenter;
        value.style.color = Colors.CollectionChipText;
        _dayToggleCaption = caption;
        _dayToggleValue = value;
        content.Add(caption);
        content.Add(value);
        button.Add(content);
        StyleCollectionChip(button, Colors.CollectionChipBackground, Colors.CollectionChipText);
        return button;
    }

    private void BuildGrid(VisualElement parent)
    {
        _gridViewport = new VisualElement();
        _gridViewport.style.flexGrow = 1f;
        _gridViewport.style.flexShrink = 1f;
        _gridViewport.style.minHeight = 0f;
        _gridViewport.style.minWidth = 0f;
        // Match the operation rail's card shell so the catalog reads as two aligned panels.
        _gridViewport.style.backgroundColor = Colors.CollectionFilterCardBackground;
        UiStyle.Radius(_gridViewport.style, Radii.Md);
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
        _gridDragScroller = new ScrollViewDragScroller(_gridScrollView);
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
        _emptyLabel.style.whiteSpace = WhiteSpace.Normal;
        _emptyLabel.style.overflow = Overflow.Hidden;
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
        _loadingLabel.style.whiteSpace = WhiteSpace.Normal;
        _loadingLabel.style.overflow = Overflow.Hidden;
        _loadingLabel.style.display = DisplayStyle.None;
        _gridViewport.Add(_loadingLabel);
    }
}
