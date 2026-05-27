#nullable enable

namespace BazaarPlusPlus.Game.CardSetPreview;

internal static class CardSetBuildRecommendationModeFlow
{
    public static CardSetBuildRecommendationMode GetNext(
        CardSetBuildRecommendationMode currentMode
    )
    {
        return currentMode == CardSetBuildRecommendationMode.SelectedSet
            ? CardSetBuildRecommendationMode.FinalBuild
            : CardSetBuildRecommendationMode.SelectedSet;
    }
}
