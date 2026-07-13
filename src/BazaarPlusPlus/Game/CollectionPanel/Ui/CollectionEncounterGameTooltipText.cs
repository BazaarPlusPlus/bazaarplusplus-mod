#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// Content for the BPP section cloned into the native encounter tooltip: one line per
// choice, accent-colored name plus the game's own result text (run through the
// supplied colorizer for native keyword coloring). Card rewards whose pool tier is
// day-driven get the day's effective tier appended to the text.
internal static class CollectionEncounterGameTooltipText
{
    private const string AccentColor = "#FFD37E";
    private const string IneligibleColor = "#8F8268";

    private static readonly ETier[] DealableTiers =
    {
        ETier.Bronze,
        ETier.Silver,
        ETier.Gold,
        ETier.Diamond,
    };

    public static string Build(
        CollectionEncounterOption option,
        Func<string, string>? colorizeResult = null,
        ETier? dayTierCeiling = null
    )
    {
        if (option == null)
            return string.Empty;

        var rawColorize = colorizeResult ?? (text => text);
        string Colorize(string text) =>
            CollectionTooltipMarkup.NormalizeInlineFragment(rawColorize(text));

        if (option.HasOutcomeGroups)
            return BuildOutcomes(option.OutcomeGroups!, Colorize, dayTierCeiling);
        if (!option.HasChoiceDetails)
            return string.Empty;

        var lines = new List<CollectionTooltipMarkup.Block>();
        foreach (var choice in option.ChoiceDetails)
        {
            // Rolled pools ("Advanced Training" trainings, "Epic Battle" monsters)
            // render as one summary line instead of one line per member.
            if (choice.Pool is { } pool)
            {
                lines.Add(ChoicePoolBlock(pool, Colorize, dayTierCeiling));
                continue;
            }

            var result = ChoiceResultText(choice, dayTierCeiling);

            // Prerequisite-unmet options render as one flat dimmed line (no accent, no
            // keyword coloring) at reduced size; the resolver already sorted them last.
            if (!choice.IsEligible)
            {
                var flat = string.IsNullOrWhiteSpace(result)
                    ? choice.DisplayName
                    : CollectionPanelText.JoinTooltipLabel(choice.DisplayName, result);
                lines.Add(
                    new CollectionTooltipMarkup.Paragraph(
                        $"<color={IneligibleColor}>{flat}</color>",
                        fontSizePercent: 85
                    )
                );
                continue;
            }

            lines.Add(
                new CollectionTooltipMarkup.Paragraph(
                    string.IsNullOrWhiteSpace(result)
                        ? $"<color={AccentColor}>{choice.DisplayName}</color>"
                        : CollectionPanelText.JoinColoredTooltipLabel(
                            choice.DisplayName,
                            Colorize(result),
                            AccentColor
                        )
                )
            );
        }
        return CollectionTooltipMarkup.Render(lines);
    }

    // Random-outcome events: one block per rolled alternative with its normalized
    // probability; prerequisite-unmet groups render dimmed without a percentage.
    private static string BuildOutcomes(
        IReadOnlyList<CollectionEncounterOutcomeView> outcomes,
        Func<string, string> colorize,
        ETier? dayTierCeiling
    )
    {
        var items = new List<CollectionTooltipMarkup.ListItem>();
        foreach (var outcome in outcomes)
        {
            string content;
            IReadOnlyList<CollectionTooltipMarkup.ListItem>? children = null;
            if (outcome.IsCombatPool)
            {
                content = CollectionPanelText.OutcomeCombatPool(outcome.OptionCount);
            }
            else if (outcome.Details.Count == 0)
            {
                // Collapsed same-shaped cluster (Farai's NPC packages): count only.
                content = CollectionPanelText.LevelUpRandomPoolSingle(outcome.OptionCount);
            }
            else if (outcome.Details.Count == 1)
            {
                content = DetailLine(outcome.Details[0], colorize, dayTierCeiling);
            }
            else
            {
                content = CollectionPanelText.OutcomeSubPool(outcome.Details.Count);
                var entries = new List<CollectionTooltipMarkup.ListItem>();
                foreach (var detail in outcome.Details)
                    entries.Add(
                        new CollectionTooltipMarkup.ListItem(
                            DetailLine(detail, colorize, dayTierCeiling)
                        )
                    );
                children = entries;
            }

            // Ownership-gated groups render dimmed; their own text already carries
            // the condition ("(if you have Powder Keg or the Big One)").
            if (!outcome.IsEligible)
            {
                items.Add(
                    new CollectionTooltipMarkup.ListItem(
                        content,
                        children,
                        fontSizePercent: 85,
                        color: IneligibleColor
                    )
                );
                continue;
            }

            var prefix = outcome.Percent.HasValue
                ? $"<color={AccentColor}>{outcome.Percent.Value}%</color> "
                : string.Empty;
            items.Add(new CollectionTooltipMarkup.ListItem($"{prefix}{content}", children));
        }
        return CollectionTooltipMarkup.Render(
            new CollectionTooltipMarkup.Block[]
            {
                new CollectionTooltipMarkup.ListBlock(CollectionPanelText.OutcomesHeader(), items),
            }
        );
    }

