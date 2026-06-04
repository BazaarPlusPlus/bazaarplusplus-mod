#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Ui;

internal sealed class LiveBuildPanelView : IDisposable
{
    private sealed class RowElements
    {
        public Label Title = null!;
        public Label Empty = null!;
        public VisualElement SlotHost = null!;
        public readonly List<VisualElement> HitTargets = new();
        public readonly List<VisualElement> Markers = new();
    }

    private readonly Transform _parent;
    private readonly Action _close;
    private readonly Action _previous;
    private readonly Action _next;
    private readonly Dictionary<BppItemBoardId, RowElements> _rows = new();
    private GameObject? _rootObject;
    private UIDocument? _document;
    private PanelSettings? _panelSettings;
    private VisualElement? _root;
    private Label? _title;
    private VisualElement? _subtitle;
    private Label? _candidateCount;
    private Label? _recommendationStatus;
    private Button? _previousButton;
    private Button? _nextButton;
    private Button? _closeButton;

    public LiveBuildPanelView(Transform parent, Action close, Action previous, Action next)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _previous = previous ?? throw new ArgumentNullException(nameof(previous));
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public event Action<BppItemBoardId, Rect>? RowBoundsChanged;
    public event Action<BppItemBoardId, Guid>? CandidateToggleRequested;

    public void EnsureCreated()
    {
        if (_rootObject != null)
            return;

        _rootObject = new GameObject("LiveBuildPanelUiToolkitRoot");
        _rootObject.transform.SetParent(_parent, false);
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.sortingOrder = 28;
        _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        _panelSettings.match = 1f;
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
        _root.style.unityFont = BppUiFont.Default;
        _root.pickingMode = PickingMode.Position;

        BppUiFont.RequestCharactersInTexture(
            LiveBuildPanelText.FontAtlasSample(),
            Sizes.FontButton,
            FontStyle.Normal
        );
        BuildTree(_root);
    }

