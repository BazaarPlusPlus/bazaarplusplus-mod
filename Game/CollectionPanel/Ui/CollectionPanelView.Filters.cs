#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.EncounterPortraits;
using BazaarPlusPlus.GameInterop.HeroPortraits;
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
            _sourceChipRow.Add(chip);
        }
    }

    private bool SourceChipsMatch(IReadOnlyList<CollectionSourceOptionViewModel> sources)
    {
        if (sources.Count != _sourceChips.Count)
            return false;
        foreach (var source in sources)
        {
            if (!_sourceChips.ContainsKey(source.SourceKey))
                return false;
        }
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
        _sourceChipRow?.Clear();
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
            Sizes.HeroChipButtonSize,
            Sizes.HeroChipButtonSize
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
        UiStyle.FixedSize(icon.style, Sizes.HeroChipIconSize, Sizes.HeroChipIconSize);
        icon.style.position = Position.Relative;
        icon.style.backgroundColor = Colors.HistoryStatusBackground;
        icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
        icon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        UiStyle.Border(icon.style, Borders.Thin, Colors.HistoryButtonBorder);
        UiStyle.Radius(icon.style, Sizes.HeroChipIconSize / 2f);

        var initials = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistoryChipText);
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
        if (sprite == null)
        {
            icon.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            return;
        }

        icon.style.backgroundImage = new StyleBackground(sprite);
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

    private static void RefreshChip(Button chip, bool selected)
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
            chip.style.color = Colors.HistoryChipText;
            UiStyle.BorderColor(chip.style, Colors.ButtonBorderFor(Colors.HistoryChipBackground));
        }
    }

    private void RefreshResetButton(bool enabled)
    {
        if (_clearButton == null)
            return;

        _clearButton.text = CollectionPanelText.Reset();
        _clearButton.SetEnabled(enabled);
        if (enabled)
        {
            StyleButton(_clearButton, Colors.HistoryButtonBackground, Colors.HistoryChipText);
            _clearButton.style.opacity = 1f;
        }
        else
        {
            StyleButton(
                _clearButton,
                Colors.WithAlpha(Colors.HistoryStatusBackground, 0.52f),
                Colors.WithAlpha(Colors.HistoryStatusText, 0.54f)
            );
            _clearButton.style.opacity = 0.82f;
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

        if (_packageToggleLabel != null)
            _packageToggleLabel.style.color = selected
                ? Colors.ButtonSelectedText
                : Colors.HistoryChipText;

        if (_packageSwitchTrack != null)
        {
            _packageSwitchTrack.style.backgroundColor = selected
                ? Colors.WithAlpha(Colors.ButtonSelectedText, 0.22f)
                : Colors.HistoryStatusBackground;
            UiStyle.BorderColor(
                _packageSwitchTrack.style,
                selected ? Colors.ButtonSelectedText : Colors.HistoryStatusBorder
            );
        }

        if (_packageSwitchKnob != null)
        {
            _packageSwitchKnob.style.left = selected
                ? Sizes.PackageSwitchKnobOnLeft
                : Sizes.PackageSwitchKnobOffLeft;
            _packageSwitchKnob.style.backgroundColor = selected
                ? Colors.ButtonSelectedText
                : Colors.HistoryStatusText;
        }
    }

    private void StyleSearchShell(bool focused)
    {
        if (_searchShell == null)
            return;

        _searchShell.style.backgroundColor = focused
            ? Colors.WithAlpha(Colors.HistoryStatusBackground, 0.92f)
            : Colors.HistoryStatusBackground;
        UiStyle.BorderColor(
            _searchShell.style,
            focused ? Colors.RunRowSelectedAccent : Colors.HistoryStatusBorder
        );
    }

    private void RefreshSearchPlaceholder(string search)
    {
        if (_searchPlaceholderLabel == null)
            return;

        _searchPlaceholderLabel.style.display = string.IsNullOrWhiteSpace(search)
            ? DisplayStyle.Flex
            : DisplayStyle.None;
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
