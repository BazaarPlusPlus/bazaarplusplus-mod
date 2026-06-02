#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionPanelSelectionState
{
    public const string DefaultMerchantSourceKey = "merchant:ande:bronze:global";

    public static CollectionPanelSelectionState Default { get; } =
        new(EHero.Vanessa, DefaultMerchantSourceKey);

    public CollectionPanelSelectionState(EHero? selectedHero, string? selectedMerchantSourceKey)
    {
        SelectedHero = selectedHero;
        SelectedMerchantSourceKey = NormalizeSourceKey(selectedMerchantSourceKey);
    }

    public EHero? SelectedHero { get; }

    public string? SelectedMerchantSourceKey { get; }

    public override bool Equals(object? obj)
    {
        return obj is CollectionPanelSelectionState other
            && SelectedHero == other.SelectedHero
            && string.Equals(
                SelectedMerchantSourceKey,
                other.SelectedMerchantSourceKey,
                StringComparison.Ordinal
            );
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + SelectedHero.GetHashCode();
            hash =
                (hash * 31)
                + (
                    SelectedMerchantSourceKey == null
                        ? 0
                        : StringComparer.Ordinal.GetHashCode(SelectedMerchantSourceKey)
                );
            return hash;
        }
    }

    private static string? NormalizeSourceKey(string? sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            return null;
        return sourceKey.Trim();
    }
}