    // One rolled-pool choice line: combat roll, small expandable entry list (with
    // accent-colored names to match sibling choice lines), or a bare option count.
    private static CollectionTooltipMarkup.Block ChoicePoolBlock(
        CollectionEncounterChoicePool pool,
        Func<string, string> colorize,
        ETier? dayTierCeiling
    )
    {
        if (pool.IsCombat)
            return new CollectionTooltipMarkup.Paragraph(
                $"<color={AccentColor}>{CollectionPanelText.OutcomeCombatPool(pool.OptionCount)}</color>"
            );

        if (pool.Entries.Count == 0)
            return new CollectionTooltipMarkup.Paragraph(
                CollectionPanelText.LevelUpRandomPoolSingle(pool.OptionCount)
            );

        var entries = new List<CollectionTooltipMarkup.ListItem>();
        foreach (var entry in pool.Entries)
        {
            var result = ChoiceResultText(entry, dayTierCeiling);
            entries.Add(
                new CollectionTooltipMarkup.ListItem(
                    string.IsNullOrWhiteSpace(result)
                        ? $"<color={AccentColor}>{entry.DisplayName}</color>"
                    : string.IsNullOrWhiteSpace(entry.DisplayName) ? colorize(result)
                    : CollectionPanelText.JoinColoredTooltipLabel(
                        entry.DisplayName,
                        colorize(result),
                        AccentColor
                    )
                )
            );
        }
        return new CollectionTooltipMarkup.ListBlock(
            CollectionPanelText.OutcomeSubPool(pool.Entries.Count),
            entries
        );
    }

    // One outcome entry: "Name: result", bare name, or bare result — query-pool
    // summaries have no card name, so their result text stands alone.
    private static string DetailLine(
        CollectionEncounterChoiceDetail detail,
        Func<string, string> colorize,
        ETier? dayTierCeiling
    )
    {
        var result = ChoiceResultText(detail, dayTierCeiling);
        if (string.IsNullOrWhiteSpace(result))
            return detail.DisplayName;
        return string.IsNullOrWhiteSpace(detail.DisplayName)
            ? colorize(result)
            : CollectionPanelText.JoinTooltipLabel(detail.DisplayName, colorize(result));
    }

