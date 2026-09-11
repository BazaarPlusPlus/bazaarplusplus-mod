#nullable enable
using BazaarPlusPlus.GameInterop.AssetLoading;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using BazaarPlusPlus.Localization;
using TheBazaar.AppFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

// Owned native-art archive layout using the existing history selection state.
internal sealed partial class HistoryPanelView : IDisposable
{
    private const string Art = "Assets/TheBazaar/Art/UI/Buttons/";
    private static readonly string[] Addresses =
    [
        Art + "Btn_Rectangle/Btn_Rct_Frame_S_TUI.png",
        Art + "Btn_Rectangle/Btn_Rct_Brown_S_Center_Active_TUI.png",
        Art + "Btn_Rectangle/Btn_Rct_Blue_S_Center_Active_TUI.png",
        Art + "Btn_MatchHistory/Btn_MatchHistory_Run_Gold_Active_TUI.png",
        "Trophy_Bronze_TUI",
        "Trophy_Silver_TUI",
        "Trophy_Gold_TUI",
        "Trophy_Diamond_TUI",
    ];
    private static readonly Color Ink = new(0.96f, 0.89f, 0.73f);
    private static readonly Color Muted = new(0.69f, 0.63f, 0.53f);
    private readonly Transform _parent;
    private readonly Action _close;
    private readonly Action _replay;
    private readonly Action<int> _selectRun;
    private readonly Action<int> _selectBattle;
    private readonly Action<HistorySectionMode> _section;
    private readonly Action<GhostBattleFilter> _ghostFilter;
    private readonly Action<string> _heroFilter;
    private readonly Action _ghostDay;
    private readonly List<(
        Image Target,
        Task<HeroPortraitLoadOutcome?> Load,
        int Generation
    )> _portraits = new();
    private GameObject? _root;
    private RectTransform? _layout;
    private RectTransform? _preview;
    private RectTransform? _opponentPreview;
    private Rect _lastOpponentBounds;
    private TextMeshProUGUI? _opponentStatus;
    private string _opponentMessage = string.Empty;
    private TextMeshProUGUI? _previewStatus;
    private NativeGameTypography.OwnedTextPreparation? _font;
    private Task<Sprite?[]>? _skinLoad;
    private Sprite?[]? _skin;
    private Texture2D? _resultAtlas;
    private Sprite? _winBadge;
    private Sprite? _lossBadge;
    private Texture2D? _stateAtlas;
    private Sprite[] _stateBadges = Array.Empty<Sprite>();
    private HistoryPanelViewModel? _model;
    private string? _status;
    private bool _statusVisible;
    private bool _visible;
    private bool _disposed;
    private float _runScrollPosition = 1f;
    private float _battleScrollPosition = 1f;
    private int _generation;
    private float _retryAt;
    private Rect _lastBounds;

    public event Action<Rect>? PreviewContainerBoundsChanged;
    public event Action<Rect>? OpponentBoundsChanged;
    internal bool ShowsBothBoards => _model?.SectionMode == HistorySectionMode.Runs;

    internal void SetOpponentStatus(string message)
    {
        _opponentMessage = message;
        if (_opponentStatus != null)
            _opponentStatus.text = message;
    }

    internal HistoryPanelView(
        Transform parent,
        Action close,
        Action replay,
        Action record,
        Action delete,
        Action checkHealth,
        Action<string?> submitAccountCode,
        Action toggleAccountLink,
        Action markAccountLinked,
        Action<int> selectRun,
        Action<int> selectBattle,
        Action<HistorySectionMode> section,
        Action<GhostBattleFilter> ghostFilter,
        Action<string> heroFilter,
        Action ghostDay
    )
    {
        _parent = parent;
        _close = close;
        _replay = replay;
        _record = record;
        _delete = delete;
        _checkHealth = checkHealth;
        _submitAccountCode = submitAccountCode;
        _toggleAccountLink = toggleAccountLink;
        _markAccountLinked = markAccountLinked;
        _selectRun = selectRun;
        _selectBattle = selectBattle;
        _section = section;
        _ghostFilter = ghostFilter;
        _heroFilter = heroFilter;
        _ghostDay = ghostDay;
    }

