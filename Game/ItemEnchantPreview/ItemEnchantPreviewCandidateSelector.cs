using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewCandidateSelector
{
    public static IReadOnlyList<EEnchantmentType> SelectCandidates(
        EEnchantmentType? currentEnchantment,
        IEnumerable<EEnchantmentType> availableEnchantments,
        IEnumerable<EEnchantmentType> allEnchantments
    )
    {
        var preferred = availableEnchantments?.Distinct().ToList() ?? new List<EEnchantmentType>();
        var fallback = allEnchantments?.Distinct().ToList() ?? new List<EEnchantmentType>();
        var source = preferred.Count > 0 ? preferred : fallback;

        return source.Where(enchantment => enchantment != currentEnchantment).ToList();
    }
}
