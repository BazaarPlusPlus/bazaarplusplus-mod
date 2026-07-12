#nullable enable
using BazaarPlusPlus.Patches;

namespace BazaarPlusPlus.Game.QuestRewardPreview;

internal static class QuestRewardPreviewGate
{
    internal static bool IsEnabled()
    {
        return BppPatchHost.Services.Config.EnableQuestRewardPreviewConfig?.Value == true;
    }
}