    public void EnsureCreated()
    {
        if (_disposed || _root != null)
            return;
        if (NativeGameTypography.PrepareOwnedText(out _font) != NativeGameTypography.Outcome.Ready)
            return;
        LoadResultBadges();
        _root = new GameObject(
            "HistoryPanelView",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        _root.transform.SetParent(_parent, false);
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = BppOverlaySorting.PanelUiToolkit;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 1000);
        scaler.matchWidthOrHeight = 0.5f;
        _root.SetActive(_visible);
        Rebuild();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        EnsureCreated();
        if (_root != null)
            _root.SetActive(visible);
        if (!visible)
        {
            _accountCode = string.Empty;
            _accountInput?.SetTextWithoutNotify(string.Empty);
            _accountInput = null;
            _moreVisible = false;
        }
        if (visible)
        {
            _lastBounds = default;
            Rebuild();
        }
    }

    public bool IsTextInputFocused() => _accountInput != null && _accountInput.isFocused;

    public void Refresh(HistoryPanelViewModel model)
    {
        _model = model;
        if (_visible)
            Rebuild();
    }

    public void SetPreviewStatus(string? message, bool visible)
    {
        _status = message;
        _statusVisible = visible;
        if (_previewStatus != null)
        {
            _previewStatus.text = message ?? string.Empty;
            _previewStatus.gameObject.SetActive(visible);
        }
    }

    public void Tick()
    {
        if (!_visible || _disposed)
            return;
        EnsureCreated();
        if (
            (_skin == null || _skin.Any(sprite => sprite == null))
            && _skinLoad == null
            && Time.unscaledTime >= _retryAt
            && Services.TryGet<AssetLoader>(out var loader)
            && loader != null
        )
        {
            _retryAt = Time.unscaledTime + 5;
            _skinLoad = Task.WhenAll(
                Addresses.Select(address =>
                    NativeGlobalAssetLoader.LoadByAddressAsync<Sprite>(loader, address)
                )
            );
        }
        if (_skinLoad?.IsCompleted == true)
        {
            var changed = false;
            if (_skinLoad.Status == TaskStatus.RanToCompletion)
            {
                var loaded = _skinLoad.Result;
                changed =
                    _skin == null
                    || Enumerable.Range(0, loaded.Length).Any(i => loaded[i] != _skin[i]);
                _skin = loaded;
            }
            else
                _ = _skinLoad.Exception;
            _skinLoad = null;
            if (changed)
                Rebuild();
        }
        for (var i = _portraits.Count - 1; i >= 0; i--)
        {
            var request = _portraits[i];
            if (!request.Load.IsCompleted)
                continue;
            if (
                request.Generation == _generation
                && request.Target != null
                && request.Load.Status == TaskStatus.RanToCompletion
                && request.Load.Result?.Sprite != null
            )
            {
                request.Target.sprite = request.Load.Result.Sprite;
                request.Target.color = Color.white;
            }
            else if (request.Load.IsFaulted)
                _ = request.Load.Exception;
            _portraits.RemoveAt(i);
        }
        if (_preview == null || !_preview.gameObject.activeInHierarchy)
            return;
        Canvas.ForceUpdateCanvases();
        var corners = new Vector3[4];
        _preview.GetWorldCorners(corners);
        var bottom = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        var top = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        var bounds = Rect.MinMaxRect(bottom.x, bottom.y, top.x, top.y);
        if (bounds.width > 1 && bounds.height > 1 && bounds != _lastBounds)
        {
            _lastBounds = bounds;
            PreviewContainerBoundsChanged?.Invoke(bounds);
        }
        if (_opponentPreview != null)
        {
            _opponentPreview.GetWorldCorners(corners);
            bottom = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            top = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            var opponentBounds = Rect.MinMaxRect(bottom.x, bottom.y, top.x, top.y);
            if (
                opponentBounds.width > 1
                && opponentBounds.height > 1
                && opponentBounds != _lastOpponentBounds
            )
            {
                _lastOpponentBounds = opponentBounds;
                OpponentBoundsChanged?.Invoke(opponentBounds);
            }
        }
    }

