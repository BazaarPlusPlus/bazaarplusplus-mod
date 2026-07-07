#nullable enable
using System;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionEncounterChoiceDetail
{
    public CollectionEncounterChoiceDetail(
        Guid templateId,
        string displayName,
        string resultText,
        CollectionEncounterRewardFilter? rewardFilter,
        bool isSourceMatch,
        string prerequisiteSummary = "",
        bool isEligible = true,
        CollectionEncounterChoicePool? pool = null
    )
    {
        TemplateId = templateId;
        DisplayName = displayName;
        ResultText = resultText ?? string.Empty;
        RewardFilter = rewardFilter;
        IsSourceMatch = isSourceMatch;
        PrerequisiteSummary = prerequisiteSummary ?? string.Empty;
        IsEligible = isEligible;
        Pool = pool;
    }

    public Guid TemplateId { get; }

    public string DisplayName { get; }

    public string ResultText { get; }

    public CollectionEncounterRewardFilter? RewardFilter { get; }

    public bool IsSourceMatch { get; }

    public string PrerequisiteSummary { get; }

    // False when the option's card prerequisites (e.g. "if you have a Bushel") are not
    // met by the player's current inventory; such options render dimmed at the bottom.
    public bool IsEligible { get; }

    // Set when this "choice" is really a random pool (a Random-selection spawn group
    // inside a choice event, e.g. Advanced Training's 16 trainings or Epic Battle's
    // 14 monsters); the line renders as a pool summary instead of one card.
    public CollectionEncounterChoicePool? Pool { get; }
}

// A random pool presented as one choice line: a combat roll, an expandable entry
// list (small pools), or a bare option count (large pools, where Entries is empty).
internal sealed class CollectionEncounterChoicePool
{
    public CollectionEncounterChoicePool(
        bool isCombat,
        int optionCount,
        System.Collections.Generic.IReadOnlyList<CollectionEncounterChoiceDetail> entries
    )
    {
        IsCombat = isCombat;
        OptionCount = optionCount;
        Entries = entries;
    }

    public bool IsCombat { get; }

    public int OptionCount { get; }

    public System.Collections.Generic.IReadOnlyList<CollectionEncounterChoiceDetail> Entries { get; }
}
