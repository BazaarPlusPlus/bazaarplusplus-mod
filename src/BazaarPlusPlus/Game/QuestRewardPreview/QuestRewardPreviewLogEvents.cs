#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.QuestRewardPreview;

[BppLogEventSource]
internal static class QuestRewardPreviewLogEvents
{
    internal static readonly BppLogEventDefinition AppendFailed = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.quest_reward_preview.append_failed",
        []
    );

    internal static readonly BppLogEventDefinition LayoutRebuildFailed = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.quest_reward_preview.layout_rebuild_failed",
        []
    );
}