    private void Rebuild()
    {
        if (_root == null || !_visible || _model == null)
            return;
        _accountCode = _accountInput != null ? _accountInput.text : _accountCode;
        _accountInput = null;
        _generation++;
        _portraits.Clear();
        _opponentPreview = null;
        _opponentStatus = null;
        _lastOpponentBounds = default;
        if (_layout != null)
        {
            _layout.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(_layout.gameObject);
        }
        _layout = CreateRect("Layout", _root.transform, 0, 0, 1, 1);
        var background = _layout.gameObject.AddComponent<Image>();
        background.color = new Color(0.055f, 0.038f, 0.026f, 1);
        Panel(_layout, 0.014f, 0.018f, .972f, .962f);
        Text(_layout, _model.Title, .04f, .032f, .37f, .055f, 34);
        Button(_layout, HistoryPanelText.Close(), .87f, .04f, .09f, .045f, _close);
        Filters();
        BuildArchive();
        var status = Text(
            _layout,
            _model.StatusMessage ?? string.Empty,
            .04f,
            .955f,
            .91f,
            .026f,
            12,
            StatusColor(_model.StatusSeverity)
        );
        status.textWrappingMode = TextWrappingModes.Normal;
        status.overflowMode = TextOverflowModes.Ellipsis;
        if (_moreVisible)
            BuildMoreDialog();
        _lastBounds = default;
    }

    private void Filters()
    {
        var model = _model!;
        Button(
            _layout!,
            HistoryPanelText.RunsTab(),
            .04f,
            .105f,
            .095f,
            .038f,
            () =>
            {
                _runScrollPosition = 1f;
                _battleScrollPosition = 1f;
                _section(HistorySectionMode.Runs);
            },
            model.SectionMode == HistorySectionMode.Runs
        );
        Button(
            _layout!,
            HistoryPanelText.GhostTab(),
            .145f,
            .105f,
            .095f,
            .038f,
            () =>
            {
                _runScrollPosition = 1f;
                _battleScrollPosition = 1f;
                _section(HistorySectionMode.Ghost);
            },
            model.SectionMode == HistorySectionMode.Ghost
        );
        if (model.SectionMode == HistorySectionMode.Runs)
        {
            var heroes = HistoryPanelHeroPresentation.RunFilterHeroIds;
            for (var i = 0; i < heroes.Count; i++)
            {
                var hero = heroes[i];
                Button(
                    _layout!,
                    HistoryPanelHeroPresentation.DisplayName(hero),
                    .265f + i * .087f,
                    .105f,
                    .08f,
                    .038f,
                    () =>
                    {
                        _runScrollPosition = 1f;
                        _battleScrollPosition = 1f;
                        _heroFilter(hero);
                    },
                    model.SelectedRunHero == hero,
                    13
                );
            }
        }
        else
        {
            var filters = new[]
            {
                GhostBattleFilter.All,
                GhostBattleFilter.IWon,
                GhostBattleFilter.ILost,
            };
            var labels = new[] { T("All", "全部"), T("Won", "胜利"), T("Lost", "失败") };
            for (var i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                Button(
                    _layout!,
                    labels[i],
                    .28f + .12f * i,
                    .105f,
                    .11f,
                    .038f,
                    () =>
                    {
                        _ghostFilter(filter);
                    },
                    model.GhostBattleFilter == filter
                );
            }
            Button(
                _layout!,
                T("Day 10+", "第 10 天起"),
                .66f,
                .105f,
                .15f,
                .038f,
                _ghostDay,
                model.GhostDayMin10
            );
        }
    }

