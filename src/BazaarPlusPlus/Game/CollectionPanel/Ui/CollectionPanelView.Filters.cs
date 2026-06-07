#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.EncounterPortraits;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed partial class CollectionPanelView
{
    private void EnsureHeroChips(IReadOnlyList<EHero> heroes)
    {
        if (_heroChipRow == null)
            return;
        if (HeroChipsMatch(heroes))
            return;
        ClearHeroChipRow();
        foreach (var hero in heroes)
        {
            var chip = CreateHeroChipButton(hero, () => _toggleHero(hero));
            _heroChips[hero] = chip;
            _heroChipRow.Add(chip);
        }
    }

    private void EnsureTierChips(IReadOnlyList<ETier> tiers)
    {
        if (_tierChipRow == null)
            return;
        if (TierChipsMatch(tiers))
            return;
        ClearChipRow(_tierChips, _tierChipRow, keepFirst: false);
        var index = 0;
        foreach (var tier in tiers)
        {
            var chip = CreateChipButton(
                CollectionPanelText.Tier(tier),
                () => _toggleTier(tier),
                true
            );
            chip.style.marginLeft = index > 0 ? UiSpacing.Sm : 0f;
            _tierChips[tier] = chip;
            _tierChipRow.Add(chip);
            index++;
        }
    }

    private bool HeroChipsMatch(IReadOnlyList<EHero> heroes)
    {
        if (heroes.Count != _heroChips.Count)
            return false;
        foreach (var hero in heroes)
        {
            if (!_heroChips.ContainsKey(hero))
                return false;
        }
        return true;
    }

    private bool TierChipsMatch(IReadOnlyList<ETier> tiers)
    {
        if (tiers.Count != _tierChips.Count)
            return false;
        foreach (var tier in tiers)
        {
            if (!_tierChips.ContainsKey(tier))
                return false;
        }
        return true;
    }

    private void EnsureSizeChips(IReadOnlyList<ECardSize> sizes)
    {
        if (_sizeChipRow == null)
            return;
        if (SizeChipsMatch(sizes))
            return;
        ClearChipRow(_sizeChips, _sizeChipRow, keepFirst: false);
        var index = 0;
        foreach (var size in sizes)
        {
            var chip = CreateChipButton(
                CollectionPanelText.Size(size),
                () => _toggleSize(size),
                true
            );
            chip.style.marginLeft = index > 0 ? UiSpacing.Sm : 0f;
            _sizeChips[size] = chip;
            _sizeChipRow.Add(chip);
            index++;
        }
    }

    private bool SizeChipsMatch(IReadOnlyList<ECardSize> sizes)
    {
        if (sizes.Count != _sizeChips.Count)
            return false;
        foreach (var size in sizes)
        {
            if (!_sizeChips.ContainsKey(size))
                return false;
        }
        return true;
    }

    private void EnsureTagChips(IReadOnlyList<ECardTag> tags, HashSet<ECardTag> selectedTags)
    {
        if (_tagChipRow == null)
            return;
        var visible = VisibleTagOptions(tags, selectedTags);
        if (!TagChipsMatch(visible))
        {
            ClearTagChipRow();
            foreach (var tag in visible)
            {
                var captured = tag;
                var chip = CreateCompactChipButton(
                    NativeTagTypography.Resolve(captured).Label,
                    () => _toggleTag(captured)
                );
                _tagChips[captured] = chip;
                _tagChipOrder.Add(captured);
                _tagChipRow.Add(chip);
            }

            _tagMoreButton = CreateCompactChipButton(string.Empty, ToggleTagRowExpanded);
            _tagChipRow.Add(_tagMoreButton);
        }

        RefreshTagMoreButton(tags.Count);
    }

    // Collapsed: the whitelist's primary slice plus any selected tag that would otherwise be
    // hidden (a selection must never be invisible). Expanded: every option.
    private List<ECardTag> VisibleTagOptions(
        IReadOnlyList<ECardTag> tags,
        HashSet<ECardTag> selectedTags
    )
    {
        var visible = new List<ECardTag>(tags.Count);
        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            if (
                _tagRowExpanded
                || i < CollectionTagWhitelist.PrimaryCount
                || selectedTags.Contains(tag)
            )
                visible.Add(tag);
        }
        return visible;
    }

    private bool TagChipsMatch(List<ECardTag> visible)
    {
        if (visible.Count != _tagChipOrder.Count)
            return false;
        for (var i = 0; i < visible.Count; i++)
            if (visible[i] != _tagChipOrder[i])
                return false;
        return true;
    }

    private void ToggleTagRowExpanded()
    {
        _tagRowExpanded = !_tagRowExpanded;
        EnsureTagChips(_lastTagOptions, _lastSelectedTags);
        foreach (var pair in _tagChips)
            RefreshChip(
                pair.Value,
                _lastSelectedTags.Contains(pair.Key),
                NativeTagTypography.Resolve(pair.Key).AccentColor
            );
    }

    private void RefreshTagMoreButton(int totalOptionCount)
    {
        if (_tagMoreButton == null)
            return;
        var hiddenCount = totalOptionCount - _tagChipOrder.Count;
        _tagMoreButton.style.display =
            _tagRowExpanded || hiddenCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        _tagMoreButton.text = _tagRowExpanded
            ? CollectionPanelText.TagLess()
            : CollectionPanelText.TagMore(hiddenCount);
        StyleButton(_tagMoreButton, Colors.HistoryButtonBackground, Colors.HistorySubtitleText);
    }

    private void ClearTagChipRow()
    {
        foreach (var button in _tagChips.Values)
        {
            if (button.parent != null)
                button.parent.Remove(button);
        }
        _tagChips.Clear();
        _tagChipOrder.Clear();
        _tagMoreButton = null;
        _tagChipRow?.Clear();
    }

    private void EnsureSourceChips(IReadOnlyList<CollectionSourceOptionViewModel> sources)
    {
        if (_sourceChipRow == null)
            return;
        if (SourceChipsMatch(sources))
            return;
        ClearSourceChipRow();
        foreach (var source in sources)
        {
            var chip = CreateSourceChipButton(source, () => _toggleSource(source.SourceKey));
            _sourceChips[source.SourceKey] = chip;
            _sourceChipOrder.Add(source.SourceKey);
            _sourceChipRow.Add(chip);
            if (source.BreakAfter)
                _sourceChipRow.Add(CreateSourceChipBreak());
        }
    }

    private bool SourceChipsMatch(IReadOnlyList<CollectionSourceOptionViewModel> sources)
    {
        if (sources.Count != _sourceChips.Count)
            return false;
        if (_sourceChipOrder.Count != sources.Count)
            return false;
        for (var i = 0; i < sources.Count; i++)
            if (!string.Equals(_sourceChipOrder[i], sources[i].SourceKey, StringComparison.Ordinal))
                return false;
        return true;
    }

    private void ClearHeroChipRow()
    {
        foreach (var button in _heroChips.Values)
        {
            if (button.parent != null)
                button.parent.Remove(button);
        }

        _heroChips.Clear();
        _heroChipIcons.Clear();
    }

    private void ClearSourceChipRow()
    {
        foreach (var button in _sourceChips.Values)
        {
            if (button.parent != null)
                button.parent.Remove(button);
        }

        _sourceChips.Clear();
        _sourceChipIcons.Clear();
        _sourceChipOrder.Clear();
        _sourceChipRow?.Clear();
    }

    private static VisualElement CreateSourceChipBreak()
    {
        var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
        spacer.style.flexBasis = Length.Percent(100);
        spacer.style.flexGrow = 0f;
        spacer.style.flexShrink = 0f;
        spacer.style.height = 1f;
        return spacer;
    }

    private void OnSourceChipRowGeometryChanged(GeometryChangedEvent evt) =>
        ApplySourceChipSizing(evt.newRect.width);

    private void ApplySourceChipSizing(float rowWidth)
    {
        if (float.IsNaN(rowWidth) || rowWidth <= 0f)
            return;

        var gap = UiSpacing.Sm;
        var box = Mathf.Max(Sizes.SourceChipMinSize, Mathf.Floor(rowWidth / 6f - gap));
        if (Mathf.Abs(box - _appliedSourceChipBox) < 0.5f)
            return;

        _appliedSourceChipBox = box;
        var icon = Mathf.Round(box * Sizes.SourceChipIconRatio);
        foreach (var pair in _sourceChips)
        {
            if (_sourceChipIcons.TryGetValue(pair.Key, out var iconElement))
                ResizeSourceChip(pair.Value, iconElement, box, icon);
        }
    }

    private static void ClearChipRow<T>(
        Dictionary<T, Button> chips,
        VisualElement row,
        bool keepFirst
    )
    {
        foreach (var button in chips.Values)
        {
            if (button.parent != null)
                button.parent.Remove(button);
        }
        chips.Clear();
        if (!keepFirst && row.childCount > 0)
            row.Clear();
    }

    private Button CreateChipButton(string text, Action onClick, bool fillRow = false)
    {
        var chip = CreateButton(
            text,
            onClick,
            fillRow ? 0f : Sizes.ChipMinWidth + 12f,
            Sizes.ChipHeight,
            fixedWidth: !fillRow
        );
        if (fillRow)
        {
            chip.style.flexBasis = 0f;
            chip.style.flexGrow = 1f;
            chip.style.flexShrink = 1f;
            chip.style.minWidth = 0f;
        }
        chip.style.marginRight = fillRow ? 0f : UiSpacing.Sm;
        chip.style.marginBottom = UiSpacing.Xs;
        StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);
        return chip;
    }

    // Compact auto-width chip for many-valued wrap rows (tags): text-sized instead of the
    // fixed-width tier/size chip so two dozen options fit in a couple of wrapped lines.
    private static Button CreateCompactChipButton(string text, Action onClick)
    {
        var chip = CreateButton(text, onClick, 0f, Sizes.InfoChipHeight, fixedWidth: false);
        chip.style.minWidth = Sizes.InfoChipMinWidth;
        chip.style.fontSize = Sizes.FontSmall;
        UiStyle.HorizontalPadding(chip.style, UiSpacing.Md);
        chip.style.marginRight = UiSpacing.Sm;
        chip.style.marginBottom = UiSpacing.Xs;
        StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);
        return chip;
    }

    private Button CreateHeroChipButton(EHero hero, Action onClick)
    {
        var labelText = CollectionPanelText.Hero(hero);
        var chip = CreateButton(
            string.Empty,
            onClick,
            Sizes.HeroChipButtonSize,
            Sizes.HeroChipButtonSize
        );
        chip.tooltip = labelText;
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.justifyContent = Justify.Center;
        chip.style.alignItems = Align.Center;
        chip.style.marginBottom = UiSpacing.Xs;
        StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);

        var icon = CreateHeroChipIcon(hero);
        chip.Add(icon);

        _heroChipIcons[hero] = icon;
        if (HeroPortraitSpriteProvider.IsRenderableHero(hero))
            LoadHeroChipIcon(hero, icon);
        return chip;
    }

    private Button CreateSourceChipButton(CollectionSourceOptionViewModel source, Action onClick)
    {
        var chip = CreateButton(
            string.Empty,
            onClick,
            CurrentSourceChipBox(),
            CurrentSourceChipBox(),
            fixedWidth: false
        );
        chip.tooltip = string.IsNullOrWhiteSpace(source.Description)
            ? source.DisplayName
            : $"{source.DisplayName} - {source.Description}";
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.justifyContent = Justify.Center;
        chip.style.alignItems = Align.Center;
        chip.style.marginRight = UiSpacing.Sm;
        chip.style.marginBottom = UiSpacing.Xs;
        StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);

        var icon = CreateSourceChipIcon(source.DisplayName);
        chip.Add(icon);
        var box = CurrentSourceChipBox();
        ResizeSourceChip(chip, icon, box, Mathf.Round(box * Sizes.SourceChipIconRatio));

        _sourceChipIcons[source.SourceKey] = icon;
        LoadSourceChipIcon(source.SourceKey, source.RepresentativeTemplateId, icon);
        return chip;
    }

    private static VisualElement CreateHeroChipIcon(EHero hero)
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        UiStyle.FixedSize(icon.style, Sizes.HeroChipIconSize, Sizes.HeroChipIconSize);
        icon.style.position = Position.Relative;
        icon.style.backgroundColor = Colors.HistoryStatusBackground;
        icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
        icon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        UiStyle.Border(icon.style, Borders.Thin, Colors.HistoryButtonBorder);
        UiStyle.Radius(icon.style, Sizes.HeroChipIconSize / 2f);

        if (!HeroPortraitSpriteProvider.IsRenderableHero(hero))
            AddCommonHeroGlyph(icon);

        return icon;
    }

    private static VisualElement CreateSourceChipIcon(string displayName)
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.style.position = Position.Relative;
        icon.style.backgroundColor = Colors.HistoryStatusBackground;
        icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
        icon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        UiStyle.Border(icon.style, Borders.Thin, Colors.HistoryButtonBorder);
        ResizeSourceIcon(icon, Mathf.Round(Sizes.SourceChipMinSize * Sizes.SourceChipIconRatio));

        var initials = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistoryChipText);
        initials.name = SourceChipInitialsName;
        initials.text = GetInitials(displayName);
        initials.pickingMode = PickingMode.Ignore;
        initials.style.position = Position.Absolute;
        initials.style.left = 0f;
        initials.style.right = 0f;
        initials.style.top = 0f;
        initials.style.bottom = 0f;
        initials.style.unityTextAlign = TextAnchor.MiddleCenter;
        icon.Add(initials);
        return icon;
    }

    private float CurrentSourceChipBox() =>
        _appliedSourceChipBox > 0f ? _appliedSourceChipBox : Sizes.SourceChipMinSize;

    private static void ResizeSourceChip(Button chip, VisualElement icon, float box, float iconSize)
    {
        chip.style.width = box;
        chip.style.minWidth = box;
        chip.style.maxWidth = box;
        chip.style.height = box;
        chip.style.minHeight = box;
        chip.style.maxHeight = box;
        ResizeSourceIcon(icon, iconSize);
    }

    private static void ResizeSourceIcon(VisualElement icon, float iconSize)
    {
        icon.style.width = iconSize;
        icon.style.minWidth = iconSize;
        icon.style.maxWidth = iconSize;
        icon.style.height = iconSize;
        icon.style.minHeight = iconSize;
        icon.style.maxHeight = iconSize;
        UiStyle.Radius(icon.style, iconSize / 2f);
    }

    private static void AddCommonHeroGlyph(VisualElement icon)
    {
        AddCommonHeroDot(icon, 21f, 21f, 6f, Colors.HistoryChipText);
        AddCommonHeroDot(icon, 9f, 9f, 5f, Colors.HistorySubtitleText);
        AddCommonHeroDot(icon, 34f, 9f, 5f, Colors.HistorySubtitleText);
        AddCommonHeroDot(icon, 9f, 34f, 5f, Colors.HistorySubtitleText);
        AddCommonHeroDot(icon, 34f, 34f, 5f, Colors.HistorySubtitleText);
    }

    private static void AddCommonHeroDot(
        VisualElement parent,
        float left,
        float top,
        float size,
        Color color
    )
    {
        var dot = new VisualElement { pickingMode = PickingMode.Ignore };
        dot.style.position = Position.Absolute;
        dot.style.left = left;
        dot.style.top = top;
        UiStyle.FixedSize(dot.style, size, size);
        dot.style.backgroundColor = color;
        UiStyle.Radius(dot.style, size / 2f);
        parent.Add(dot);
    }

    private static void LoadHeroChipIcon(EHero hero, VisualElement icon)
    {
        icon.userData = hero;

        if (HeroPortraitSpriteProvider.TryGetCached(hero, out var cached))
        {
            ApplyHeroChipIcon(icon, cached);
            return;
        }

        ApplyHeroChipIcon(icon, null);
        _ = ApplyHeroChipIconWhenLoadedAsync(hero, icon);
    }

    private static void LoadSourceChipIcon(
        string sourceKey,
        Guid representativeTemplateId,
        VisualElement icon
    )
    {
        icon.userData = sourceKey;

        if (EncounterPortraitSpriteProvider.TryGetCached(representativeTemplateId, out var cached))
        {
            ApplySourceChipIcon(icon, cached);
            return;
        }

        ApplySourceChipIcon(icon, null);
        _ = ApplySourceChipIconWhenLoadedAsync(sourceKey, representativeTemplateId, icon);
    }

    private static async System.Threading.Tasks.Task ApplyHeroChipIconWhenLoadedAsync(
        EHero hero,
        VisualElement icon
    )
    {
        var sprite = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero);
        if (!Equals(icon.userData, hero))
            return;
        ApplyHeroChipIcon(icon, sprite);
    }

    private static async System.Threading.Tasks.Task ApplySourceChipIconWhenLoadedAsync(
        string sourceKey,
        Guid representativeTemplateId,
        VisualElement icon
    )
    {
        var sprite = await EncounterPortraitSpriteProvider.LoadPortraitAsync(
            representativeTemplateId
        );
        if (!Equals(icon.userData, sourceKey))
            return;
        ApplySourceChipIcon(icon, sprite);
    }

    private static void ApplyHeroChipIcon(VisualElement icon, Sprite? sprite)
    {
        if (sprite == null)
        {
            icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            return;
        }

        icon.style.backgroundImage = new StyleBackground(sprite);
        icon.MarkDirtyRepaint();
    }

    private static void ApplySourceChipIcon(VisualElement icon, Sprite? sprite)
    {
        var initials = icon.Q<Label>(SourceChipInitialsName);
        if (sprite == null)
        {
            icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            if (initials != null)
                initials.style.display = DisplayStyle.Flex;
            return;
        }

        icon.style.backgroundImage = new StyleBackground(sprite);
        if (initials != null)
            initials.style.display = DisplayStyle.None;
        icon.MarkDirtyRepaint();
    }

    private static string GetInitials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "?";

        var initials = new List<char>(2);
        foreach (
            var part in displayName.Split(
                new[] { ' ', '-', '_' },
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            initials.Add(char.ToUpperInvariant(part[0]));
            if (initials.Count == 2)
                break;
        }

        if (initials.Count == 0)
            return "?";
        return new string(initials.ToArray());
    }

    // unselectedTextColor carries the game's official keyword color for tag chips; the selected
    // state keeps the gold highlight regardless so selection always reads the same way.
    private static void RefreshChip(Button chip, bool selected, Color? unselectedTextColor = null)
    {
        if (selected)
        {
            chip.style.backgroundColor = Colors.ButtonSelectedBackground;
            chip.style.color = Colors.ButtonSelectedText;
            UiStyle.BorderColor(
                chip.style,
                Colors.ButtonBorderFor(Colors.ButtonSelectedBackground)
            );
        }
        else
        {
            chip.style.backgroundColor = Colors.HistoryChipBackground;
            chip.style.color = unselectedTextColor ?? Colors.HistoryChipText;
            UiStyle.BorderColor(chip.style, Colors.ButtonBorderFor(Colors.HistoryChipBackground));
        }
    }

    private void RefreshPackageToggle(bool selected, bool visible)
    {
        if (_packageToggleButton == null)
            return;

        _packageToggleButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (!visible)
            return;

        StyleButton(
            _packageToggleButton,
            selected ? Colors.ButtonSelectedBackground : Colors.HistoryChipBackground,
            selected ? Colors.ButtonSelectedText : Colors.HistoryChipText
        );
    }

    // Always visible; the face shows the effective day number and highlights when the day
    // participates in filtering (gold = on, chip background = off).
    private void RefreshDayToggle(int day, bool active)
    {
        if (_dayToggleButton == null)
            return;

        _dayToggleButton.text = day.ToString(System.Globalization.CultureInfo.InvariantCulture);
        StyleButton(
            _dayToggleButton,
            active ? Colors.ButtonSelectedBackground : Colors.HistoryChipBackground,
            active ? Colors.ButtonSelectedText : Colors.HistoryChipText
        );
    }

    private void RefreshHeroChip(EHero hero, Button chip, bool selected)
    {
        RefreshChip(chip, selected);
    }

    private static void RefreshTabButton(Button button, bool selected)
    {
        if (selected)
            StyleButton(button, Colors.ButtonSelectedBackground, Colors.ButtonSelectedText);
        else
            StyleButton(button, Colors.RunsTabBackground, Colors.White);
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

    private static Button CreateButton(
        string text,
        Action onClick,
        float width,
        float height,
        bool fixedWidth = true
    )
    {
        var button = new Button(() => onClick()) { text = text };
        if (fixedWidth)
            UiStyle.FixedWidth(button.style, width);
        button.style.height = height;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        button.style.unityFont = BppUiFont.Default;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        UiStyle.Padding(button.style, UiSpacing.None);
        button.style.backgroundColor = Colors.HistoryButtonBackground;
        button.style.color = Colors.White;
        UiStyle.Border(button.style, Borders.Thin, Colors.HistoryButtonBorder);
        UiStyle.Radius(button.style, Radii.Md);

        var textElement = button.Q<TextElement>();
        if (textElement != null)
        {
            textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            textElement.style.flexGrow = 1f;
            textElement.style.unityFont = BppUiFont.Default;
        }
        return button;
    }

    private static void StyleButton(Button button, Color background, Color textColor)
    {
        button.style.backgroundColor = background;
        button.style.color = textColor;
        UiStyle.BorderColor(button.style, Colors.ButtonBorderFor(background));
    }
}
