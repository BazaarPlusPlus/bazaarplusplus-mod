#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.QuestRewardPreview;

internal static class QuestRewardPreviewSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Quest Reward Preview",
        "任务奖励预览",
        "任務獎勵預覽",
        "Questbelohnungsvorschau",
        "Prévia de Recompensa de Missão",
        "퀘스트 보상 미리보기",
        "Anteprima Ricompensa Missione"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
