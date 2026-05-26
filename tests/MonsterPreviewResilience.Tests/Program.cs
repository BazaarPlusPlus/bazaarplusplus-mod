using BazaarPlusPlus.Game.MonsterPreview;

TestRecommendationModesExposeOnlyCurrentAndTenWin();
TestRecommendationModeFlow();
TestCardSetPreviewHotkeys();

Console.WriteLine("MonsterPreviewResilience checks passed.");

static void TestRecommendationModesExposeOnlyCurrentAndTenWin()
{
    var modes = Enum.GetNames<CardSetBuildRecommendationMode>();

    Assert(
        modes.SequenceEqual(["SelectedSet", "FinalBuild"]),
        "Recommendation preview modes should only expose selected set and ten-win build."
    );
}

static void TestRecommendationModeFlow()
{
    Assert(
        CardSetBuildRecommendationModeFlow.GetNext(CardSetBuildRecommendationMode.SelectedSet)
            == CardSetBuildRecommendationMode.FinalBuild,
        "Cycling from selected set should move directly to ten-win build."
    );
    Assert(
        CardSetBuildRecommendationModeFlow.GetNext(CardSetBuildRecommendationMode.FinalBuild)
            == CardSetBuildRecommendationMode.SelectedSet,
        "Cycling from ten-win build should return to selected set."
    );
}

static void TestCardSetPreviewHotkeys()
{
    Assert(
        CardSetPreviewHotkeys.ResolveDisplayMode(
            currentSetPressed: true,
            finalBuildPressed: false
        ) == CardSetBuildRecommendationMode.SelectedSet,
        "A should switch to the current card set."
    );
    Assert(
        CardSetPreviewHotkeys.ResolveDisplayMode(
            currentSetPressed: false,
            finalBuildPressed: true
        ) == CardSetBuildRecommendationMode.FinalBuild,
        "D should switch to the ten-win build."
    );
    Assert(
        CardSetPreviewHotkeys.ResolveDisplayMode(
            currentSetPressed: true,
            finalBuildPressed: true
        ) == null,
        "Pressing A and D together should not switch display modes."
    );
    Assert(
        CardSetPreviewHotkeys.ResolveRecommendationDelta(
            previousCandidatePressed: true,
            nextCandidatePressed: false
        ) == -1,
        "W should move to the previous candidate."
    );
    Assert(
        CardSetPreviewHotkeys.ResolveRecommendationDelta(
            previousCandidatePressed: false,
            nextCandidatePressed: true
        ) == 1,
        "S should move to the next candidate."
    );
    Assert(
        CardSetPreviewHotkeys.ResolveRecommendationDelta(
            previousCandidatePressed: true,
            nextCandidatePressed: true
        ) == 0,
        "Pressing W and S together should not browse candidates."
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
