using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.PreviewSurface;

TestPreviewCardSpecFilter();

Console.WriteLine("MonsterPreviewResilience checks passed.");

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
