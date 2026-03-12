using System.Collections.Generic;
using Xunit;

namespace BazaarPlusPlus.Tests.Overlay;

public sealed class PreviewDataSourceTests
{
    [Fact]
    public void CompositePreviewDataSource_returns_first_successful_model()
    {
        var expected = new PreviewBoardModel { Title = "Chosen", Signature = "chosen" };
        var source = new CompositePreviewDataSource(
            new StubPreviewDataSource(false, new PreviewBoardModel { Title = "Ignored" }),
            new StubPreviewDataSource(true, expected),
            new StubPreviewDataSource(true, new PreviewBoardModel { Title = "Later" })
        );

        var result = source.TryBuild(out var model);

        Assert.True(result);
        Assert.Same(expected, model);
    }

    [Fact]
    public void CompositePreviewDataSource_returns_false_when_all_sources_fail()
    {
        var source = new CompositePreviewDataSource(
            new StubPreviewDataSource(false, new PreviewBoardModel()),
            new StubPreviewDataSource(false, new PreviewBoardModel())
        );

        var result = source.TryBuild(out var model);

        Assert.False(result);
        Assert.Null(model);
    }

    [Fact]
    public void PreviewBoardSignature_builds_stable_value_for_identical_models()
    {
        var first = new PreviewBoardModel
        {
            Title = "Hydra",
            ItemCards = new List<PreviewCardSpec>
            {
                new() { TemplateId = "a", Tier = 1, Size = 2, Enchant = "None", Attributes = new Dictionary<int, int> { [1] = 2, [3] = 4 } },
            },
            SkillCards = new List<PreviewCardSpec>
            {
                new() { TemplateId = "s", Tier = 2, Size = 1, Enchant = "Burning", Attributes = new Dictionary<int, int> { [7] = 8 } },
            },
            Metadata = new Dictionary<string, string> { ["source"] = "monster_db", ["encounter"] = "hydra" },
        };

        var second = new PreviewBoardModel
        {
            Title = "Hydra",
            ItemCards = new List<PreviewCardSpec>
            {
                new() { TemplateId = "a", Tier = 1, Size = 2, Enchant = "None", Attributes = new Dictionary<int, int> { [3] = 4, [1] = 2 } },
            },
            SkillCards = new List<PreviewCardSpec>
            {
                new() { TemplateId = "s", Tier = 2, Size = 1, Enchant = "Burning", Attributes = new Dictionary<int, int> { [7] = 8 } },
            },
            Metadata = new Dictionary<string, string> { ["encounter"] = "hydra", ["source"] = "monster_db" },
        };

        var firstSignature = PreviewBoardSignature.Build(first);
        var secondSignature = PreviewBoardSignature.Build(second);

        Assert.Equal(firstSignature, secondSignature);
    }

    [Fact]
    public void PreviewBoardSignature_changes_when_card_payload_changes()
    {
        var first = new PreviewBoardModel
        {
            ItemCards = new List<PreviewCardSpec> { new() { TemplateId = "a", Tier = 1 } },
        };
        var second = new PreviewBoardModel
        {
            ItemCards = new List<PreviewCardSpec> { new() { TemplateId = "a", Tier = 2 } },
        };

        Assert.NotEqual(PreviewBoardSignature.Build(first), PreviewBoardSignature.Build(second));
    }

    private sealed class StubPreviewDataSource : IPreviewDataSource
    {
        private readonly bool _succeeds;
        private readonly PreviewBoardModel _model;

        public StubPreviewDataSource(bool succeeds, PreviewBoardModel model)
        {
            _succeeds = succeeds;
            _model = model;
        }

        public bool TryBuild(out PreviewBoardModel? model)
        {
            model = _succeeds ? _model : null;
            return _succeeds;
        }
    }
}
