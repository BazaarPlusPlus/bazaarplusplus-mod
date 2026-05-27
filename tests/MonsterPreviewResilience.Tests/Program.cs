using BazaarPlusPlus.Game.CardSetPreview;

TestRecommendationModesExposeOnlyCurrentAndTenWin();
TestRecommendationModeFlow();
TestCardSetPreviewHotkeys();
TestCardSetPreviewSponsorText();
TestCardSetPreviewModeStatusText();

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

static void TestCardSetPreviewSponsorText()
{
    Assert(
        CardSetPreviewSponsorTextFormatter.FormatSupportedBy("Alice", "en")
            == "Supported by Alice",
        "English sponsor text should preserve the existing Supported by wording."
    );
    Assert(
        CardSetPreviewSponsorTextFormatter.FormatSupportedBy("Alice", "zh-CN")
            == "由 Alice 支持",
        "Chinese sponsor text should preserve the existing localized wording."
    );
    Assert(
        CardSetPreviewSponsorTextFormatter.FormatSupportedBy(" Alice ", "zh-Hant")
            == "由 Alice 支持",
        "Sponsor text should trim names before formatting."
    );
    Assert(
        CardSetPreviewSponsorTextFormatter.FormatSupportedBy(" ", "zh-CN") == string.Empty,
        "Blank sponsor names should not produce a visible sponsor label."
    );
}

static void TestCardSetPreviewModeStatusText()
{
    Assert(
        CardSetPreviewModeStatusText.Build("当前卡组", "zh-CN")
            == "卡组选择模式：点击物品加入/移除，CapsLock 退出 | 当前卡组",
        "Chinese mode status should explain the active stage and how to exit."
    );
    Assert(
        CardSetPreviewModeStatusText.Build("Selected Set", "en")
            == "Card Set Selection: click items to add/remove, CapsLock to exit | Selected Set",
        "English mode status should explain the active stage and how to exit."
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
