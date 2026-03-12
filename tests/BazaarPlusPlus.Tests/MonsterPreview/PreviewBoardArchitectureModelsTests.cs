using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace BazaarPlusPlus.Tests.MonsterPreview;

public sealed class PreviewBoardArchitectureModelsTests
{
    [Fact]
    public void PreviewBoardModel_defaults_to_empty_card_lists_and_metadata()
    {
        var model = new PreviewBoardModel();

        Assert.Empty(model.ItemCards);
        Assert.Empty(model.SkillCards);
        Assert.Empty(model.Metadata);
        Assert.Equal(string.Empty, model.Title);
        Assert.Equal(string.Empty, model.Signature);
    }

    [Fact]
    public void PreviewBoardPresentation_defaults_hide_debug_and_use_default_layout_values()
    {
        var presentation = new PreviewBoardPresentation();

        Assert.False(presentation.Visible);
        Assert.False(presentation.DebugEnabled);
        Assert.Equal(Vector3.zero, presentation.LocalOffset);
        Assert.Equal(Vector3.one, presentation.CardScale);
    }

    [Fact]
    public void PreviewBoardDebugOptions_can_toggle_debug_groups_independently()
    {
        var debug = new PreviewBoardDebugOptions
        {
            Enabled = true,
            ShowAnchorPoint = true,
            ShowItemSlots = false,
            ShowSkillSlots = true,
            ShowCardBounds = false,
            ShowLabels = true,
        };

        Assert.True(debug.Enabled);
        Assert.True(debug.ShowAnchorPoint);
        Assert.False(debug.ShowItemSlots);
        Assert.True(debug.ShowSkillSlots);
        Assert.False(debug.ShowCardBounds);
        Assert.True(debug.ShowLabels);
    }

    [Fact]
    public void BoardPose_captures_world_position_and_rotation()
    {
        var pose = new BoardPose
        {
            Position = new Vector3(1f, 2f, 3f),
            Rotation = Quaternion.Euler(0f, 90f, 0f),
        };

        Assert.Equal(new Vector3(1f, 2f, 3f), pose.Position);
        Assert.Equal(Quaternion.Euler(0f, 90f, 0f), pose.Rotation);
    }

    [Fact]
    public void PreviewBoardRequest_can_bundle_data_anchor_and_presentation_state()
    {
        var model = new PreviewBoardModel
        {
            Title = "Pygmalien",
            Signature = "monster:pygmalien",
            ItemCards = new List<PreviewCardSpec> { new() { TemplateId = "item-1" } },
            SkillCards = new List<PreviewCardSpec> { new() { TemplateId = "skill-1" } },
            Metadata = new Dictionary<string, string> { ["source"] = "monster_db" },
        };

        var request = new PreviewBoardRequest
        {
            DataSource = new StubPreviewDataSource(model),
            AnchorStrategy = new FixedAnchorStrategy(new BoardPose()),
            InitialModel = model,
            Presentation = new PreviewBoardPresentation { Visible = true, DebugEnabled = true },
            Debug = new PreviewBoardDebugOptions { Enabled = true, ShowAnchorPoint = true },
            Pose = new BoardPose { Position = new Vector3(4f, 5f, 6f), Rotation = Quaternion.identity },
        };

        Assert.NotNull(request.DataSource);
        Assert.NotNull(request.AnchorStrategy);
        Assert.Same(model, request.InitialModel);
        Assert.True(request.Presentation.Visible);
        Assert.True(request.Presentation.DebugEnabled);
        Assert.True(request.Debug.Enabled);
        Assert.True(request.Debug.ShowAnchorPoint);
        Assert.Equal(new Vector3(4f, 5f, 6f), request.Pose.Position);
    }

    [Fact]
    public void BoardRenderModel_can_hold_data_presentation_debug_and_pose_together()
    {
        var renderModel = new BoardRenderModel
        {
            Data = new PreviewBoardModel { Title = "Hydra" },
            Presentation = new PreviewBoardPresentation { Visible = true },
            Debug = new PreviewBoardDebugOptions { Enabled = true, ShowLabels = true },
            Pose = new BoardPose { Position = new Vector3(7f, 8f, 9f), Rotation = Quaternion.identity },
        };

        Assert.Equal("Hydra", renderModel.Data.Title);
        Assert.True(renderModel.Presentation.Visible);
        Assert.True(renderModel.Debug.Enabled);
        Assert.True(renderModel.Debug.ShowLabels);
        Assert.Equal(new Vector3(7f, 8f, 9f), renderModel.Pose.Position);
    }

    private sealed class StubPreviewDataSource : IPreviewDataSource
    {
        private readonly PreviewBoardModel _model;

        public StubPreviewDataSource(PreviewBoardModel model)
        {
            _model = model;
        }

        public bool TryBuild(out PreviewBoardModel? model)
        {
            model = _model;
            return true;
        }
    }
}
