#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionEncounterOption
{
    public CollectionEncounterOption(
        Guid templateId,
        string displayName,
        string? sourceKey,
        CollectionSourceKind? sourceKind,
        Guid representativeTemplateId,
        string resultText,
        CollectionEncounterRewardFilter? rewardFilter,
        IReadOnlyList<CollectionEncounterChoiceDetail>? choiceDetails = null
    )
    {
        TemplateId = templateId;
        DisplayName = displayName;
        SourceKey = sourceKey;
        SourceKind = sourceKind;
        RepresentativeTemplateId = representativeTemplateId;
        ResultText = resultText ?? string.Empty;
        RewardFilter = rewardFilter;
        ChoiceDetails = choiceDetails ?? Array.Empty<CollectionEncounterChoiceDetail>();
    }

    public Guid TemplateId { get; }

    public string DisplayName { get; }

    public string? SourceKey { get; }

    public CollectionSourceKind? SourceKind { get; }

    public Guid RepresentativeTemplateId { get; }

    public string ResultText { get; }

    public CollectionEncounterRewardFilter? RewardFilter { get; }

    public IReadOnlyList<CollectionEncounterChoiceDetail> ChoiceDetails { get; }

    public bool IsSourceMatch => !string.IsNullOrWhiteSpace(SourceKey) && SourceKind.HasValue;

    public bool HasRewardFilter => RewardFilter != null;

    public bool HasChoiceDetails => ChoiceDetails.Count > 0;
}
