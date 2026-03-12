using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace BazaarPlusPlus.Tests.Overlay;

public sealed class BoardViewTests
{
    [Fact]
    public void Render_updates_visibility_pose_and_layout_snapshot()
    {
        var view = new BoardView();
        var renderModel = new BoardRenderModel
        {
            Data = new PreviewBoardModel
            {
                ItemCards = new List<PreviewCardSpec>
                {
                    new() { TemplateId = "item-a", Size = 1 },
                    new() { TemplateId = "item-b", Size = 2 },
                },
                SkillCards = new List<PreviewCardSpec>
                {
                    new() { TemplateId = "skill-a" },
                    new() { TemplateId = "skill-b" },
                },
            },
            Presentation = new PreviewBoardPresentation
            {
                Visible = true,
                LocalOffset = new Vector3(0f, 1f, 0f),
                CardScale = new Vector3(0.5f, 0.5f, 0.5f),
            },
            Debug = new PreviewBoardDebugOptions
            {
                Enabled = true,
                ShowAnchorPoint = true,
                ShowItemSlots = true,
                ShowSkillSlots = false,
            },
            Pose = new BoardPose
            {
                Position = new Vector3(4f, 5f, 6f),
                Rotation = Quaternion.Euler(0f, 90f, 0f),
            },
        };

        view.Render(renderModel);

        Assert.True(view.Visible);
        Assert.Equal(new Vector3(4f, 5f, 6f), view.LastPose.Position);
        Assert.Equal(2, view.LastSnapshot.ItemSlots.Count);
        Assert.Equal(2, view.LastSnapshot.SkillSlots.Count);
        Assert.True(view.LastDebugState.ShowAnchorPoint);
        Assert.True(view.LastDebugState.ShowItemSlots);
        Assert.False(view.LastDebugState.ShowSkillSlots);
    }

    [Fact]
    public void SetVisible_false_hides_view_without_resetting_snapshot()
    {
        var view = new BoardView();
        view.Render(
            new BoardRenderModel
            {
                Data = new PreviewBoardModel
                {
                    ItemCards = new List<PreviewCardSpec> { new() { TemplateId = "item-a", Size = 1 } },
                },
                Presentation = new PreviewBoardPresentation { Visible = true },
            }
        );

        view.SetVisible(false);

        Assert.False(view.Visible);
        Assert.Single(view.LastSnapshot.ItemSlots);
    }

    [Fact]
    public void Debug_groups_follow_debug_options_independently()
    {
        var view = new BoardView();
        view.Render(
            new BoardRenderModel
            {
                Data = new PreviewBoardModel
                {
                    ItemCards = new List<PreviewCardSpec> { new() { TemplateId = "item-a", Size = 1 } },
                    SkillCards = new List<PreviewCardSpec> { new() { TemplateId = "skill-a" } },
                },
                Presentation = new PreviewBoardPresentation { Visible = true },
                Debug = new PreviewBoardDebugOptions
                {
                    Enabled = true,
                    ShowAnchorPoint = false,
                    ShowItemSlots = true,
                    ShowSkillSlots = true,
                    ShowCardBounds = false,
                    ShowLabels = true,
                },
            }
        );

        Assert.False(view.LastDebugState.ShowAnchorPoint);
        Assert.True(view.LastDebugState.ShowItemSlots);
        Assert.True(view.LastDebugState.ShowSkillSlots);
        Assert.False(view.LastDebugState.ShowCardBounds);
        Assert.True(view.LastDebugState.ShowLabels);
    }

    [Fact]
    public void Layout_snapshot_centers_items_by_card_span_and_skills_by_count()
    {
        var snapshot = BoardLayoutSnapshot.Build(
            new PreviewBoardModel
            {
                ItemCards = new List<PreviewCardSpec>
                {
                    new() { TemplateId = "a", Size = 2 },
                    new() { TemplateId = "b", Size = 1 },
                },
                SkillCards = new List<PreviewCardSpec>
                {
                    new() { TemplateId = "s1" },
                    new() { TemplateId = "s2" },
                    new() { TemplateId = "s3" },
                },
            }
        );

        Assert.Equal(2, snapshot.ItemSlots.Count);
        Assert.Equal(3, snapshot.SkillSlots.Count);
        Assert.True(snapshot.ItemSlots[0].Center.x < snapshot.ItemSlots[1].Center.x);
        Assert.True(snapshot.SkillSlots[0].Center.x < snapshot.SkillSlots[1].Center.x);
        Assert.True(snapshot.SkillSlots[1].Center.x < snapshot.SkillSlots[2].Center.x);
    }
}
