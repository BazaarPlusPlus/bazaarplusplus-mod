#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal sealed class PostCombatImpactView : IDisposable
{
    private static readonly Color Backdrop = new(0.018f, 0.018f, 0.018f, 0.92f);
    private static readonly Color Surface = new(0.055f, 0.052f, 0.047f, 0.98f);
    private static readonly Color SurfaceRaised = new(0.09f, 0.085f, 0.075f, 1f);
    private static readonly Color Border = new(0.27f, 0.24f, 0.18f, 1f);
    private static readonly Color Text = new(0.94f, 0.92f, 0.86f, 1f);
    private static readonly Color Muted = new(0.57f, 0.55f, 0.50f, 1f);
    private static readonly Color Gold = new(0.92f, 0.74f, 0.34f, 1f);

    private readonly Transform _parent;
    private readonly Action _closeRequested;
    private readonly PostCombatImpactCardArtProvider _artProvider = new();
    private readonly HashSet<string> _expandedSources = new(StringComparer.Ordinal);
    private GameObject? _rootObject;
    private PanelSettings? _panelSettings;
    private UIDocument? _document;
    private NativeGameTypography.PanelScope? _typography;
    private VisualElement? _root;
    private ScrollView? _list;
    private CombatImpactReport _report = CombatImpactReport.Empty;
    private ECombatantId _selectedOwner = ECombatantId.Player;
    private Button? _playerButton;
    private Button? _opponentButton;

    internal PostCombatImpactView(Transform parent, Action closeRequested)
    {
        _parent = parent;
        _closeRequested = closeRequested;
    }

    internal bool IsVisible => _root?.style.display.value == DisplayStyle.Flex;

    internal void Show(CombatImpactReport report)
    {
        EnsureCreated();
        if (_root == null)
            return;

        _report = report;
        _selectedOwner = report.Sources.Any(source => source.Entity.Owner == ECombatantId.Player)
            ? ECombatantId.Player
            : ECombatantId.Opponent;
        _expandedSources.Clear();
        Rebuild();
        _root.style.display = DisplayStyle.Flex;
        _root.Focus();
    }

    internal void Hide()
    {
        if (_root != null)
            _root.style.display = DisplayStyle.None;
    }

    private void EnsureCreated()
    {
        if (_rootObject != null)
            return;

        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.sortingOrder = BppOverlaySorting.PanelUiToolkit + 1;
        _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        _panelSettings.match = 1f;
        _panelSettings.clearColor = false;
        _panelSettings.targetDisplay = 0;
        if (
            NativeGameTypography.TryAttachPanel(_panelSettings, out _typography)
                != NativeGameTypography.Outcome.Ready
            || _typography == null
        )
        {
            UnityEngine.Object.DestroyImmediate(_panelSettings);
            _panelSettings = null;
            return;
        }

        _rootObject = new GameObject("PostCombatImpactUiToolkitRoot");
        _rootObject.transform.SetParent(_parent, false);
        _document = _rootObject.AddComponent<UIDocument>();
        _document.panelSettings = _panelSettings;
        _root = _document.rootVisualElement;
        _root.style.position = Position.Absolute;
        _root.style.left = 0f;
        _root.style.right = 0f;
        _root.style.top = 0f;
        _root.style.bottom = 0f;
        _root.style.display = DisplayStyle.None;
        _root.style.backgroundColor = Backdrop;
        _root.style.alignItems = Align.Center;
        _root.style.justifyContent = Justify.Center;
        _root.pickingMode = PickingMode.Position;
        _root.focusable = true;
        _root.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopPropagation();
                _closeRequested();
            }
        });
        _typography.Apply(_root);

        var panel = new VisualElement();
        panel.style.width = new Length(88f, LengthUnit.Percent);
        panel.style.maxWidth = 1450f;
        panel.style.height = new Length(88f, LengthUnit.Percent);
        panel.style.maxHeight = 920f;
        panel.style.backgroundColor = Surface;
        panel.style.borderTopWidth = 1f;
        panel.style.borderBottomWidth = 1f;
        panel.style.borderLeftWidth = 1f;
        panel.style.borderRightWidth = 1f;
        panel.style.borderTopColor = Border;
        panel.style.borderBottomColor = Border;
        panel.style.borderLeftColor = Border;
        panel.style.borderRightColor = Border;
        panel.style.borderTopLeftRadius = 10f;
        panel.style.borderTopRightRadius = 10f;
        panel.style.borderBottomLeftRadius = 10f;
        panel.style.borderBottomRightRadius = 10f;
        _root.Add(panel);

        panel.Add(BuildHeader());
        _list = new ScrollView(ScrollViewMode.Vertical);
        _list.style.flexGrow = 1f;
        _list.style.paddingLeft = 28f;
        _list.style.paddingRight = 28f;
        _list.style.paddingBottom = 24f;
        panel.Add(_list);
    }

    private VisualElement BuildHeader()
    {
        var header = new VisualElement();
        header.style.height = 92f;
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.paddingLeft = 30f;
        header.style.paddingRight = 22f;
        header.style.borderBottomWidth = 1f;
        header.style.borderBottomColor = Border;

        var titleColumn = new VisualElement();
        titleColumn.style.flexGrow = 1f;
        titleColumn.Add(Label(T("战斗影响", "Combat impact"), 25f, Text, FontStyle.Bold));
        titleColumn.Add(
            Label(T("按产生的非零效果排序", "Non-zero effects, ranked by source"), 14f, Muted)
        );
        header.Add(titleColumn);

        _playerButton = BuildOwnerButton(ECombatantId.Player, T("我方", "Player"));
        _opponentButton = BuildOwnerButton(ECombatantId.Opponent, T("对手", "Opponent"));
        header.Add(_playerButton);
        header.Add(_opponentButton);

        var close = new Button(_closeRequested) { text = "×" };
        close.style.width = 46f;
        close.style.height = 46f;
        close.style.marginLeft = 18f;
        close.style.fontSize = 28f;
        close.style.color = Text;
        close.style.backgroundColor = Color.clear;
        close.style.borderTopWidth = 0f;
        close.style.borderBottomWidth = 0f;
        close.style.borderLeftWidth = 0f;
        close.style.borderRightWidth = 0f;
        header.Add(close);
        return header;
    }

    private Button BuildOwnerButton(ECombatantId owner, string text)
    {
        var button = new Button(() =>
        {
            _selectedOwner = owner;
            _expandedSources.Clear();
            Rebuild();
        })
        {
            text = text,
        };
        button.style.height = 38f;
        button.style.minWidth = 88f;
        button.style.marginLeft = 8f;
        button.style.fontSize = 15f;
        button.style.color = Text;
        button.style.backgroundColor = SurfaceRaised;
        button.style.borderTopColor = Border;
        button.style.borderBottomColor = Border;
        button.style.borderLeftColor = Border;
        button.style.borderRightColor = Border;
        button.style.borderTopLeftRadius = 6f;
        button.style.borderTopRightRadius = 6f;
        button.style.borderBottomLeftRadius = 6f;
        button.style.borderBottomRightRadius = 6f;
        return button;
    }

    private void Rebuild()
    {
        if (_list == null)
            return;
        _list.Clear();
        KeywordIconSpriteProvider.BeginResolvePass();
        SetOwnerButtonState(_playerButton, _selectedOwner == ECombatantId.Player);
        SetOwnerButtonState(_opponentButton, _selectedOwner == ECombatantId.Opponent);

        var sources = _report
            .Sources.Where(source => source.Entity.Owner == _selectedOwner)
            .ToArray();
        if (sources.Length == 0)
        {
            var empty = Label(
                T("本方没有可归因的非零效果", "No attributable non-zero effects"),
                18f,
                Muted
            );
            empty.style.marginTop = 80f;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            _list.Add(empty);
            return;
        }

        for (var index = 0; index < sources.Length; index++)
            _list.Add(BuildSource(index + 1, sources[index]));
    }

    private VisualElement BuildSource(int rank, CombatImpactSource source)
    {
        var expanded = _expandedSources.Contains(source.Entity.Id);
        var section = new VisualElement();
        section.style.borderBottomWidth = 1f;
        section.style.borderBottomColor = Border;

        var row = new Button(() =>
        {
            if (!_expandedSources.Add(source.Entity.Id))
                _expandedSources.Remove(source.Entity.Id);
            Rebuild();
        });
        row.style.height = 96f;
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.paddingLeft = 12f;
        row.style.paddingRight = 12f;
        row.style.backgroundColor = Color.clear;
        row.style.borderTopWidth = 0f;
        row.style.borderBottomWidth = 0f;
        row.style.borderLeftWidth = 0f;
        row.style.borderRightWidth = 0f;

        var rankLabel = Label(rank.ToString(), 17f, Muted);
        rankLabel.style.width = 42f;
        rankLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        row.Add(rankLabel);
        row.Add(BuildEntityImage(source.Entity, 68f));

        var identity = new VisualElement();
        identity.style.width = 285f;
        identity.style.marginLeft = 18f;
        identity.Add(Label(source.Entity.Name, 20f, Text, FontStyle.Bold));
        identity.Add(
            Label(
                source.Entity.TypeLabel == "Skill" ? T("技能", "Skill") : T("物品", "Item"),
                14f,
                Muted
            )
        );
        var activityParts = new List<string>();
        if (source.UseCount > 0)
            activityParts.Add(T($"使用 {source.UseCount}", $"{source.UseCount} uses"));
        if (source.TriggerCount > 0)
            activityParts.Add(T($"触发 {source.TriggerCount}", $"{source.TriggerCount} triggers"));
        if (activityParts.Count > 0)
            identity.Add(Label(string.Join(" · ", activityParts), 12f, Muted));
        row.Add(identity);

        var summaries = new VisualElement();
        summaries.style.flexGrow = 1f;
        summaries.style.flexDirection = FlexDirection.Row;
        summaries.style.justifyContent = Justify.FlexEnd;
        foreach (var group in source.Groups.Take(3))
        {
            var summary = Label(
                $"{ResolveDisplay(group).Label} {FormatSummaryValue(group)}",
                15f,
                Muted
            );
            summary.style.marginLeft = 24f;
            summaries.Add(summary);
        }
        row.Add(summaries);

        var total = Label(
            T($"{source.TotalCount} 个效果", $"{source.TotalCount} effects"),
            18f,
            Text,
            FontStyle.Bold
        );
        total.style.minWidth = 105f;
        total.style.marginLeft = 28f;
        total.style.unityTextAlign = TextAnchor.MiddleRight;
        row.Add(total);

        var caret = Label(expanded ? "⌃" : "⌄", 19f, Muted);
        caret.style.width = 36f;
        caret.style.marginLeft = 10f;
        caret.style.unityTextAlign = TextAnchor.MiddleCenter;
        row.Add(caret);
        section.Add(row);

        if (expanded)
            section.Add(BuildDetails(source));
        return section;
    }

    private VisualElement BuildDetails(CombatImpactSource source)
    {
        var details = new VisualElement();
        details.style.flexDirection = FlexDirection.Row;
        details.style.flexWrap = Wrap.Wrap;
        details.style.paddingLeft = 130f;
        details.style.paddingRight = 44f;
        details.style.paddingBottom = 26f;

        foreach (var group in source.Groups)
            details.Add(BuildGroup(group));
        return details;
    }

    private VisualElement BuildGroup(CombatImpactGroup group)
    {
        var display = ResolveDisplay(group);
        var column = new VisualElement();
        column.style.width = new Length(48f, LengthUnit.Percent);
        column.style.marginRight = new Length(2f, LengthUnit.Percent);
        column.style.marginTop = 8f;
        column.style.marginBottom = 18f;

        var header = new VisualElement();
        header.style.height = 46f;
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.borderTopWidth = 1f;
        header.style.borderBottomWidth = 1f;
        header.style.borderTopColor = Border;
        header.style.borderBottomColor = Border;
        header.Add(BuildEffectIcon(display, 25f));

        var name = Label(display.Label, 17f, Text, FontStyle.Bold);
        name.style.marginLeft = 10f;
        name.style.flexGrow = 1f;
        header.Add(name);
        header.Add(Label(FormatGroupMetric(group), 16f, Text, FontStyle.Bold));
        column.Add(header);

        foreach (var target in group.Targets)
        {
            var targetRow = new VisualElement();
            targetRow.style.minHeight = 52f;
            targetRow.style.flexDirection = FlexDirection.Row;
            targetRow.style.alignItems = Align.Center;
            targetRow.style.borderBottomWidth = 1f;
            targetRow.style.borderBottomColor = new Color(Border.r, Border.g, Border.b, 0.55f);
            targetRow.Add(BuildEntityImage(target.Entity, 34f));

            var targetName = Label(target.Entity.Name, 16f, Text);
            targetName.style.marginLeft = 12f;
            targetName.style.flexGrow = 1f;
            targetRow.Add(targetName);
            targetRow.Add(Label(FormatTargetMetric(group.Kind, target), 15f, Muted));
            column.Add(targetRow);
        }
        return column;
    }

    private VisualElement BuildEffectIcon(NativeTagDisplay display, float size)
    {
        var image = new Image { scaleMode = ScaleMode.ScaleToFit };
        image.style.width = size;
        image.style.height = size;
        image.style.flexShrink = 0f;
        var outcome = KeywordIconSpriteProvider.Resolve(display.IconName);
        if (outcome.Sprite != null)
        {
            image.sprite = outcome.Sprite;
            if (display.AccentColor.HasValue)
                image.tintColor = display.AccentColor.Value;
        }
        return image;
    }

    private VisualElement BuildEntityImage(CombatImpactEntity entity, float size)
    {
        var image = new Image { scaleMode = ScaleMode.ScaleToFit };
        image.style.width = size;
        image.style.height = size;
        image.style.flexShrink = 0f;
        image.style.backgroundColor = new Color(0.12f, 0.11f, 0.10f, 1f);
        image.style.borderTopLeftRadius = 5f;
        image.style.borderTopRightRadius = 5f;
        image.style.borderBottomLeftRadius = 5f;
        image.style.borderBottomRightRadius = 5f;
        if (entity.Hero.HasValue)
            _ = LoadHeroPortrait(image, entity.Hero.Value);
        else
            _ = LoadArt(image, entity.ArtKey);
        return image;
    }

    private async Task LoadArt(Image image, string? artKey)
    {
        var art = await _artProvider.Get(artKey);
        if (image != null && art.HasValue)
        {
            image.image = art.Value.Texture;
            image.uv = art.Value.Uv;
        }
    }

    private static async Task LoadHeroPortrait(Image image, EHero hero)
    {
        var outcome = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero);
        if (image != null && outcome?.Sprite != null)
            image.sprite = outcome.Sprite;
    }

    private static NativeTagDisplay ResolveDisplay(CombatImpactGroup group)
    {
        var native = NativeTagTypography.Resolve(group.NativeAttributeKey);
        return group.Kind == CombatImpactKind.Critical
            ? new NativeTagDisplay(T("暴击", "Crit"), native.AccentColor, native.IconName)
            : native;
    }

    private static void SetOwnerButtonState(Button? button, bool selected)
    {
        if (button == null)
            return;
        button.style.color = selected ? Gold : Muted;
        button.style.borderBottomColor = selected ? Gold : Border;
        button.style.backgroundColor = selected
            ? new Color(0.17f, 0.14f, 0.08f, 1f)
            : SurfaceRaised;
    }

    private static string FormatSummaryValue(CombatImpactGroup group) =>
        group.AggregateValue.HasValue
            ? FormatValue(
                group.AggregateValue.Value,
                group.Unit,
                group.ValueIsPartial,
                group.Kind == CombatImpactKind.AttributeChange
            )
            : group.Count.ToString();

    private static string FormatGroupMetric(CombatImpactGroup group)
    {
        var count = T($"{group.Count} 次", $"{group.Count}×");
        return group.AggregateValue.HasValue
            ? $"{count} · {FormatValue(group.AggregateValue.Value, group.Unit, group.ValueIsPartial, group.Kind == CombatImpactKind.AttributeChange)}"
            : count;
    }

    private static string FormatTargetMetric(CombatImpactKind kind, CombatImpactTarget target)
    {
        var count = $"×{target.Count}";
        return target.AggregateValue.HasValue
            ? $"{count} · {FormatValue(target.AggregateValue.Value, target.Unit, target.ValueIsPartial, kind == CombatImpactKind.AttributeChange)}"
            : count;
    }

    private static string FormatValue(
        int value,
        CombatImpactValueUnit unit,
        bool partial,
        bool showSign = false
    )
    {
        var prefix = partial ? (showSign ? "≈" : "≥") : string.Empty;
        var sign = showSign && value >= 0 ? "+" : string.Empty;
        return unit switch
        {
            CombatImpactValueUnit.Milliseconds => Math.Abs(value) >= 1000
                ? $"{prefix}{sign}{value / 1000f:0.##}s"
                : $"{prefix}{sign}{value}ms",
            CombatImpactValueUnit.PercentagePoints =>
                $"{prefix}{(value >= 0 ? "+" : string.Empty)}{value}%",
            _ => $"{prefix}{sign}{value:N0}",
        };
    }

    private static Label Label(
        string text,
        float size,
        Color color,
        FontStyle fontStyle = FontStyle.Normal
    )
    {
        var label = new Label(text);
        label.style.fontSize = size;
        label.style.color = color;
        label.style.unityFontStyleAndWeight = fontStyle;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        return label;
    }

    private static string T(string chinese, string english) =>
        BazaarPlusPlus.Localization.L.CurrentLanguageCode.StartsWith(
            "zh",
            StringComparison.OrdinalIgnoreCase
        )
            ? chinese
            : english;

    public void Dispose()
    {
        _artProvider.Dispose();
        _typography?.Dispose();
        _typography = null;
        if (_rootObject != null)
            UnityEngine.Object.DestroyImmediate(_rootObject);
        if (_panelSettings != null)
            UnityEngine.Object.DestroyImmediate(_panelSettings);
        _rootObject = null;
        _panelSettings = null;
        _document = null;
        _root = null;
        _list = null;
    }
}
