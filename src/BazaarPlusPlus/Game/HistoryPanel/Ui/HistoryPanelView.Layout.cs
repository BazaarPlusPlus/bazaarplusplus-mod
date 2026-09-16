#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private sealed class Row
    {
        internal Button Button = null!;
        internal Image Portrait = null!;
        internal Image Badge = null!;
        internal Image RankBadge = null!;
        internal TextMeshProUGUI Rating = null!;
        internal TextMeshProUGUI UnknownResult = null!;
        internal TextMeshProUGUI UnknownRank = null!;
        internal string? Hero;
        internal string? Id;
    }

    private sealed class PageList
    {
        internal ScrollRect Scroll = null!;
        internal Row[] Rows = new Row[40];
        internal float Height;
        internal bool ResetScroll;
    }

    private PageList _archiveList = null!;
    private PageList _timeline = null!;
    private TextMeshProUGUI _title = null!,
        _heading = null!,
        _metadata = null!,
        _statusLabel = null!,
        _pageLabel = null!,
        _runSummary = null!;
    private Button _runsTab = null!,
        _ghostTab = null!,
        _older = null!,
        _newer = null!,
        _latest = null!,
        _battleOlder = null!,
        _battleNewer = null!,
        _dayFilter = null!,
        _runFacts = null!;
    private readonly Dictionary<string, Button> _heroes = new();
    private readonly Dictionary<GhostBattleFilter, Button> _outcomes = new();
    private GameObject _heroFilters = null!,
        _ghostFilters = null!,
        _factsRoot = null!;
    private TextMeshProUGUI _factsText = null!;
    private bool _factsVisible;
    private HistorySectionMode? _boundSection;
    private Image _detailOutcome = null!,
        _detailRank = null!,
        _summaryRank = null!;
    private TextMeshProUGUI _detailRating = null!,
        _summaryRating = null!,
        _detailUnknownOutcome = null!,
        _detailUnknownRank = null!,
        _summaryUnknownRank = null!;

    private void CreateLayout()
    {
        _layout = CreateRect("Layout", _root!.transform, 0, 0, 1, 1);
        _layout.gameObject.AddComponent<Image>().color = new Color(.055f, .038f, .026f, 1);
        Panel(_layout, .014f, .018f, .972f, .962f);
        _title = Text(_layout, "", .04f, .03f, .37f, .06f, 34);
        Button(_layout, HistoryPanelText.Close(), .87f, .04f, .09f, .045f, _close);
        BuildSupporterHeader();
        _runsTab = Button(
            _layout,
            "",
            .04f,
            .105f,
            .095f,
            .038f,
            () => _section(HistorySectionMode.Runs)
        );
        _ghostTab = Button(
            _layout,
            "",
            .14f,
            .105f,
            .095f,
            .038f,
            () => _section(HistorySectionMode.Ghost)
        );
        _heroFilters = CreateRect("HeroFilters", _layout, .255f, .105f, .695f, .038f).gameObject;
        var heroes = HistoryPanelHeroPresentation.RunFilterHeroIds;
        for (var i = 0; i < heroes.Count; i++)
        {
            var hero = heroes[i];
            _heroes[hero] = Button(
                _heroFilters.transform,
                HistoryPanelHeroPresentation.DisplayName(hero),
                i / (float)heroes.Count,
                0,
                .97f / heroes.Count,
                1,
                () => _heroFilter(hero),
                size: 14
            );
        }
        _ghostFilters = CreateRect("GhostFilters", _layout, .255f, .105f, .695f, .038f).gameObject;
        foreach (
            var filter in new[]
            {
                GhostBattleFilter.All,
                GhostBattleFilter.IWon,
                GhostBattleFilter.ILost,
            }
        )
            _outcomes[filter] = Button(
                _ghostFilters.transform,
                "",
                (int)filter * .17f,
                0,
                .16f,
                1,
                () => _ghostFilter(filter)
            );
        _dayFilter = Button(
            _ghostFilters.transform,
            HistoryPanelText.FilterDayMin10(),
            .53f,
            0,
            .19f,
            1,
            _ghostDay
        );
        Text(
            _ghostFilters.transform,
            T("Cloud: latest 5 days, up to 200", "云端：最近5天，最多200条"),
            .73f,
            0,
            .27f,
            1,
            11,
            Muted
        );
        _archiveList = CreatePageList("Archive", .035f, .225f, .245f, .595f, 116, false);
        _timeline = CreatePageList("BattleTimeline", .285f, .225f, .095f, .595f, 74, true);
        _newer = Button(
            _layout,
            T("Newer", "较新"),
            .035f,
            .835f,
            .073f,
            .033f,
            () => NavigateArchive(-1),
            size: 14
        );
        _older = Button(
            _layout,
            T("Older", "较早"),
            .111f,
            .835f,
            .073f,
            .033f,
            () => NavigateArchive(1),
            size: 14
        );
        _latest = Button(
            _layout,
            T("Latest", "最新"),
            .187f,
            .835f,
            .086f,
            .033f,
            () => NavigateArchive(0),
            size: 14
        );
        _pageLabel = Text(_layout, "", .035f, .17f, .245f, .043f, 12, Muted);
        _pageLabel.textWrappingMode = TextWrappingModes.Normal;
        _battleNewer = Button(
            _layout,
            "↑",
            .29f,
            .835f,
            .038f,
            .033f,
            () =>
            {
                _timeline.ResetScroll = true;
                _pageBattles?.Invoke(-1);
            }
        );
        _battleOlder = Button(
            _layout,
            "↓",
            .335f,
            .835f,
            .038f,
            .033f,
            () =>
            {
                _timeline.ResetScroll = true;
                _pageBattles?.Invoke(1);
            }
        );
        _heading = Text(_layout, "", .4f, .21f, .54f, .055f, 24, Ink, true);
        var detailStatus = CreateRect("BattleStatus", _layout, .4f, .265f, .54f, .045f);
        _detailOutcome = Badge(detailStatus, "BattleResult", 0, .05f, .055f, .9f);
        _detailUnknownOutcome = Text(detailStatus, "?", 0, .05f, .055f, .9f, 16, Muted, true);
        _detailRank = Badge(detailStatus, "OpponentRank", .07f, 0, .06f, 1);
        _detailUnknownRank = Text(detailStatus, "?", .07f, 0, .06f, 1, 16, Muted, true);
        _detailRating = Text(detailStatus, "", .14f, 0, .18f, 1, 14, Ink);
        _metadata = Text(detailStatus, "", .33f, 0, .67f, 1, 14, Muted);
        _metadata.textWrappingMode = TextWrappingModes.Normal;
        _runSummary = Text(_layout, "", .4f, .153f, .32f, .045f, 13, Muted);
        _summaryRank = Badge(_layout, "PlayerRank", .724f, .148f, .031f, .054f);
        _summaryUnknownRank = Text(_layout, "?", .724f, .148f, .031f, .054f, 16, Muted, true);
        _summaryRating = Text(_layout, "", .762f, .153f, .085f, .045f, 13, Ink);
        _runFacts = Button(
            _layout,
            T("Run data", "本局数据"),
            .855f,
            .157f,
            .09f,
            .035f,
            () =>
            {
                _factsVisible = !_factsVisible;
                UpdateFacts();
            },
            size: 13
        );
        _opponentPreview = CreateRect("OpponentBoardBounds", _layout, .39f, .32f, .56f, .235f);
        _opponentStatus = Text(_opponentPreview, "", 0, .4f, 1, .2f, 17, Muted, true);
        _preview = CreateRect("PlayerBoardBounds", _layout, .39f, .59f, .56f, .25f);
        _previewStatus = Text(_preview, "", 0, .35f, 1, .3f, 17, Muted, true);
        _statusLabel = Text(_layout, "", .04f, .953f, .91f, .03f, 12, Muted);
        CreateActions();
        CreateFacts();
    }

    private void NavigateArchive(int direction)
    {
        _archiveList.ResetScroll = true;
        _timeline.ResetScroll = true;
        _pageArchive?.Invoke(direction);
    }

    private PageList CreatePageList(
        string name,
        float x,
        float y,
        float width,
        float height,
        float rowHeight,
        bool timeline
    )
    {
        var viewport = CreateRect(name, _layout!, x, y, width, height);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45;
        var content = CreateRect("Rows", viewport, 0, 0, 1, 1);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        scroll.content = content;
        var list = new PageList { Scroll = scroll, Height = rowHeight };
        for (var i = 0; i < list.Rows.Length; i++)
        {
            var index = i;
            var button = Button(
                content,
                "",
                0,
                0,
                1,
                1,
                () =>
                {
                    if (timeline || !ShowsBothBoards)
                        _selectBattle(index);
                    else
                    {
                        _timeline.ResetScroll = true;
                        _selectRun(index);
                    }
                },
                size: timeline ? 15 : 14
            );
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1);
            rect.sizeDelta = new Vector2(0, rowHeight - 5);
            rect.anchoredPosition = new Vector2(0, -i * rowHeight);
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.alignment = timeline
                ? TextAlignmentOptions.Center
                : TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.rectTransform.anchorMin = new Vector2(
                timeline ? .04f : .26f,
                timeline ? .38f : .06f
            );
            label.rectTransform.anchorMax = new Vector2(timeline ? .31f : .77f, .94f);
            var portrait = CreateRect("Hero", rect, .025f, .11f, .21f, .70f)
                .gameObject.AddComponent<Image>();
            portrait.raycastTarget = false;
            portrait.preserveAspect = true;
            portrait.color = Color.clear;
            var badge = timeline
                ? Badge(rect, "Outcome", .33f, .08f, .26f, .52f)
                : Badge(rect, "Outcome", .82f, .71f, .14f, .25f);
            var rank = timeline
                ? Badge(rect, "Rank", .65f, .05f, .31f, .56f)
                : Badge(rect, "Rank", .80f, .05f, .18f, .45f);
            var unknownResult = Text(badge.transform, "?", 0, 0, 1, 1, 14, Muted, true);
            var unknownRank = Text(rank.transform, "?", 0, 0, 1, 1, 14, Muted, true);
            var rating = timeline
                ? Text(rect, "", .04f, .66f, .92f, .24f, 11, Ink, true)
                : Text(rect, "", .77f, .49f, .22f, .18f, 10, Ink, true);
            list.Rows[i] = new Row
            {
                Button = button,
                Portrait = portrait,
                Badge = badge,
                RankBadge = rank,
                Rating = rating,
                UnknownResult = unknownResult,
                UnknownRank = unknownRank,
            };
            button.gameObject.SetActive(false);
        }
        return list;
    }

    private void BindRows(
        PageList list,
        IReadOnlyList<string> ids,
        Func<int, string> label,
        Func<int, string?> hero,
        Func<int, Sprite?> badge,
        Func<int, string?> rank,
        Func<int, int?> rating,
        int selected
    )
    {
        var offset = Mathf.Max(0, list.Scroll.content.anchoredPosition.y);
        var oldTop = Math.Min(39, (int)(offset / list.Height));
        var topId = list.Rows[oldTop].Id;
        var newTop = -1;
        for (var i = 0; i < list.Rows.Length; i++)
        {
            var row = list.Rows[i];
            row.Button.gameObject.SetActive(i < ids.Count);
            if (i >= ids.Count)
            {
                row.Id = null;
                continue;
            }
            row.Id = ids[i];
            if (row.Id == topId)
                newTop = i;
            SetButton(row.Button, label(i), i == selected, !_model!.PageLoading);
            row.Badge.sprite = badge(i);
            row.Badge.color = row.Badge.sprite != null ? Color.white : Color.clear;
            row.UnknownResult.gameObject.SetActive(row.Badge.sprite == null);
            BindRank(row.RankBadge, row.Rating, row.UnknownRank, rank(i), rating(i));
            var name = hero(i);
            if (row.Hero != name)
            {
                row.Hero = name;
                row.Portrait.color = Color.clear;
                _portraits.RemoveAll(p => p.Target == row.Portrait);
                if (TheDragonsHeroIdentity.TryResolve(name, out var identity))
                    _portraits.Add(
                        (
                            row.Portrait,
                            HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(identity),
                            _generation
                        )
                    );
            }
        }
        list.Scroll.content.sizeDelta = new Vector2(0, ids.Count * list.Height);
        var nextOffset =
            list.ResetScroll ? 0
            : newTop >= 0 ? newTop * list.Height + offset % list.Height
            : Math.Max(0, selected) * list.Height;
        list.Scroll.content.anchoredPosition = new Vector2(
            0,
            Mathf.Clamp(
                nextOffset,
                0,
                Mathf.Max(0, list.Scroll.content.sizeDelta.y - list.Scroll.viewport.rect.height)
            )
        );
        list.ResetScroll = false;
    }

    private Sprite? RunBadge(HistoryRunRecord run) =>
        HistoryPanelFormatter.GetRunOutcomeTier(run) switch
        {
            RunOutcomeTier.Bronze => _skin?[4],
            RunOutcomeTier.Silver => _skin?[5],
            RunOutcomeTier.Gold => _skin?[6],
            RunOutcomeTier.Diamond => _skin?[7],
            _ => _stateBadges.Length == 0
                ? null
                : _stateBadges[
                    run.RawStatus == "active" ? 2
                    : run.RawStatus == "abandoned" ? 1
                    : 0
                ],
        };

    private void UpdateContent()
    {
        if (_layout == null || _model is not { } m)
            return;
        var runs = ShowsBothBoards;
        if (_boundSection != m.SectionMode)
        {
            _boundSection = m.SectionMode;
            _layoutFrames = 2;
            _archiveList.ResetScroll = true;
            _preview!.anchorMin = new Vector2(.39f, runs ? .16f : .19f);
            _preview.anchorMax = new Vector2(.95f, runs ? .41f : .67f);
        }
        _title.text = m.Title;
        SetButton(_runsTab, HistoryPanelText.RunsTab(), runs);
        SetButton(_ghostTab, HistoryPanelText.GhostTab(), !runs);
        _heroFilters.SetActive(runs);
        _ghostFilters.SetActive(!runs);
        foreach (var pair in _heroes)
            SetButton(
                pair.Value,
                HistoryPanelHeroPresentation.DisplayName(pair.Key),
                HistoryPanelHeroPresentation.IsSelected(m.SelectedRunHero, pair.Key)
            );
        foreach (var pair in _outcomes)
            SetButton(
                pair.Value,
                pair.Key == GhostBattleFilter.All ? T("All", "全部")
                    : pair.Key == GhostBattleFilter.IWon ? T("Won", "我赢了")
                    : T("Lost", "我输了"),
                pair.Key == m.GhostBattleFilter
            );
        SetButton(_dayFilter, HistoryPanelText.FilterDayMin10(), m.GhostDayMin10);
        _timeline.Scroll.gameObject.SetActive(runs);
        _opponentPreview!.gameObject.SetActive(runs);
        _battleNewer.gameObject.SetActive(runs);
        _battleOlder.gameObject.SetActive(runs);
        _newer.interactable = m.HasNewer && !m.PageLoading;
        _older.interactable = m.HasOlder && !m.PageLoading;
        _latest.interactable = !m.PageLoading;
        _battleNewer.interactable = m.BattleHasNewer;
        _battleOlder.interactable = m.BattleHasOlder;
        _pageLabel.text = m.PageLoading ? HistoryPanelText.LoadingPreview() : m.PageRange;
        if (runs)
            BindRows(
                _archiveList,
                m.Runs.Select(r => r.RunId).ToArray(),
                i => HistoryPanelFormatter.RunListText(m.Runs[i]),
                i => m.Runs[i].Hero,
                i => RunBadge(m.Runs[i]),
                i => m.Runs[i].PlayerRank,
                i => m.Runs[i].PlayerRating,
                m.SelectedRunIndex
            );
        else
            BindRows(
                _archiveList,
                m.VisibleBattles.Select(b => b.BattleId).ToArray(),
                i => HistoryPanelFormatter.GhostListText(m.VisibleBattles[i]),
                i => m.VisibleBattles[i].OpponentHero,
                i => BattleBadge(m.VisibleBattles[i]),
                i => m.VisibleBattles[i].OpponentRank,
                i => m.VisibleBattles[i].OpponentRating,
                m.SelectedBattleIndex
            );
        if (runs)
            BindRows(
                _timeline,
                m.VisibleBattles.Select(b => b.BattleId).ToArray(),
                i => HistoryPanelFormatter.FormatDayOnly(m.VisibleBattles[i].Day),
                _ => null,
                i => BattleBadge(m.VisibleBattles[i]),
                i => m.VisibleBattles[i].OpponentRank,
                i => m.VisibleBattles[i].OpponentRating,
                m.SelectedBattleIndex
            );
        _heading.text = m.DetailOpponentName;
        var battle = m.DetailBattle;
        _detailOutcome.gameObject.SetActive(battle != null);
        _detailOutcome.sprite = BattleBadge(battle);
        _detailOutcome.color = _detailOutcome.sprite != null ? Color.white : Color.clear;
        _detailUnknownOutcome.gameObject.SetActive(battle != null && _detailOutcome.sprite == null);
        _detailRank.gameObject.SetActive(battle != null);
        _detailRating.gameObject.SetActive(battle != null);
        BindRank(
            _detailRank,
            _detailRating,
            _detailUnknownRank,
            battle?.OpponentRank,
            battle?.OpponentRating
        );
        _detailUnknownRank.gameObject.SetActive(battle != null && _detailRank.sprite == null);
        _metadata.text =
            m.DetailMetaText
            + (
                string.IsNullOrEmpty(m.GhostOpponentEliminatedNoticeText)
                    ? ""
                    : "\n" + m.GhostOpponentEliminatedNoticeText
            );
        _runSummary.text = m.RunSummary;
        var run = runs ? m.Runs.ElementAtOrDefault(m.SelectedRunIndex) : null;
        _summaryRank.gameObject.SetActive(run != null);
        _summaryRating.gameObject.SetActive(run != null);
        BindRank(
            _summaryRank,
            _summaryRating,
            _summaryUnknownRank,
            run?.PlayerRank,
            run?.PlayerRating
        );
        _summaryUnknownRank.gameObject.SetActive(run != null && _summaryRank.sprite == null);
        _runFacts.gameObject.SetActive(runs && m.Runs.Count > 0);
        _statusLabel.text = m.StatusMessage ?? "";
        _statusLabel.color = StatusColor(m.StatusSeverity);
        _supporterAttribution?.Bind(m.Supporters, HistoryPanelText.Subtitle());
        RefreshActions();
        UpdateFacts();
    }

    private static Image Badge(
        Transform parent,
        string name,
        float x,
        float y,
        float width,
        float height
    )
    {
        var image = CreateRect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.color = Color.clear;
        return image;
    }

    private Sprite? BattleBadge(HistoryBattleRecord? battle) =>
        battle == null ? null
        : HistoryPanelFormatter.IsBattleWin(battle) ? _winBadge
        : HistoryPanelFormatter.IsBattleLoss(battle) ? _lossBadge
        : null;

    private void BindRank(
        Image badge,
        TextMeshProUGUI ratingLabel,
        TextMeshProUGUI unknown,
        string? rank,
        int? rating
    )
    {
        badge.sprite = _rankBadges.Get(rank);
        badge.color = badge.sprite != null ? Color.white : Color.clear;
        unknown.gameObject.SetActive(badge.sprite == null);
        ratingLabel.text =
            string.Equals(rank?.Trim(), "Legendary", StringComparison.OrdinalIgnoreCase)
            && rating.HasValue
                ? $"ELO {rating.Value}"
                : string.Empty;
    }

    private void CreateFacts()
    {
        var rect = CreateRect("RunFacts", _layout!, .43f, .32f, .48f, .30f);
        var canvas = rect.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = BppOverlaySorting.PanelForeground;
        rect.gameObject.AddComponent<GraphicRaycaster>();
        rect.gameObject.AddComponent<Image>().color = new Color(.075f, .05f, .03f, 1);
        Panel(rect, 0, 0, 1, 1);
        Button(
            rect,
            HistoryPanelText.Close(),
            .76f,
            .05f,
            .2f,
            .13f,
            () =>
            {
                _factsVisible = false;
                UpdateFacts();
            },
            size: 14
        );
        _factsText = Text(rect, "", .07f, .22f, .86f, .7f, 20);
        _factsText.textWrappingMode = TextWrappingModes.Normal;
        _factsRoot = rect.gameObject;
        _factsRoot.SetActive(false);
    }

    private void UpdateFacts()
    {
        if (_factsRoot == null || _model == null)
            return;
        _factsRoot.SetActive(
            _factsVisible && ShowsBothBoards && _model.Runs.Count > 0 && !_moreVisible
        );
        _factsText.text = _model.RunFacts;
    }
}
