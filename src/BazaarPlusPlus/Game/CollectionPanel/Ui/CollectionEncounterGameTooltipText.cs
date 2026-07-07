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
        if (option == null || !option.HasChoiceDetails)
            return string.Empty;

        var colorize = colorizeResult ?? (text => text);
        var lines = new List<string>();
        foreach (var choice in option.ChoiceDetails)
        {
            var result = choice.ResultText;
            if (!string.IsNullOrWhiteSpace(result))
            {
                var suffix = DayTierSuffix(choice, dayTierCeiling);
                if (suffix != null)
                    result = $"{result} {suffix}";
            }

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