    private void BuildArchive()
    {
        var m = _model!;
        Text(_layout!, m.CountChipText, .045f, .17f, .25f, .04f, 20);
        if (m.SectionMode == HistorySectionMode.Runs)
        {
            var content = ScrollList(
                "RunList",
                .035f,
                .225f,
                .19f,
                .65f,
                m.Runs.Count * 88f,
                _runScrollPosition,
                value => _runScrollPosition = value
            );
            for (var index = 0; index < m.Runs.Count; index++)
            {
                var modelIndex = index;
                var run = m.Runs[index];
                var row = (RectTransform)
                    Button(
                        content,
                        string.Empty,
                        0,
                        0,
                        1,
                        1,
                        () =>
                        {
                            _battleScrollPosition = 1f;
                            _selectRun(modelIndex);
                        },
                        index == m.SelectedRunIndex
                    ).transform;
                row.anchorMin = new Vector2(0, 1);
                row.anchorMax = Vector2.one;
                row.pivot = new Vector2(.5f, 1);
                row.sizeDelta = new Vector2(0, 83);
                row.anchoredPosition = new Vector2(0, -index * 88);
                Portrait(row, run.Hero, .042f, .108f, .279f, .783f);
                var tier = HistoryPanelFormatter.GetRunOutcomeTier(run);
                var trophyIndex = tier switch
                {
                    RunOutcomeTier.Bronze => 4,
                    RunOutcomeTier.Silver => 5,
                    RunOutcomeTier.Gold => 6,
                    RunOutcomeTier.Diamond => 7,
                    _ => -1,
                };
                var stateIndex =
                    tier == RunOutcomeTier.Misfortune ? 0
                    : run.RawStatus == "abandoned" ? 1
                    : run.RawStatus == "active" ? 2
                    : -1;
                var outcomeSprite =
                    trophyIndex >= 0 ? _skin?[trophyIndex]
                    : stateIndex >= 0 && _stateBadges.Length > stateIndex ? _stateBadges[stateIndex]
                    : null;
                if (outcomeSprite != null)
                {
                    var trophy = CreateRect("RunOutcomeBadge", row, .826f, .060f, .142f, .422f)
                        .gameObject.AddComponent<Image>();
                    trophy.sprite = outcomeSprite;
                    trophy.preserveAspect = true;
                    trophy.raycastTarget = false;
                }
                var record = $"{run.Victories ?? 0}{T("W", "胜")} {run.Losses ?? 0}{T("L", "负")}";
                if (trophyIndex < 0)
                {
                    var state =
                        tier == RunOutcomeTier.Misfortune
                            ? T("Misfortune journey", "惨淡旅程")
                            : HistoryPanelFormatter.FormatRunStatus(run.RawStatus);
                    var stateLabel = Text(
                        row,
                        state,
                        .347f,
                        .036f,
                        .447f,
                        .277f,
                        17,
                        run.RawStatus == "active" ? Ink : Muted
                    );
                    stateLabel.enableAutoSizing = true;
                    stateLabel.fontSizeMin = 11;
                    stateLabel.fontSizeMax = 17;
                    Text(row, record, .347f, .313f, .60f, .217f, 13);
                }
                else
                    Text(row, record, .347f, .096f, .47f, .325f, 18);
                Text(
                    row,
                    HistoryPanelFormatter.FormatTimestamp(run.StartedAtUtc),
                    .347f,
                    .627f,
                    .60f,
                    .193f,
                    11,
                    Muted
                );
            }
        }
        else
            BattleList();
        var detailX = ShowsBothBoards ? .355f : .35f;
        var detailWidth = .95f - detailX;
        if (ShowsBothBoards)
        {
            Text(_layout!, m.BattleChipText, .24f, .17f, .095f, .04f, 20);
            BattleTimeline(.24f, .225f, .095f, .65f);
        }
        BattleHeading(detailX + .01f, .225f, detailWidth - .02f);
        if (ShowsBothBoards)
        {
            Preview(detailX, .665f, detailWidth, .22f);
            _opponentPreview = CreateRect(
                "OpponentBoardPreviewBounds",
                _layout!,
                detailX,
                .395f,
                detailWidth,
                .22f
            );
            _opponentStatus = Text(
                _layout!,
                _opponentMessage,
                detailX + .01f,
                .475f,
                detailWidth - .02f,
                .05f,
                16,
                Muted,
                true
            );
        }
        else
            Preview(detailX, .46f, detailWidth, .37f);
        BuildActions();
    }

