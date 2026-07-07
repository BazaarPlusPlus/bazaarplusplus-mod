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
        bool isEligible = true
    )
    {
        TemplateId = templateId;
        DisplayName = displayName;
        ResultText = resultText ?? string.Empty;
        RewardFilter = rewardFilter;
        IsSourceMatch = isSourceMatch;
        PrerequisiteSummary = prerequisiteSummary ?? string.Empty;
        IsEligible = isEligible;
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
}
