#nullable enable
#pragma warning disable CS0436
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Localization;
using TheBazaar;
using TheBazaar.UI.Tooltips;
using TheBazaar.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Patches.PostCombatImpact;

internal sealed class NativePostCombatImpactTooltipView : IPostCombatImpactTooltipView
{
    private const float TargetIconSize = 34f;
    private const float SourceIconSize = 48f;
    private const float TooltipMinWidth = 620f;
    private const float TooltipPreferredWidth = 660f;
    private const float TooltipGap = 18f;

    private static readonly List<UIPositioner.Side> PreferredSides =
    [
        UIPositioner.Side.Right,
        UIPositioner.Side.Left,
        UIPositioner.Side.Bottom,
        UIPositioner.Side.Top,
    ];

    private static readonly List<UIPositioner.Side> AllowedOverflowSides =
    [
        UIPositioner.Side.Right,
        UIPositioner.Side.Left,
    ];

    private readonly PostCombatImpactCardArtProvider _artProvider = new();
    private AuxiliaryTooltipController? _activeAuxiliary;
    private CardTooltipController? _activePrimary;
    private GameObject? _contentRoot;
    private TMP_Text? _nativeBodyText;
    private bool _nativeBodyWasActive;
    private RectTransform? _nativeAuxParentRect;
    private float _nativeAuxiliaryWidth;
    private float _nativeAuxiliaryHeight;
    private float _nativeAuxParentWidth;
    private bool _nativeSizeCaptured;
    private int _renderGeneration;

    public string Header => T("本场影响", "Combat impact");

    public bool Show(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary,
        CombatImpactSource? source
    )
    {
        if (auxiliary.auxParent == null || auxiliary.bodyText == null)
            return false;

        CleanupCustomContent();
        var generation = ++_renderGeneration;
        _activeAuxiliary = auxiliary;
        _activePrimary = primary;
        _nativeBodyText = auxiliary.bodyText;
        _nativeBodyWasActive = _nativeBodyText.gameObject.activeSelf;
        _nativeBodyText.gameObject.SetActive(false);
        CaptureAndApplyNativeSize(auxiliary);

        var root = CreateVertical("BppPostCombatImpactContent", auxiliary.auxParent.transform, 8f);
        _contentRoot = root.gameObject;
        var background = root.gameObject.AddComponent<Image>();
        background.color = new Color32(25, 17, 10, 248);
        background.raycastTarget = false;
        AddLayout(
            root.gameObject,
            preferredHeight: -1f,
            preferredWidth: TooltipPreferredWidth,
            minWidth: TooltipMinWidth
        );
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, TooltipPreferredWidth);
        Build(_nativeBodyText, root, source, generation);