    private void BattleHeading(float x, float y, float width)
    {
        var m = _model!;
        var battle =
            m.SelectedBattleIndex >= 0 && m.SelectedBattleIndex < m.VisibleBattles.Count
                ? m.VisibleBattles[m.SelectedBattleIndex]
                : null;
        if (battle == null)
        {
            Text(_layout!, m.DetailPlaceholderText, x, y, width, .12f, 24);
            return;
        }
        Portrait(_layout!, battle.PlayerHero, x, y, .075f, .115f, true);
        Portrait(_layout!, battle.OpponentHero, x + width - .075f, y, .075f, .115f, true);
        Text(_layout!, m.DetailResultText, x + .115f, y + .006f, width - .23f, .04f, 28, Ink, true);
        Text(
            _layout!,
            m.DetailOpponentName,
            x + .115f,
            y + .052f,
            width - .23f,
            .032f,
            21,
            Ink,
            true
        );
        Text(
            _layout!,
            m.DetailMetaText,
            x + .115f,
            y + .094f,
            width - .23f,
            .027f,
            14,
            Muted,
            true
        );
        if (!string.IsNullOrEmpty(m.GhostOpponentEliminatedNoticeText))
            Text(
                _layout!,
                m.GhostOpponentEliminatedNoticeText,
                x,
                y + .145f,
                width,
                .036f,
                15,
                Ink,
                true
            );
    }

    private void BattleTimeline(float x, float y, float width, float height)
    {
        var m = _model!;
        var content = ScrollList(
            "BattleTimeline",
            x,
            y,
            width,
            height,
            m.VisibleBattles.Count * 58f,
            _battleScrollPosition,
            value => _battleScrollPosition = value
        );
        for (var index = 0; index < m.VisibleBattles.Count; index++)
        {
            var modelIndex = index;
            var battle = m.VisibleBattles[index];
            var won = HistoryPanelFormatter.IsBattleWin(battle);
            var lost = HistoryPanelFormatter.IsBattleLoss(battle);
            var row = CreateRect("BattleTimelineEntry", content, 0, 0, 1, 1);
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = Vector2.one;
            row.pivot = new Vector2(.5f, 1);
            row.sizeDelta = new Vector2(0, 52);
            row.anchoredPosition = new Vector2(0, -index * 58);
            var background = row.gameObject.AddComponent<Image>();
            background.color = new Color(26f / 255f, 13f / 255f, 2f / 255f);
            var action = row.gameObject.AddComponent<Button>();
            action.targetGraphic = background;
            action.onClick.AddListener(() => _selectBattle(modelIndex));
            if (index == m.SelectedBattleIndex)
            {
                background.sprite = _skin?[1];
                background.type = Image.Type.Sliced;
                background.pixelsPerUnitMultiplier = 2.5f;
                background.color =
                    background.sprite != null ? Color.white : new Color(.23f, .16f, .07f);
                Layer(row, 0, Ink);
            }
            ResultMark(row, won, lost);
            Text(row, HistoryPanelFormatter.FormatDayOnly(battle.Day), .34f, .12f, .62f, .76f, 17);
            if (!won && !lost)
                Text(
                    row,
                    HistoryPanelFormatter.FormatBattleResult(battle),
                    .34f,
                    .64f,
                    .62f,
                    .3f,
                    11,
                    Muted
                );
        }
    }

