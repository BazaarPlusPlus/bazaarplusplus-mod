#nullable enable
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal enum CollectionSortPriority
{
    Quality,
    Size,
}

// Mutable selection state held by CollectionPanel; pure data. The filter engine reads
// this and produces an ordered visible set.
internal sealed class CollectionFilterState
{
    public ECardType ActiveType { get; set; } = ECardType.Item;
    public HashSet<EHero> Heroes { get; } = new();
    public HashSet<ETier> Tiers { get; } = new();
    public HashSet<ECardTag> Tags { get; } = new();

    // Item card size (Small/Medium/Large). Only meaningful on the Item tab — Skills are a single
    // size, so the engine ignores this set when ActiveType is Skill and the UI hides the row.
    public HashSet<ECardSize> Sizes { get; } = new();
    public HashSet<CollectionMerchantKind> Merchants { get; } = new();
    public string? SelectedMerchantSourceKey { get; set; }
    public string? SelectedTrainerSourceKey { get; set; }
    public bool IncludePackages { get; set; }
    public CollectionSortPriority SortPriority { get; set; } = CollectionSortPriority.Quality;

    public bool HasActiveFilters =>
        Heroes.Count > 1
        || Tiers.Count > 0
        || Tags.Count > 0
        || Sizes.Count > 0
        || Merchants.Count > 0
        || !string.IsNullOrWhiteSpace(SelectedMerchantSourceKey)
        || !string.IsNullOrWhiteSpace(SelectedTrainerSourceKey)
        || IncludePackages
        || SortPriority != CollectionSortPriority.Quality;

    public EHero? SelectedHero
    {
        get
        {
            if (Heroes.Count != 1)
                return null;
            foreach (var hero in Heroes)
                return hero;
            return null;
        }
    }

    public string? GetSelectedSourceKey(ECardType activeType) =>
        activeType == ECardType.Skill ? SelectedTrainerSourceKey : SelectedMerchantSourceKey;

    public void ApplySelection(CollectionPanelSelectionState selection)
    {
        if (selection == null)
            throw new System.ArgumentNullException(nameof(selection));

        Heroes.Clear();
        Heroes.Add(selection.SelectedHero ?? CollectionPanelSelectionState.DefaultHero);

        Merchants.Clear();
        if (selection.SelectedSourceKind == CollectionSourceKind.Trainer)
        {
            ActiveType = ECardType.Skill;
            SelectedMerchantSourceKey = null;
            SelectedTrainerSourceKey = selection.SelectedSourceKey;
            return;
        }

        ActiveType = ECardType.Item;
        SelectedMerchantSourceKey = selection.SelectedSourceKey;
        SelectedTrainerSourceKey = null;
    }

    public CollectionPanelSelectionState ToSelectionState()
    {
        var kind =
            ActiveType == ECardType.Skill
                ? CollectionSourceKind.Trainer
                : CollectionSourceKind.Merchant;
        return new CollectionPanelSelectionState(
            SelectedHero,
            GetSelectedSourceKey(ActiveType),
            kind
        );
    }

    public void ToggleHero(EHero hero)
    {
        if (Heroes.Count == 1 && Heroes.Contains(hero))
            return;

        Heroes.Clear();
        Heroes.Add(hero);
    }

    public void ToggleSource(ECardType activeType, string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            return;

        if (activeType == ECardType.Skill)
        {
            SelectedTrainerSourceKey = string.Equals(
                SelectedTrainerSourceKey,
                sourceKey,
                System.StringComparison.Ordinal
            )
                ? null
                : sourceKey;
            return;
        }

        SelectedMerchantSourceKey = string.Equals(
            SelectedMerchantSourceKey,
            sourceKey,
            System.StringComparison.Ordinal
        )
            ? null
            : sourceKey;
    }

    public bool ClearSelectedSource(ECardType activeType)
    {
        if (activeType == ECardType.Skill)
        {
            if (string.IsNullOrWhiteSpace(SelectedTrainerSourceKey))
                return false;
            SelectedTrainerSourceKey = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(SelectedMerchantSourceKey))
            return false;
        SelectedMerchantSourceKey = null;
        return true;
    }

    public bool PruneSelectedSources(
        IReadOnlyCollection<string> visibleMerchantSourceKeys,
        IReadOnlyCollection<string> visibleTrainerSourceKeys
    )
    {
        var changed = false;
        if (
            !string.IsNullOrWhiteSpace(SelectedMerchantSourceKey)
            && !ContainsOrdinal(visibleMerchantSourceKeys, SelectedMerchantSourceKey!)
        )
        {
            SelectedMerchantSourceKey = null;
            changed = true;
        }

        if (
            !string.IsNullOrWhiteSpace(SelectedTrainerSourceKey)
            && !ContainsOrdinal(visibleTrainerSourceKeys, SelectedTrainerSourceKey!)
        )
        {
            SelectedTrainerSourceKey = null;
            changed = true;
        }

        return changed;
    }

    public void Reset()
    {
        var selectedHero = SelectedHero ?? CollectionPanelSelectionState.DefaultHero;
        ActiveType = ECardType.Item;
        Heroes.Clear();
        Heroes.Add(selectedHero);
        Tiers.Clear();
        Tags.Clear();
        Sizes.Clear();
        Merchants.Clear();
        SelectedMerchantSourceKey = null;
        SelectedTrainerSourceKey = null;
        IncludePackages = false;
        SortPriority = CollectionSortPriority.Quality;
    }

    private static bool ContainsOrdinal(IReadOnlyCollection<string> values, string value)
    {
        foreach (var candidate in values)
        {
            if (string.Equals(candidate, value, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
