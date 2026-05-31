#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
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
        ClearChipRow(_heroChips, _heroChipRow, keepFirst: true);
        foreach (var hero in heroes)
        {
            var chip = CreateChipButton(CollectionPanelText.Hero(hero), () => _toggleHero(hero));
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
        ClearChipRow(_tierChips, _tierChipRow, keepFirst: true);
        foreach (var tier in tiers)
        {
            var chip = CreateChipButton(CollectionPanelText.Tier(tier), () => _toggleTier(tier));
            _tierChips[tier] = chip;
            _tierChipRow.Add(chip);
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
        ClearChipRow(_sizeChips, _sizeChipRow, keepFirst: true);
        foreach (var size in sizes)
        {
            var chip = CreateChipButton(CollectionPanelText.Size(size), () => _toggleSize(size));
            _sizeChips[size] = chip;
            _sizeChipRow.Add(chip);
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

    private Button CreateChipButton(string text, Action onClick)
    {
        var chip = CreateButton(text, onClick, Sizes.ChipMinWidth + 12f, Sizes.ChipHeight);
        chip.style.marginRight = UiSpacing.Sm;
        chip.style.marginBottom = UiSpacing.Xs;
        StyleButton(chip, Colors.HistoryChipBackground, Colors.HistoryChipText);
        return chip;
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

    private static Button CreateButton(string text, Action onClick, float width, float height)
    {
        var button = new Button(() => onClick()) { text = text };
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
