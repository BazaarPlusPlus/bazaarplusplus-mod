#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    private const string EnglishTierNames = "bronze|silver|gold|diamond|legendary";

    private static readonly Regex ExplicitTierDescriptorRegex = new(
        $@"\b(?:{EnglishTierNames})\s*-?\s*tier\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

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

        var colorize = colorizeResult ?? (text => text);
        if (option.HasOutcomeGroups)
            return BuildOutcomes(option.OutcomeGroups!, colorize, dayTierCeiling);
        if (!option.HasChoiceDetails)
            return string.Empty;

        var lines = new List<string>();
        foreach (var choice in option.ChoiceDetails)
        {
            // Rolled pools ("Advanced Training" trainings, "Epic Battle" monsters)
            // render as one summary line instead of one line per member.
            if (choice.Pool is { } pool)
            {
                lines.Add(ChoicePoolLine(pool, colorize, dayTierCeiling));
                continue;
            }

            var result = ChoiceResultText(choice, dayTierCeiling);

            // Prerequisite-unmet options render as one flat dimmed line (no accent, no
            // keyword coloring) at reduced size; the resolver already sorted them last.
            if (!choice.IsEligible)
            {
                var flat = string.IsNullOrWhiteSpace(result)
                    ? choice.DisplayName
                    : $"{choice.DisplayName}: {result}";
                lines.Add($"<size=85%><color={IneligibleColor}>{flat}</color></size>");
                continue;
            }

            lines.Add(
                string.IsNullOrWhiteSpace(result)
                    ? $"<color={AccentColor}>{choice.DisplayName}</color>"
                    : $"<color={AccentColor}>{choice.DisplayName}:</color> {colorize(result)}"
            );
        }
        return CollectionTooltipMarkup.Wrap(string.Join(CollectionTooltipMarkup.BlockBreak, lines));
    }

    // Random-outcome events: one block per rolled alternative with its normalized
    // probability; prerequisite-unmet groups render dimmed without a percentage.
    private static string BuildOutcomes(
        IReadOnlyList<CollectionEncounterOutcomeView> outcomes,
        Func<string, string> colorize,
        ETier? dayTierCeiling
    )
    {
        var lines = new List<string> { CollectionPanelText.OutcomesHeader() };
        foreach (var outcome in outcomes)
        {
            string content;
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
                var block = new System.Text.StringBuilder(
                    CollectionPanelText.OutcomeSubPool(outcome.Details.Count)
                );
                foreach (var detail in outcome.Details)
                {
                    // Hanging indent keeps soft-wrapped lines aligned with the dash.
                    block.Append(CollectionTooltipMarkup.SubItemBreak);
                    block.Append("<indent=2.2em>- ");
                    block.Append(DetailLine(detail, colorize, dayTierCeiling));
                    block.Append("</indent>");
                }
                content = block.ToString();
            }

            // Ownership-gated groups render dimmed; their own text already carries
            // the condition ("(if you have Powder Keg or the Big One)").
            if (!outcome.IsEligible)
            {
                lines.Add(
                    $"<size=85%><color={IneligibleColor}>· <indent=1em>{content}</indent></color></size>"
                );
                continue;
            }

            var prefix = outcome.Percent.HasValue
                ? $"<color={AccentColor}>{outcome.Percent.Value}%</color> "
                : string.Empty;
            lines.Add($"· <indent=1em>{prefix}{content}</indent>");
        }
        return CollectionTooltipMarkup.Wrap(string.Join(CollectionTooltipMarkup.BlockBreak, lines));
    }

    // One rolled-pool choice line: combat roll, small expandable entry list (with
    // accent-colored names to match sibling choice lines), or a bare option count.
    private static string ChoicePoolLine(
        CollectionEncounterChoicePool pool,
        Func<string, string> colorize,
        ETier? dayTierCeiling
    )
    {
        if (pool.IsCombat)
            return $"<color={AccentColor}>{CollectionPanelText.OutcomeCombatPool(pool.OptionCount)}</color>";

        if (pool.Entries.Count == 0)
            return CollectionPanelText.LevelUpRandomPoolSingle(pool.OptionCount);

        var block = new System.Text.StringBuilder(
            CollectionPanelText.OutcomeSubPool(pool.Entries.Count)
        );
        foreach (var entry in pool.Entries)
        {
            var result = ChoiceResultText(entry, dayTierCeiling);
            block.Append(CollectionTooltipMarkup.SubItemBreak);
            block.Append("<indent=2.2em>- ");
            block.Append(
                string.IsNullOrWhiteSpace(result)
                    ? $"<color={AccentColor}>{entry.DisplayName}</color>"
                : string.IsNullOrWhiteSpace(entry.DisplayName) ? colorize(result)
                : $"<color={AccentColor}>{entry.DisplayName}:</color> {colorize(result)}"
            );
            block.Append("</indent>");
        }
        return block.ToString();
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
            : $"{detail.DisplayName}: {colorize(result)}";
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

        result = CollapseWhitespace(result.Replace("\r", string.Empty).Replace('\n', ' '));
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
        if (ExplicitTierDescriptorRegex.IsMatch(choice.ResultText ?? string.Empty))
            return null;

        var tiers = reward.Tiers;
        if (tiers.Count != 0 && tiers.Count < DealableTiers.Length)
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
}