        primary.SetLockedFlag(true);
        Canvas.ForceUpdateCanvases();
        TempoUIUtility.ForceRebuildRecursive(auxiliary.PositioningRectTransform);
        return true;
    }

    public bool Position(AuxiliaryTooltipController auxiliary, CardTooltipController primary)
    {
        if (
            !ReferenceEquals(_activeAuxiliary, auxiliary)
            || !ReferenceEquals(_activePrimary, primary)
            || _contentRoot == null
            || primary.RootCanvasComponent == null
        )
            return false;

        Canvas.ForceUpdateCanvases();
        TempoUIUtility.ForceRebuildRecursive(primary.PositioningRectTransform);
        TempoUIUtility.ForceRebuildRecursive(auxiliary.PositioningRectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(primary.PositioningRectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(auxiliary.PositioningRectTransform);
        ApplyNativeHeight(auxiliary);
        var side = UIPositioner.PositionRectRelativeToAnother(
            auxiliary.PositioningRectTransform,
            primary.PositioningRectTransform,
            primary.RootCanvasComponent,
            new UIPositioner.Margin(TooltipGap),
            PreferredSides,
            AllowedOverflowSides
        );
        if (side == UIPositioner.Side.None)
            return false;

        auxiliary.KeepTooltipWithinBounds();
        return true;
    }

    public void Hide()
    {
        if (!CleanupCustomContent())
            return;

        Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
    }

    public bool OnNativeTooltipChanging(CardTooltipController controller)
    {
        if (!ReferenceEquals(_activePrimary, controller))
            return false;

        Hide();
        return true;
    }

    public bool OnNativeAuxiliaryTooltipChanging(AuxiliaryTooltipController controller)
    {
        if (!ReferenceEquals(_activeAuxiliary, controller))
            return false;

        // The native caller owns the auxiliary host now. Remove our content and unlock the
        // recap tooltip without completing the host's node sequence underneath that caller.
        CleanupCustomContent();
        return true;
    }

    private bool CleanupCustomContent()
    {
        var hadActiveContent = _activeAuxiliary != null || _contentRoot != null;
        if (!hadActiveContent)
            return false;

        _renderGeneration++;
        if (_activePrimary != null)
            _activePrimary.SetLockedFlag(false);
        if (_contentRoot != null)
        {
            _contentRoot.SetActive(false);
            Object.Destroy(_contentRoot);
        }
        if (_nativeBodyText != null)
            _nativeBodyText.gameObject.SetActive(_nativeBodyWasActive);
        RestoreNativeSize();

        _activeAuxiliary = null;
        _activePrimary = null;
        _contentRoot = null;
        _nativeBodyText = null;
        _nativeBodyWasActive = false;
        return true;
    }

    private void CaptureAndApplyNativeSize(AuxiliaryTooltipController auxiliary)
    {
        _nativeAuxParentRect = auxiliary.auxParent.transform as RectTransform;
        _nativeAuxiliaryWidth = auxiliary.PositioningRectTransform.rect.width;
        _nativeAuxiliaryHeight = auxiliary.PositioningRectTransform.rect.height;
        _nativeAuxParentWidth = _nativeAuxParentRect?.rect.width ?? 0f;
        _nativeSizeCaptured = true;

        var layout = auxiliary.auxParent.GetComponent<VerticalLayoutGroup>();
        var horizontalPadding = layout?.padding.horizontal ?? 0;
        var frameWidth = TooltipPreferredWidth + horizontalPadding;
        auxiliary.PositioningRectTransform.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            frameWidth
        );
        _nativeAuxParentRect?.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, frameWidth);
    }

    private void ApplyNativeHeight(AuxiliaryTooltipController auxiliary)
    {
        var contentHeight = _nativeAuxParentRect?.rect.height ?? 0f;
        if (contentHeight <= 0f)
            return;

        auxiliary.PositioningRectTransform.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            contentHeight
        );
    }

    private void RestoreNativeSize()
    {
        if (!_nativeSizeCaptured)
            return;

        if (_activeAuxiliary != null && _nativeAuxiliaryWidth > 0f)
        {
            _activeAuxiliary.PositioningRectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                _nativeAuxiliaryWidth
            );
            if (_nativeAuxiliaryHeight > 0f)
            {
                _activeAuxiliary.PositioningRectTransform.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    _nativeAuxiliaryHeight
                );
            }
        }
        if (_nativeAuxParentRect != null && _nativeAuxParentWidth > 0f)
        {
            _nativeAuxParentRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                _nativeAuxParentWidth
            );
        }

        _nativeAuxParentRect = null;
        _nativeAuxiliaryWidth = 0f;
        _nativeAuxiliaryHeight = 0f;
        _nativeAuxParentWidth = 0f;
        _nativeSizeCaptured = false;
    }

    private void Build(
        TMP_Text textTemplate,
        RectTransform root,
        CombatImpactSource? source,
        int generation
    )
    {
        if (source == null)
        {
            var empty = CloneText(
                textTemplate,
                root,
                T("本场未记录到可归因的影响", "No attributable impact recorded this combat"),
                FontStyles.Normal,
                0.9f
            );
            empty.alpha = 0.72f;
            return;
        }

        BuildSourceSummary(textTemplate, root, source, generation);
        foreach (var group in source.Groups)
            BuildGroup(textTemplate, root, group, generation);
    }

    private void BuildSourceSummary(
        TMP_Text textTemplate,
        RectTransform parent,
        CombatImpactSource source,
        int generation
    )
    {
        var row = CreateHorizontal(
            "ImpactSourceSummary",
            parent,
            10f,
            preferredHeight: SourceIconSize + 4f
        );
        BuildEntityIcon(row, source.Entity, SourceIconSize, generation);

        var textColumn = CreateVertical("ImpactSourceText", row, 0f);
        AddLayout(textColumn.gameObject, preferredHeight: -1f, flexibleWidth: 1f);
        CloneText(
            textTemplate,
            textColumn,
            source.Entity.Name,
            FontStyles.Bold,
            1f,
            flexibleWidth: 1f
        );

        var summaryParts = new List<string>();
        if (source.UseCount > 0)
            summaryParts.Add(T($"使用 {source.UseCount}", $"{source.UseCount} uses"));
        if (source.TriggerCount > 0)
            summaryParts.Add(T($"触发 {source.TriggerCount}", $"{source.TriggerCount} triggers"));
        summaryParts.Add(T($"{source.TotalCount} 个效果", $"{source.TotalCount} effects"));
        var summary = CloneText(
            textTemplate,
            textColumn,
            string.Join(" · ", summaryParts),
            FontStyles.Normal,
            0.82f,
            flexibleWidth: 1f
        );
        summary.alpha = 0.72f;
    }

    private void BuildGroup(
        TMP_Text textTemplate,
        RectTransform parent,
        CombatImpactGroup group,
        int generation
    )
    {
        var groupRoot = CreateVertical("ImpactGroup", parent, 2f);
        AddLayout(groupRoot.gameObject, preferredHeight: -1f, flexibleWidth: 1f);

        var header = CreateHorizontal("ImpactGroupHeader", groupRoot, 8f, preferredHeight: 34f);
        var (label, iconKey) = ResolveEffect(group);
        var effectIcon =
            Data.TooltipTypography?.GetKeywordStringWithIconNoScale(
                iconKey,
                string.Empty,
                useNumberFont: false
            ) ?? string.Empty;
        var icon = CloneText(
            textTemplate,
            header,
            effectIcon,
            FontStyles.Normal,
            0.96f,
            minWidth: 34f
        );
        icon.alignment = TextAlignmentOptions.Center;
        var labelColumn = CreateVertical("ImpactGroupLabel", header, 0f);
        AddLayout(labelColumn.gameObject, preferredHeight: -1f, flexibleWidth: 1f, minWidth: 220f);
        CloneText(textTemplate, labelColumn, label, FontStyles.Bold, 0.96f);
        var metric = CloneText(
            textTemplate,
            header,
            CombatImpactMetricFormatter.Group(group, IsChinese()),
            FontStyles.Bold,
            0.9f,
            minWidth: 112f
        );
        metric.alignment = TextAlignmentOptions.Right;

        foreach (var target in group.Targets)
            BuildTarget(textTemplate, groupRoot, group.Kind, target, generation);
    }

    private void BuildTarget(
        TMP_Text textTemplate,
        RectTransform parent,
        CombatImpactKind kind,
        CombatImpactTarget target,
        int generation
    )
    {
        var row = CreateHorizontal(
            "ImpactTargetRow",
            parent,
            9f,
            preferredHeight: TargetIconSize + 4f
        );
        BuildEntityIcon(row, target.Entity, TargetIconSize, generation);
        var nameColumn = CreateVertical("ImpactTargetName", row, 0f);
        AddLayout(nameColumn.gameObject, preferredHeight: -1f, flexibleWidth: 1f, minWidth: 220f);
        CloneText(textTemplate, nameColumn, target.Entity.Name, FontStyles.Normal, 0.88f);
        var metric = CloneText(
            textTemplate,
            row,
            CombatImpactMetricFormatter.Target(kind, target),
            FontStyles.Normal,
            0.84f,
            minWidth: 120f
        );
        metric.alignment = TextAlignmentOptions.Right;
        metric.alpha = 0.76f;
    }

    private void BuildEntityIcon(
        RectTransform parent,
        CombatImpactEntity entity,
        float size,
        int generation
    )
    {
        var iconObject = new GameObject(
            "ImpactEntityIcon",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(LayoutElement)
        );
        var rect = (RectTransform)iconObject.transform;
        rect.SetParent(parent, worldPositionStays: false);
        AddLayout(iconObject, preferredHeight: size, preferredWidth: size);

        if (entity.Hero.HasValue)
        {
            var image = iconObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false;
            _ = LoadHero(image, entity.Hero.Value, generation);
            return;
        }

        var rawImage = iconObject.AddComponent<RawImage>();
        rawImage.raycastTarget = false;
        rawImage.enabled = false;
        _ = LoadCardArt(rawImage, entity, generation);
    }

    private async Task LoadCardArt(RawImage image, CombatImpactEntity entity, int generation)
    {
        var art = await _artProvider.Get(entity);
        if (generation != _renderGeneration || image == null || !art.HasValue)
            return;

        image.texture = art.Value.Texture;
        image.uvRect = art.Value.Uv;
        image.enabled = true;
    }

    private async Task LoadHero(Image image, EHero hero, int generation)
    {
        try
        {
            var outcome = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero);
            if (generation == _renderGeneration && image != null && outcome?.Sprite != null)
            {
                image.sprite = outcome.Sprite;
                image.enabled = true;
            }
        }
        catch
        {
            // Entity art is optional; the native tooltip remains usable without it.
        }
    }

    private static TMP_Text CloneText(
        TMP_Text template,
        Transform parent,
        string content,
        FontStyles fontStyle,
        float sizeScale,
        float flexibleWidth = 0f,
        float minWidth = -1f
    )
    {
        var text = Object.Instantiate(template, parent, worldPositionStays: false);
        text.name = "BppPostCombatImpactText";
        text.gameObject.SetActive(true);
        text.fontStyle = fontStyle;
        text.fontSize *= sizeScale;
        text.color = new Color32(248, 238, 213, 255);
        text.alpha = 1f;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        NativeGameTypography.EnsureNativeTextCoverage(text, content);
        text.text = content;
        AddLayout(text.gameObject, preferredHeight: -1f, flexibleWidth, minWidth: minWidth);
        return text;
    }

    private static RectTransform CreateVertical(string name, Transform parent, float spacing)
    {
        var gameObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)
        );
        var rect = (RectTransform)gameObject.transform;
        rect.SetParent(parent, worldPositionStays: false);
        var layout = gameObject.GetComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        var fitter = gameObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rect;
    }

    private static RectTransform CreateHorizontal(
        string name,
        Transform parent,
        float spacing,
        float preferredHeight
    )
    {
        var gameObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup),
            typeof(LayoutElement)
        );
        var rect = (RectTransform)gameObject.transform;
        rect.SetParent(parent, worldPositionStays: false);
        var layout = gameObject.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        AddLayout(gameObject, preferredHeight, flexibleWidth: 1f);
        return rect;
    }

    private static LayoutElement AddLayout(
        GameObject gameObject,
        float preferredHeight,
        float flexibleWidth = 0f,
        float preferredWidth = -1f,
        float minWidth = -1f
    )
    {
        var layout =
            gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = false;
        layout.preferredHeight = preferredHeight;
        layout.preferredWidth = preferredWidth;
        layout.minWidth = minWidth;
        layout.flexibleWidth = flexibleWidth;
        return layout;
    }

    private static (string Label, string IconKey) ResolveEffect(CombatImpactGroup group)
    {
        var iconKey = group.Kind switch
        {
            CombatImpactKind.Destroy => "Destroy",
            CombatImpactKind.Critical => "CritChance",
            CombatImpactKind.AttributeChange => AttributeIconKey(group.NativeAttributeKey),
            _ => group.NativeAttributeKey,
        };
        var display = NativeTagTypography.Resolve(
            group.Kind == CombatImpactKind.Destroy ? "Destroy" : group.NativeAttributeKey
        );
        var label = group.Kind switch
        {
            CombatImpactKind.Critical => T("暴击", "Crit"),
            CombatImpactKind.Destroy => T("摧毁", "Destroy"),
            _ => display.Label,
        };
        return (label, iconKey);
    }

    private static string AttributeIconKey(string key) =>
        key switch
        {
            "Health" or "HealthMax" or "HealAmount" => "HealAmount",
            "HealthRegen" => "RegenApplyAmount",
            "Rage" or "RageMax" => "RageApplyAmount",
            "Burn" => "BurnApplyAmount",
            "Poison" => "PoisonApplyAmount",
            "Shield" => "ShieldApplyAmount",
            "DamageCrit" => "CritChance",
            _ => key,
        };

    private static bool IsChinese() =>
        L.CurrentLanguageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static string T(string chinese, string english) => IsChinese() ? chinese : english;

    public void Dispose()
    {
        Hide();
        _artProvider.Dispose();
    }
}