    private RectTransform ScrollList(
        string name,
        float x,
        float y,
        float width,
        float height,
        float contentHeight,
        float position,
        Action<float> onScroll
    )
    {
        var viewport = CreateRect(name + "Viewport", _layout!, x, y, width, height);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 35f;
        scroll.viewport = viewport;
        var content = CreateRect(name + "Content", viewport, 0, 0, .94f, 1);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(.94f, 1);
        content.pivot = new Vector2(.5f, 1);
        content.sizeDelta = new Vector2(0, contentHeight);
        content.anchoredPosition = Vector2.zero;
        scroll.content = content;
        var track = CreateRect(name + "Scrollbar", viewport, .965f, 0, .025f, 1);
        track.gameObject.AddComponent<Image>().color = new Color(.22f, .18f, .11f);
        var handle = CreateRect("Handle", track, 0, 0, 1, 1);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = Muted;
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = position;
        var generation = _generation;
        scroll.onValueChanged.AddListener(value =>
        {
            if (generation == _generation)
                onScroll(value.y);
        });
        return content;
    }

    private void ResultMark(Transform parent, bool won, bool lost)
    {
        var badge =
            won ? _winBadge
            : lost ? _lossBadge
            : null;
        if (badge == null)
            return;
        var mark = CreateRect("BattleResultBadge", parent, .01f, .04f, .33f, .92f);
        var image = mark.gameObject.AddComponent<Image>();
        image.sprite = badge;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private void LoadResultBadges()
    {
        if (_resultAtlas == null)
        {
            var result = LoadBadgeAtlas("battle-result-badges.png", 2);
            _resultAtlas = result.Texture;
            _winBadge = result.Sprites[0];
            _lossBadge = result.Sprites[1];
        }
        if (_stateAtlas == null)
        {
            var states = LoadBadgeAtlas("run-state-badges.png", 3);
            _stateAtlas = states.Texture;
            _stateBadges = states.Sprites;
        }
    }

    private static (Texture2D Texture, Sprite[] Sprites) LoadBadgeAtlas(string name, int count)
    {
        using var stream = typeof(HistoryPanelView).Assembly.GetManifestResourceStream(
            $"BazaarPlusPlus.Resources.HistoryPanel.{name}"
        );
        if (stream == null)
            throw new InvalidOperationException($"History artwork is missing: {name}");
        using var bytes = new System.IO.MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        if (!texture.LoadImage(bytes.ToArray()))
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidOperationException($"History artwork could not be decoded: {name}");
        }
        var width = texture.width / (float)count;
        var sprites = Enumerable
            .Range(0, count)
            .Select(index =>
                Sprite.Create(
                    texture,
                    new Rect(index * width, 0, width, texture.height),
                    new Vector2(.5f, .5f)
                )
            )
            .ToArray();
        return (texture, sprites);
    }

    private void BattleList()
    {
        var m = _model!;
        var content = ScrollList(
            "GhostList",
            .04f,
            .225f,
            .27f,
            .65f,
            m.VisibleBattles.Count * 78f,
            _battleScrollPosition,
            value => _battleScrollPosition = value
        );
        for (var index = 0; index < m.VisibleBattles.Count; index++)
        {
            var modelIndex = index;
            var battle = m.VisibleBattles[index];
            var row = (RectTransform)
                Button(
                    content,
                    $"{battle.OpponentName}\n{HistoryPanelFormatter.FormatDayOnly(battle.Day)} · {HistoryPanelFormatter.FormatBattleResult(battle)}",
                    0,
                    0,
                    1,
                    1,
                    () => _selectBattle(modelIndex),
                    index == m.SelectedBattleIndex,
                    19
                ).transform;
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = Vector2.one;
            row.pivot = new Vector2(.5f, 1);
            row.sizeDelta = new Vector2(0, 72);
            row.anchoredPosition = new Vector2(0, -index * 78);
        }
    }

