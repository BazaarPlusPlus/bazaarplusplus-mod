using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.PreviewSurface;

TestRecommendationModesExposeOnlyCurrentAndTenWin();
TestRecommendationModeFlow();
TestCardSetPreviewHotkeys();
TestPreviewCardSpecFilter();

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

static void TestPreviewCardSpecFilter()
{
    var knownTemplate = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var specs = new List<PreviewCardSpec>
    {
        new()
        {
            TemplateId = knownTemplate.ToString(),
            Tier = 2,
            Size = 2,
            SourceName = "Known",
        },
        new()
        {
            TemplateId = "not-a-guid",
            Tier = 1,
            Size = 1,
            SourceName = "BadGuid",
        },
        new()
        {
            TemplateId = "22222222-2222-2222-2222-222222222222",
            Tier = 3,
            Size = 3,
            SourceName = "Missing",
        },
    };

    var filtered = PreviewCardSpecFilter.Filter(specs, templateId => templateId == knownTemplate);

    Assert(filtered.Count == 1, "Only locally renderable preview specs should remain.");
    Assert(
        filtered[0].TemplateId == knownTemplate.ToString(),
        "Known template should be preserved."
    );
    Assert(filtered[0].SourceName == "Known", "Known spec data should be preserved.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