    public void SetVisible(bool visible)
    {
        if (_root != null)
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void Refresh(LiveBuildPanelSnapshot snapshot)
    {
        if (_root == null)
            return;

        _title!.text = LiveBuildPanelText.Title();
        _closeButton!.text = LiveBuildPanelText.Close();
        BPPSupporterAttributionRow.Bind(
            _subtitle!,
            snapshot.Supporters,
            LiveBuildPanelText.Subtitle()
        );
        _candidateCount!.text = LiveBuildPanelText.CandidateCount(
            snapshot.CandidateTemplateIds.Count
        );
        _recommendationStatus!.text = snapshot.RecommendationStatus;
        _previousButton!.text = LiveBuildPanelText.Previous();
        _nextButton!.text = LiveBuildPanelText.Next();
        _previousButton.SetEnabled(snapshot.RecommendationCount > 1);
        _nextButton.SetEnabled(snapshot.RecommendationCount > 1);

        var candidates = new HashSet<Guid>(snapshot.CandidateTemplateIds);
        foreach (var row in snapshot.Rows)
            RefreshRow(row, candidates);
    }

    public void Dispose()
    {
        if (_rootObject != null)
            UnityEngine.Object.Destroy(_rootObject);
        if (_panelSettings != null)
            UnityEngine.Object.Destroy(_panelSettings);

        _rows.Clear();
        _rootObject = null;
        _document = null;
        _panelSettings = null;
        _root = null;
    }

    private void BuildTree(VisualElement root)
    {
        var panel = new VisualElement();
        panel.style.flexGrow = 1f;
        panel.style.flexDirection = FlexDirection.Row;
        panel.style.backgroundColor = Colors.HistoryPanelBackground;
        panel.style.paddingLeft = 34f;
        panel.style.paddingRight = 34f;
        panel.style.paddingTop = 28f;
        panel.style.paddingBottom = 28f;
        root.Add(panel);

        var boardArea = new VisualElement();
        boardArea.style.flexGrow = 1f;
        boardArea.style.flexShrink = 1f;
        boardArea.style.minWidth = 0f;
        boardArea.style.flexDirection = FlexDirection.Column;
        panel.Add(boardArea);

        foreach (
            var id in new[]
            {
                BppItemBoardId.FinalBuild,
                BppItemBoardId.LiveShop,
                BppItemBoardId.LiveBoard,
                BppItemBoardId.LiveStash,
            }
        )
        {
            BuildRow(boardArea, id);
        }

        BuildRail(panel);
    }

    private void BuildRow(VisualElement parent, BppItemBoardId id)
    {
        var row = new VisualElement();
        row.style.flexGrow = 1f;
        row.style.flexShrink = 1f;
        row.style.minHeight = 0f;
        row.style.marginBottom = 12f;
        row.style.backgroundColor = Colors.HistoryPreviewBackground;
        row.style.borderBottomColor = Colors.HistoryListFrameBorder;
        row.style.borderTopColor = Colors.HistoryListFrameBorder;
        row.style.borderLeftColor = Colors.HistoryListFrameBorder;
        row.style.borderRightColor = Colors.HistoryListFrameBorder;
        row.style.borderBottomWidth = 1f;
        row.style.borderTopWidth = 1f;
        row.style.borderLeftWidth = 1f;
        row.style.borderRightWidth = 1f;
        row.style.flexDirection = FlexDirection.Row;
        row.style.overflow = Overflow.Hidden;
        parent.Add(row);

        var labelColumn = new VisualElement();
        labelColumn.style.width = 160f;
        labelColumn.style.flexShrink = 0f;
        labelColumn.style.paddingLeft = 14f;
        labelColumn.style.paddingRight = 12f;
        labelColumn.style.justifyContent = Justify.Center;
        row.Add(labelColumn);

        var title = CreateLabel(18, FontStyle.Bold, Colors.HistorySectionTitleText);
        title.style.whiteSpace = WhiteSpace.Normal;
        labelColumn.Add(title);

        var empty = CreateLabel(13, FontStyle.Normal, Colors.HistoryFooterSecondaryText);
        empty.style.marginTop = 4f;
        empty.style.whiteSpace = WhiteSpace.Normal;
        labelColumn.Add(empty);

        var slotHost = new VisualElement();
        slotHost.style.flexGrow = 1f;
        slotHost.style.flexShrink = 1f;
        slotHost.style.minWidth = 0f;
        slotHost.style.position = Position.Relative;
        slotHost.style.overflow = Overflow.Hidden;
        row.Add(slotHost);

        for (var i = 0; i < 10; i++)
        {
            var slot = new VisualElement();
            slot.pickingMode = PickingMode.Ignore;
            slot.style.position = Position.Absolute;
            slot.style.left = Length.Percent(i * 10f);
            slot.style.top = 6f;
            slot.style.bottom = 6f;
            slot.style.width = Length.Percent(10f);
            slot.style.backgroundColor = Colors.CollectionSlotBackground;
            slot.style.borderLeftColor = Colors.HistoryListFrameBorder;
            slot.style.borderLeftWidth = i == 0 ? 0f : 1f;
            slotHost.Add(slot);
        }

        slotHost.RegisterCallback<GeometryChangedEvent>(_ => PublishRowBounds(id, slotHost));
        _rows[id] = new RowElements
        {
            Title = title,
            Empty = empty,
            SlotHost = slotHost,
        };
    }

    private void BuildRail(VisualElement parent)
    {
        var rail = new VisualElement();
        rail.style.width = 330f;
        rail.style.flexShrink = 0f;
        rail.style.marginLeft = 24f;
        rail.style.flexDirection = FlexDirection.Column;
        parent.Add(rail);

        var titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.alignItems = Align.Center;
        rail.Add(titleRow);

        _title = CreateLabel(28, FontStyle.Bold, Colors.HistoryTitleText);
        _title.style.flexGrow = 1f;
        titleRow.Add(_title);

        _closeButton = CreateButton(LiveBuildPanelText.Close(), _close);
        _closeButton.style.width = 86f;
        _closeButton.style.backgroundColor = Colors.CloseBackground;
        _closeButton.style.color = Colors.CloseText;
        titleRow.Add(_closeButton);

        _subtitle = BPPSupporterAttributionRow.Create();
        _subtitle.style.marginTop = 8f;
        rail.Add(_subtitle);

        _candidateCount = CreateLabel(16, FontStyle.Bold, Colors.HistoryChipText);
        _candidateCount.style.marginTop = 22f;
        _candidateCount.style.height = 34f;
        _candidateCount.style.backgroundColor = Colors.HistoryChipBackground;
        _candidateCount.style.unityTextAlign = TextAnchor.MiddleCenter;
        rail.Add(_candidateCount);

        _recommendationStatus = CreateLabel(16, FontStyle.Normal, Colors.HistoryStatusText);
        _recommendationStatus.style.marginTop = 12f;
        _recommendationStatus.style.whiteSpace = WhiteSpace.Normal;
        _recommendationStatus.style.backgroundColor = Colors.HistoryStatusBackground;
        _recommendationStatus.style.paddingLeft = 12f;
        _recommendationStatus.style.paddingRight = 12f;
        _recommendationStatus.style.paddingTop = 12f;
        _recommendationStatus.style.paddingBottom = 12f;
        rail.Add(_recommendationStatus);

        var nav = new VisualElement();
        nav.style.flexDirection = FlexDirection.Row;
        nav.style.marginTop = 12f;
        rail.Add(nav);

        _previousButton = CreateButton(LiveBuildPanelText.Previous(), _previous);
        _previousButton.style.flexGrow = 1f;
        nav.Add(_previousButton);

        _nextButton = CreateButton(LiveBuildPanelText.Next(), _next);
        _nextButton.style.flexGrow = 1f;
        _nextButton.style.marginLeft = 8f;
        nav.Add(_nextButton);
    }

    private void RefreshRow(LiveItemBoardRowVm row, HashSet<Guid> candidates)
    {
        if (!_rows.TryGetValue(row.Board.Id, out var elements))
            return;

        elements.Title.text = row.Title;
        elements.Empty.text = row.Board.Cards.Count == 0 ? row.EmptyText : string.Empty;
        ClearDynamic(elements);

        foreach (var card in row.Board.Cards)
        {
            var socket = card.DisplaySocketId ?? card.SourceSocketId;
            if (!socket.HasValue)
                continue;

            if (row.CanToggleCandidates)
                AddHitTarget(elements, row.Board.Id, card, socket.Value);

            if (candidates.Contains(card.TemplateId))
                AddCandidateMarker(elements, card, socket.Value);
        }
    }

    private void AddHitTarget(
        RowElements elements,
        BppItemBoardId rowId,
        BppItemBoardCard card,
        EContainerSocketId socket
    )
    {
        var hit = new VisualElement();
        hit.style.position = Position.Absolute;
        hit.style.left = Length.Percent((int)socket * 10f);
        hit.style.top = 0f;
        hit.style.bottom = 0f;
        hit.style.width = Length.Percent(Mathf.Clamp(card.DisplaySpan, 1, 10) * 10f);
        hit.style.backgroundColor = Color.clear;
        hit.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            CandidateToggleRequested?.Invoke(rowId, card.TemplateId);
            evt.StopPropagation();
        });
        elements.SlotHost.Add(hit);
        elements.HitTargets.Add(hit);
    }

    private void AddCandidateMarker(
        RowElements elements,
        BppItemBoardCard card,
        EContainerSocketId socket
    )
    {
        var marker = new VisualElement();
        marker.pickingMode = PickingMode.Ignore;
        marker.style.position = Position.Absolute;
        marker.style.left = Length.Percent((int)socket * 10f);
        marker.style.top = 4f;
        marker.style.bottom = 4f;
        marker.style.width = Length.Percent(Mathf.Clamp(card.DisplaySpan, 1, 10) * 10f);
        marker.style.backgroundColor = new Color(1f, 0.72f, 0.18f, 0.13f);
        marker.style.borderBottomColor = Colors.HistoryGoldAccent;
        marker.style.borderTopColor = Colors.HistoryGoldAccent;
        marker.style.borderLeftColor = Colors.HistoryGoldAccent;
        marker.style.borderRightColor = Colors.HistoryGoldAccent;
        marker.style.borderBottomWidth = 3f;
        marker.style.borderTopWidth = 3f;
        marker.style.borderLeftWidth = 3f;
        marker.style.borderRightWidth = 3f;

        var badge = CreateLabel(16, FontStyle.Bold, Color.black);
        badge.text = "✓";
        badge.pickingMode = PickingMode.Ignore;
        badge.style.position = Position.Absolute;
        badge.style.right = 6f;
        badge.style.top = 6f;
        badge.style.width = 26f;
        badge.style.height = 26f;
        badge.style.unityTextAlign = TextAnchor.MiddleCenter;
        badge.style.backgroundColor = Colors.HistoryGoldAccent;
        marker.Add(badge);

        elements.SlotHost.Add(marker);
        elements.Markers.Add(marker);
    }

    private static void ClearDynamic(RowElements elements)
    {
        foreach (var hit in elements.HitTargets)
            hit.RemoveFromHierarchy();
        foreach (var marker in elements.Markers)
            marker.RemoveFromHierarchy();
        elements.HitTargets.Clear();
        elements.Markers.Clear();
    }

    private void PublishRowBounds(BppItemBoardId id, VisualElement slotHost)
    {
        var worldBound = slotHost.worldBound;
        var ppp = slotHost.scaledPixelsPerPoint;
        var bounds = new Rect(
            Mathf.Round(worldBound.x * ppp),
            Mathf.Round(Screen.height - worldBound.yMax * ppp),
            Mathf.Max(1f, Mathf.Round(worldBound.width * ppp)),
            Mathf.Max(1f, Mathf.Round(worldBound.height * ppp))
        );
        RowBoundsChanged?.Invoke(id, bounds);
    }

    private static Label CreateLabel(int fontSize, FontStyle fontStyle, Color color)
    {
        var label = new Label();
        label.style.fontSize = fontSize;
        label.style.unityFont = BppUiFont.Default;
        label.style.unityFontStyleAndWeight = fontStyle;
        label.style.color = color;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        return label;
    }

    private static Button CreateButton(string text, Action onClick)
    {
        var button = new Button(() => onClick()) { text = text };
        button.style.height = 40f;
        button.style.unityFont = BppUiFont.Default;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        button.style.backgroundColor = Colors.HistoryButtonBackground;
        button.style.color = Colors.White;
        button.style.borderBottomColor = Colors.HistoryButtonBorder;
        button.style.borderTopColor = Colors.HistoryButtonBorder;
        button.style.borderLeftColor = Colors.HistoryButtonBorder;
        button.style.borderRightColor = Colors.HistoryButtonBorder;
        button.style.borderBottomWidth = 1f;
        button.style.borderTopWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderRightWidth = 1f;
        return button;
    }
}
