using BazaarPlusPlus;

TestPreviewCardSpecFilter();
TestPreviewRenderGenerationGate();

Console.WriteLine("MonsterPreviewResilience checks passed.");

static void TestPreviewCardSpecFilter()
{
    var knownTemplate = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var specs = new List<PreviewCardSpec>
    {
        new() { TemplateId = knownTemplate.ToString(), Tier = 2, Size = 2, SourceName = "Known" },
        new() { TemplateId = "not-a-guid", Tier = 1, Size = 1, SourceName = "BadGuid" },
        new() { TemplateId = "22222222-2222-2222-2222-222222222222", Tier = 3, Size = 3, SourceName = "Missing" },
    };

    var filtered = PreviewCardSpecFilter.Filter(specs, templateId => templateId == knownTemplate);

    Assert(filtered.Count == 1, "Only locally renderable preview specs should remain.");
    Assert(filtered[0].TemplateId == knownTemplate.ToString(), "Known template should be preserved.");
    Assert(filtered[0].SourceName == "Known", "Known spec data should be preserved.");
}

static void TestPreviewRenderGenerationGate()
{
    var gate = new PreviewRenderGenerationGate();

    var firstGeneration = gate.BeginRender(visible: true);
    Assert(!gate.ShouldCancel(firstGeneration), "Active visible render should remain valid.");

    var secondGeneration = gate.BeginRender(visible: true);
    Assert(gate.ShouldCancel(firstGeneration), "Older render generation should be cancelled by a newer render.");
    Assert(!gate.ShouldCancel(secondGeneration), "Newest visible render should remain valid.");

    gate.InvalidateForHide();
    Assert(gate.ShouldCancel(secondGeneration), "Hide should cancel the active render generation.");

    var thirdGeneration = gate.BeginRender(visible: true);
    Assert(!gate.ShouldCancel(thirdGeneration), "A new visible render after hide should become valid again.");

    gate.MarkDisposed();
    Assert(gate.ShouldCancel(thirdGeneration), "Dispose should cancel all generations.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