    // Flattens embedded newlines (descriptions render as list entries) and appends
    // the day-tier suffix where the pool is day-driven.
    private static string ChoiceResultText(
        CollectionEncounterChoiceDetail choice,
        ETier? dayTierCeiling
    )
    {
        var result = choice.ResultText;
        if (string.IsNullOrWhiteSpace(result))
            return string.Empty;

        result = CollectionPanelText.NormalizeRewardSpacing(
            CollapseWhitespace(result.Replace("\r", string.Empty).Replace('\n', ' '))
        );
        var suffix = DayTierSuffix(choice, dayTierCeiling);
        if (suffix == null)
            return result;
        // Full-width punctuation carries its own visual gap; an ASCII space in
        // front of it reads as a hole.
        return suffix.Length > 0 && suffix[0] >= '⺀' ? $"{result}{suffix}" : $"{result} {suffix}";
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var character in text)
        {
            var isSpace = character == ' ';
            if (isSpace && previousWasSpace)
                continue;
            previousWasSpace = isSpace;
            builder.Append(character);
        }
        return builder.ToString();
    }

    // A pool with no tier constraint (or one spanning every dealable tier) is day-driven:
    // clamp it to the day's ceiling and spell out the effective tier. Narrow constraints
    // mean the text already states the tier explicitly, so nothing is appended.
    private static string? DayTierSuffix(
        CollectionEncounterChoiceDetail choice,
        ETier? dayTierCeiling
    )
    {
        if (!dayTierCeiling.HasValue || choice.RewardFilter is not { } reward)
            return null;
        if (!reward.UsesDayTierTable)
            return null;

        var tiers = reward.Tiers;
        if (tiers.Count != 0 && tiers.Count < DealableTiers.Length)
            return null;
        if (HasExplicitTierDescriptor(choice.ResultText))
            return null;

        var ceilingRank = CollectionCardFacetRanks.TierRank(dayTierCeiling.Value);
        var effective = new List<ETier>();
        foreach (var tier in DealableTiers)
            if (CollectionCardFacetRanks.TierRank(tier) <= ceilingRank)
                effective.Add(tier);

        if (effective.Count == 0)
            return null;
        return effective.Count == 1
            ? CollectionPanelText.EncounterTierExact(effective[0])
            : CollectionPanelText.EncounterDayTierSuffix(effective[^1]);
    }

    private static bool HasExplicitTierDescriptor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var tier in Enum.GetValues(typeof(ETier)))
        {
            if (tier is ETier value && HasExplicitTierDescriptor(text, value))
                return true;
        }

        return false;
    }

    private static bool HasExplicitTierDescriptor(string text, ETier tier)
    {
        var tierName = tier.ToString();
        if (HasEnglishTierDescriptor(text, tierName))
            return true;
        if (ContainsOrdinalIgnoreCase(text, $"({tierName})"))
            return true;

        // The result text is written in the game language's script, which is independent
        // of the BPP locale mode, so both Chinese variants are always tested.
        var (_, chineseMainland, chineseTraditional) = CollectionPanelText.TierForms(tier);
        return HasChineseTierDescriptor(text, chineseMainland)
            || HasChineseTierDescriptor(text, chineseTraditional);
    }

    private static bool HasChineseTierDescriptor(string text, string tierWord)
    {
        if (string.IsNullOrEmpty(tierWord))
            return false;

        return ContainsOrdinalIgnoreCase(text, $"{tierWord}级")
            || ContainsOrdinalIgnoreCase(text, $"{tierWord}級")
            || ContainsOrdinalIgnoreCase(text, $"{tierWord}階")
            || ContainsOrdinalIgnoreCase(text, $"{tierWord}品质")
            || ContainsOrdinalIgnoreCase(text, $"{tierWord}品質")
            || ContainsOrdinalIgnoreCase(text, $"（{tierWord}）");
    }

    private static bool HasEnglishTierDescriptor(string text, string tierName)
    {
        var index = 0;
        while (index < text.Length)
        {
            index = text.IndexOf(tierName, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            if (IsAsciiWordBoundary(text, index - 1))
            {
                var cursor = index + tierName.Length;
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                    cursor++;
                if (cursor < text.Length && text[cursor] == '-')
                    cursor++;
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                    cursor++;

                const string TierSuffix = "tier";
                if (
                    cursor + TierSuffix.Length <= text.Length
                    && string.Compare(
                        text,
                        cursor,
                        TierSuffix,
                        0,
                        TierSuffix.Length,
                        StringComparison.OrdinalIgnoreCase
                    ) == 0
                    && IsAsciiWordBoundary(text, cursor + TierSuffix.Length)
                )
                    return true;
            }

            index += tierName.Length;
        }

        return false;
    }

    private static bool IsAsciiWordBoundary(string text, int index)
    {
        if (index < 0 || index >= text.Length)
            return true;

        var character = text[index];
        return !(
            character >= 'a' && character <= 'z'
            || character >= 'A' && character <= 'Z'
            || character >= '0' && character <= '9'
            || character == '_'
        );
    }

    private static bool ContainsOrdinalIgnoreCase(string text, string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
