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

        var colorize = colorizeResult ?? (text => text);
        if (option.HasOutcomeGroups)
            return BuildOutcomes(option.OutcomeGroups!, colorize, dayTierCeiling);
        if (!option.HasChoiceDetails)
            return string.Empty;

        var lines = new List<string>();
        foreach (var choice in option.ChoiceDetails)
        {
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
        // A shrunken non-empty spacer line between choices keeps distinct options
        // visually separated without inflating intra-choice line wrapping.
        return string.Join("\n<size=45%> </size>\n", lines);
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
            else if (outcome.Details.Count == 1)
            {
                var detail = outcome.Details[0];
                var result = ChoiceResultText(detail, dayTierCeiling);
                content = string.IsNullOrWhiteSpace(result)
                    ? detail.DisplayName
                    : $"{detail.DisplayName}: {colorize(result)}";
            }
            else
            {
                var block = new System.Text.StringBuilder(
                    CollectionPanelText.OutcomeSubPool(outcome.Details.Count)
                );
                foreach (var detail in outcome.Details)
                {
                    var result = ChoiceResultText(detail, dayTierCeiling);
                    // Hanging indent keeps soft-wrapped lines aligned with the dash.
                    block.Append("\n<indent=2.2em>- ");
                    block.Append(
                        string.IsNullOrWhiteSpace(result)
                            ? detail.DisplayName
                            : $"{detail.DisplayName}: {colorize(result)}"
                    );
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
        return string.Join("\n<size=45%> </size>\n", lines);
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

        result = result.Replace("\r", string.Empty).Replace('\n', ' ');
        var suffix = DayTierSuffix(choice, dayTierCeiling);
        return suffix == null ? result : $"{result} {suffix}";
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
