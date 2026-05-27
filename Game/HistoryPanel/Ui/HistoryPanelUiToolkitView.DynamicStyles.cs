#nullable enable
using System;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelUiToolkitView
{
    private static Label CreateBattleDayBubble(VisualElement parent)
    {
        var bubble = CreateDayBubble(parent);
        bubble.style.fontSize = Sizes.FontBody;
        return bubble;
    }

    private static void ConfigurePill(
        Label pill,
        string text,
        Color background,
        Color textColor,
        bool visible
    )
    {
        pill.text = text;
        pill.style.backgroundColor = background;
        pill.style.color = textColor;
        pill.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void ConfigureInfoChip(Label chip, string label, string value, Color accent)
    {
        chip.text = $"{label} {value}";
        chip.style.backgroundColor = Colors.InfoChipBackground(accent);
        chip.style.color = accent;
        chip.style.borderLeftWidth = Borders.Accent;
        chip.style.borderLeftColor = Colors.InfoChipBorder(accent);
    }

    private static void ConfigureStatusPill(Label pill, string rawStatus)
    {
        var status = HistoryPanelFormatter.FormatRunStatus(rawStatus);
        var isCompleted = string.Equals(rawStatus, "completed", StringComparison.OrdinalIgnoreCase);
        var isAbandoned = string.Equals(rawStatus, "abandoned", StringComparison.OrdinalIgnoreCase);
        var background =
            isCompleted ? Colors.StatusCompletedBackground
            : isAbandoned ? Colors.StatusAbandonedBackground
            : Colors.StatusDefaultBackground;
        var text =
            isCompleted ? Colors.StatusCompletedText
            : isAbandoned ? Colors.StatusAbandonedText
            : Colors.StatusDefaultText;
        ConfigurePill(pill, status, background, text, true);
    }

    private static void BindHeroPill(Label pill, string? rawHero)
    {
        var hero = HistoryPanelFormatter.FormatOpponentHero(rawHero);
        if (string.IsNullOrWhiteSpace(hero))
        {
            ConfigurePill(pill, string.Empty, Colors.Clear, Colors.Clear, false);
            return;
        }

        var heroStyle = GetHeroBadgeStyle(hero);
        ConfigurePill(pill, heroStyle.ShortCode, heroStyle.Background, heroStyle.Text, true);
    }

    private static void BindBattleRankPill(Label pill, string? rawRank, int? rating)
    {
        var rank = HistoryPanelFormatter.NormalizeRank(rawRank);
        if (string.Equals(rank, "Legendary", StringComparison.OrdinalIgnoreCase))
        {
            ConfigurePill(
                pill,
                HistoryPanelText.RankLabel(rank, rating),
                Colors.RankLegendaryBackground,
                Colors.White,
                true
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(rank))
        {
            ConfigurePill(pill, string.Empty, Colors.Clear, Colors.Clear, false);
            return;
        }

        var palette = GetRankBadgePalette(rank);
        ConfigurePill(
            pill,
            HistoryPanelText.RankLabel(rank),
            palette.Background,
            palette.Text,
            true
        );
    }

    private static void BindRunOutcomeBubble(Label bubble, HistoryRunRecord run)
    {
        var tier = HistoryPanelFormatter.GetRunOutcomeTier(run) ?? RunOutcomeTier.Misfortune;
        var palette = GetOutcomePalette(tier);
        bubble.text = HistoryPanelText.RunOutcomeBubbleLabel(tier);
        bubble.style.backgroundColor = palette.Background;
        UiStyle.BorderColor(bubble.style, palette.Border);
    }

    private static void RefreshTabButton(Button button, bool selected)
    {
        StyleButton(
            button,
            selected ? Colors.ButtonSelectedBackground : Colors.RunsTabBackground,
            selected ? Colors.ButtonSelectedText : Colors.White
        );
    }

    private static void RefreshGhostFilterButton(Button button, bool selected)
    {
        StyleButton(
            button,
            selected ? Colors.ButtonSelectedBackground : Colors.GhostFilterBackground,
            selected ? Colors.ButtonSelectedText : Colors.White
        );
    }

    private static void RefreshDeleteButton(Button button, string text, bool enabled)
    {
        var isConfirmState = string.Equals(
            text,
            HistoryPanelText.DeleteConfirm(),
            StringComparison.Ordinal
        );
        if (isConfirmState)
        {
            StyleButton(button, Colors.DeleteConfirmBackground, Colors.DeleteConfirmText);
            return;
        }

        if (!enabled)
        {
            StyleButton(button, Colors.DeleteDisabledBackground, Colors.DeleteDisabledText);
            return;
        }

        StyleButton(button, Colors.DeleteBackground, Colors.DeleteText);
    }

    private static void ApplyRunRowState(RunRowRefs refs, bool selected)
    {
        refs.Root.style.backgroundColor = selected
            ? Colors.RunRowSelectedBackground
            : Colors.HistoryRowBackground;
        refs.Accent.style.backgroundColor = selected
            ? Colors.RunRowSelectedAccent
            : Colors.RunRowDefaultAccent;
        var borderColor = selected ? Colors.RunRowSelectedBorder : Colors.RunRowDefaultBorder;
        UiStyle.BorderColor(refs.Root.style, borderColor);
        UiStyle.BorderColor(refs.OutcomeBubble.style, borderColor);
        refs.OutcomeBubble.style.opacity = selected ? 1f : 0.96f;
    }

    private static void ApplyBattleRowState(
        BattleRowRefs refs,
        bool selected,
        HistoryBattleRecord battle
    )
    {
        var isWin = HistoryPanelFormatter.IsBattleWin(battle);
        var isLoss = HistoryPanelFormatter.IsBattleLoss(battle);
        var isEliminated = HistoryPanelFormatter.IsGhostOpponentEliminated(battle);
        refs.Root.style.backgroundColor = GetBattleRowBackground(
            selected,
            isEliminated,
            isWin,
            isLoss
        );
        refs.Accent.style.backgroundColor = GetBattleAccent(isEliminated, isWin, isLoss);
        var borderColor = GetBattleBorder(isEliminated, isWin, isLoss);
        UiStyle.BorderColor(refs.Root.style, borderColor);
        refs.DayBubble.style.backgroundColor = GetBattleDayBackground(isEliminated, isWin, isLoss);
        UiStyle.BorderColor(refs.DayBubble.style, borderColor);
    }
}
