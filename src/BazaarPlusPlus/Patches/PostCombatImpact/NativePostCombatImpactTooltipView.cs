#nullable enable
#pragma warning disable CS0436
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Patches.Tooltips;
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
    private const string SectionKey = "post-combat-impact";
    private const float TargetIconSize = 34f;
    private static readonly BppTooltipSections.Style SectionStyle = new()
    {
        FontScale = 0.82f,
        SectionTopPaddingScale = 0.7f,
        SectionBottomPaddingScale = 1.15f,
        ShowNativeDivider = true,
        DividerHorizontalInset = 12f,
    };

    private readonly PostCombatImpactCardArtProvider _artProvider = new();
    private CardTooltipController? _activeController;
    private string? _activeSourceId;
    private int _renderGeneration;

    public bool Show(CardTooltipController controller, string sourceId, CombatImpactSource? source)
    {
        if (
            ReferenceEquals(_activeController, controller)
            && string.Equals(_activeSourceId, sourceId, StringComparison.Ordinal)
        )
            return true;

        Hide();
        var generation = ++_renderGeneration;
        var shown = BppTooltipSections.TryShowCustom(
            controller,
            SectionKey,
            controller.passiveEffectParent,
            section => Build(section, source, generation),
            SectionStyle
        );
        if (!shown)
            return false;

        _activeController = controller;
        _activeSourceId = sourceId;

        // Native Lock() rejects recap cards because their original CardController is
        // deliberately hidden. The flag is the part RecapItemVisualController reads to
        // keep this already-positioned native tooltip alive after pointer exit.
        TempoUIUtility.ForceRebuildRecursive(controller.TooltipRectTransform);
        controller.KeepTooltipWithinBounds();
        controller.SetLockedFlag(true);
        return true;
    }

    public void Hide()
    {
        _renderGeneration++;
        if (_activeController != null)
        {
            _activeController.SetLockedFlag(false);
            BppTooltipSections.Hide(_activeController, SectionKey);
        }
        _activeController = null;
        _activeSourceId = null;
    }

    public bool OnNativeTooltipChanging(CardTooltipController controller)
    {
        if (!ReferenceEquals(_activeController, controller))
            return false;
        Hide();
        return true;
    }

    private void Build(
        BppTooltipSections.Section section,
        CombatImpactSource? source,
        int generation
    )
    {
        var root = CreateVertical("BppPostCombatImpactContent", section.Block.transform, 6f);
        section.CustomContent = root.gameObject;

        var title = CloneText(
            section.Text.textObject,
            root,
            T("本场影响", "Combat impact"),
            FontStyles.Bold,
            1.08f
        );
        title.alignment = TextAlignmentOptions.Left;

        if (source == null)
        {
            var empty = CloneText(
                section.Text.textObject,
                root,
                T("本场未记录到可归因的影响", "No attributable impact recorded this combat"),
                FontStyles.Normal,
                0.84f
            );
            empty.alpha = 0.72f;
            return;
        }

        var summaryParts = new List<string>();
        if (source.UseCount > 0)
            summaryParts.Add(T($"使用 {source.UseCount}", $"{source.UseCount} uses"));
        if (source.TriggerCount > 0)
            summaryParts.Add(T($"触发 {source.TriggerCount}", $"{source.TriggerCount} triggers"));
        summaryParts.Add(T($"{source.TotalCount} 个效果", $"{source.TotalCount} effects"));
        var summary = CloneText(
            section.Text.textObject,
            root,
            string.Join(" · ", summaryParts),
            FontStyles.Normal,
            0.82f
        );
        summary.alpha = 0.72f;

        foreach (var group in source.Groups)
            BuildGroup(section.Text.textObject, root, group, generation);
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
        var effectText =
            Data.TooltipTypography?.GetKeywordStringWithIconNoScale(
                iconKey,
                label,
                useNumberFont: false
            ) ?? label;
        CloneText(textTemplate, header, effectText, FontStyles.Bold, 0.96f, flexibleWidth: 1f);
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
        BuildTargetIcon(row, target.Entity, generation);
        CloneText(
            textTemplate,
            row,
            target.Entity.Name,
            FontStyles.Normal,
            0.88f,
            flexibleWidth: 1f
        );
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

    private void BuildTargetIcon(RectTransform parent, CombatImpactEntity entity, int generation)
    {
        var iconObject = new GameObject(
            "ImpactTargetIcon",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(LayoutElement)
        );
        var rect = (RectTransform)iconObject.transform;
        rect.SetParent(parent, worldPositionStays: false);
        AddLayout(iconObject, preferredHeight: TargetIconSize, preferredWidth: TargetIconSize);

        if (entity.Hero.HasValue)
        {
            var image = iconObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            _ = LoadHero(image, entity.Hero.Value, generation);
            return;
        }

        var rawImage = iconObject.AddComponent<RawImage>();
        rawImage.raycastTarget = false;
        _ = LoadCardArt(rawImage, entity, generation);
    }

    private async Task LoadCardArt(RawImage image, CombatImpactEntity entity, int generation)
    {
        var art = await _artProvider.Get(entity);
        if (generation != _renderGeneration || image == null || !art.HasValue)
            return;

        image.texture = art.Value.Texture;
        image.uvRect = art.Value.Uv;
    }

    private async Task LoadHero(Image image, EHero hero, int generation)
    {
        try
        {
            var outcome = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero);
            if (generation == _renderGeneration && image != null && outcome?.Sprite != null)
                image.sprite = outcome.Sprite;
        }
        catch
        {
            // Target art is optional; the native tooltip remains usable without it.
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