    private void Preview(float x, float y, float width, float height)
    {
        if (!ShowsBothBoards)
            Text(
                _layout!,
                HistoryPanelText.GhostPerspective(),
                x + .008f,
                y - .028f,
                width - .016f,
                .024f,
                15,
                Muted
            );
        _preview = CreateRect("NativeBoardPreviewBounds", _layout!, x, y, width, height);
        _previewStatus = Text(
            _layout!,
            _status ?? string.Empty,
            x + .02f,
            y + height * .42f,
            width - .04f,
            .07f,
            18,
            Muted,
            true
        );
        _previewStatus.gameObject.SetActive(_statusVisible);
    }

    private static string T(string english, string chinese) =>
        LocalizedTextHelpers.Resolve(new LocalizedTextSet(english, chinese));

    private static RectTransform CreateRect(
        string name,
        Transform parent,
        float x,
        float y,
        float width,
        float height
    )
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(x, 1 - y - height);
        rect.anchorMax = new Vector2(x + width, 1 - y);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private Image Layer(Transform parent, int spriteIndex, Color color)
    {
        var image = CreateRect("NativeArt", parent, 0, 0, 1, 1).gameObject.AddComponent<Image>();
        image.sprite = _skin?[spriteIndex];
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2.5f;
        image.color = image.sprite == null ? Color.clear : color;
        image.raycastTarget = false;
        return image;
    }

    private void Panel(Transform parent, float x, float y, float width, float height)
    {
        var rect = CreateRect("NativePanel", parent, x, y, width, height);
        Layer(rect, 1, new Color(.48f, .43f, .38f));
        Layer(rect, 0, new Color(.72f, .62f, .45f));
    }

    private Button Button(
        Transform parent,
        string label,
        float x,
        float y,
        float width,
        float height,
        Action click,
        bool selected = false,
        int size = 17
    )
    {
        var rect = CreateRect("HistoryAction", parent, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = _skin?[selected ? 2 : 1];
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2.5f;
        image.color = image.sprite == null ? new Color(.18f, .14f, .1f) : Color.white;
        Layer(rect, 0, selected ? Ink : new Color(.65f, .55f, .39f));
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => click());
        Text(rect, label, .04f, .06f, .92f, .88f, size, Ink, true);
        return button;
    }

    private TextMeshProUGUI Text(
        Transform parent,
        string label,
        float x,
        float y,
        float width,
        float height,
        int size,
        Color? color = null,
        bool center = false
    )
    {
        var text = CreateRect("Label", parent, x, y, width, height)
            .gameObject.AddComponent<TextMeshProUGUI>();
        _font?.Apply(text);
        text.text = label;
        text.fontSize = size;
        text.color = color ?? Ink;
        text.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private void Portrait(
        Transform parent,
        string? hero,
        float x,
        float y,
        float width,
        float height,
        bool framed = false
    )
    {
        var rect = CreateRect("HeroPortrait", parent, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.clear;
        image.preserveAspect = true;
        image.raycastTarget = false;
        if (TheDragonsHeroIdentity.TryResolve(hero, out var identity))
            _portraits.Add(
                (image, HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(identity), _generation)
            );
        if (framed && _skin?[3] != null)
        {
            var frame = Layer(rect, 3, Color.white);
            frame.type = Image.Type.Simple;
            frame.preserveAspect = true;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _generation++;
        _portraits.Clear();
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
        if (_winBadge != null)
            UnityEngine.Object.Destroy(_winBadge);
        if (_lossBadge != null)
            UnityEngine.Object.Destroy(_lossBadge);
        if (_resultAtlas != null)
            UnityEngine.Object.Destroy(_resultAtlas);
        foreach (var badge in _stateBadges)
            UnityEngine.Object.Destroy(badge);
        if (_stateAtlas != null)
            UnityEngine.Object.Destroy(_stateAtlas);
        _stateAtlas = null;
        _stateBadges = Array.Empty<Sprite>();
        _winBadge = _lossBadge = null;
        _resultAtlas = null;
        // Sprites and font assets belong to the shared native loaders.
    }
}
