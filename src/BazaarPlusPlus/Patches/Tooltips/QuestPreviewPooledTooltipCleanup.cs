#nullable enable

namespace BazaarPlusPlus.Patches.Tooltips;

internal static class QuestPreviewPooledTooltipCleanup
{
    internal static void Clear()
    {
        QuestRewardPreviewTooltipPatch.ClearPooledPresentation();
        AggregateItemMissingTypesTooltipPatch.ClearPooledPresentation();
    }
}
