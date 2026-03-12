using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace BazaarPlusPlus.Tests.Overlay;

public sealed class MonsterPreviewOverlayCoordinatorTests
{
    [Fact]
    public void Tick_builds_request_from_cards_skills_layout_and_anchor()
    {
        var renderTarget = new RecordingRenderTarget();
        var coordinator = new MonsterPreviewOverlayCoordinator(renderTarget);
        coordinator.SetAnchorStrategy(
            new StubAnchorStrategy(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 15f, 0f))
        );
        coordinator.SetPresentation(
            new PreviewBoardPresentation
            {
                LocalOffset = new Vector3(0f, 2f, 0f),
                CardScale = new Vector3(0.5f, 0.5f, 0.5f),
                CardSpacing = new Vector3(1.25f, 0f, 0f),
                BoardSize = new Vector2(9f, 3f),
                BoardThickness = 0.1f,
                BorderThickness = 0.2f,
                BorderHeight = 0.3f,
            }
        );
        coordinator.SetCards(new List<PreviewCardSpec> { new() { TemplateId = "item-a" } });
        coordinator.SetSkillCards(new List<PreviewCardSpec> { new() { TemplateId = "skill-a" } });
        coordinator.SetVisible(true);

        coordinator.Tick();

        Assert.True(renderTarget.Visible);
        Assert.NotNull(renderTarget.LastRenderModel);
        Assert.Single(renderTarget.LastRenderModel!.Data.ItemCards);
        Assert.Single(renderTarget.LastRenderModel.Data.SkillCards);
        Assert.Equal(new Vector3(3f, 4f, 5f), renderTarget.LastRenderModel.Pose.Position);
        Assert.Equal(new Vector3(0f, 2f, 0f), renderTarget.LastRenderModel.Presentation.LocalOffset);
        Assert.Equal(new Vector2(9f, 3f), renderTarget.LastRenderModel.Presentation.BoardSize);
    }

    [Fact]
    public void ClearCards_removes_payload_before_next_tick()
    {
        var renderTarget = new RecordingRenderTarget();
        var coordinator = new MonsterPreviewOverlayCoordinator(renderTarget);
        coordinator.SetAnchorStrategy(new StubAnchorStrategy(Vector3.zero, Quaternion.identity));
        coordinator.SetCards(new List<PreviewCardSpec> { new() { TemplateId = "item-a" } });
        coordinator.SetVisible(true);
        coordinator.Tick();

        coordinator.ClearCards();
        coordinator.Refresh();
        coordinator.Tick();

        Assert.Empty(renderTarget.LastRenderModel!.Data.ItemCards);
        Assert.Empty(renderTarget.LastRenderModel.Data.SkillCards);
    }

    [Fact]
    public void SetVisible_false_hides_render_target()
    {
        var renderTarget = new RecordingRenderTarget();
        var coordinator = new MonsterPreviewOverlayCoordinator(renderTarget);
        coordinator.SetAnchorStrategy(new StubAnchorStrategy(Vector3.zero, Quaternion.identity));
        coordinator.SetVisible(true);
        coordinator.Tick();

        coordinator.SetVisible(false);

        Assert.False(renderTarget.Visible);
    }

    [Fact]
    public void SetDebugOptions_flows_independent_debug_groups_into_render_request()
    {
        var renderTarget = new RecordingRenderTarget();
        var coordinator = new MonsterPreviewOverlayCoordinator(renderTarget);
        coordinator.SetAnchorStrategy(new StubAnchorStrategy(Vector3.zero, Quaternion.identity));
        coordinator.SetDebugOptions(
            new PreviewBoardDebugOptions
            {
                Enabled = true,
                ShowAnchorPoint = true,
                ShowItemSlots = false,
                ShowSkillSlots = true,
                ShowCardBounds = true,
                ShowLabels = false,
            }
        );
        coordinator.SetVisible(true);

        coordinator.Tick();

        Assert.True(renderTarget.LastRenderModel!.Debug.Enabled);
        Assert.True(renderTarget.LastRenderModel.Debug.ShowAnchorPoint);
        Assert.False(renderTarget.LastRenderModel.Debug.ShowItemSlots);
        Assert.True(renderTarget.LastRenderModel.Debug.ShowSkillSlots);
        Assert.True(renderTarget.LastRenderModel.Debug.ShowCardBounds);
        Assert.False(renderTarget.LastRenderModel.Debug.ShowLabels);
    }

    [Fact]
    public void ShowRequest_renders_external_request_without_using_incremental_setters()
    {
        var renderTarget = new RecordingRenderTarget();
        var coordinator = new MonsterPreviewOverlayCoordinator(renderTarget);
        var request = new PreviewBoardRequest
        {
            DataSource = new StaticPreviewDataSource(
                new PreviewBoardModel
                {
                    Title = "Encounter",
                    ItemCards = new List<PreviewCardSpec> { new() { TemplateId = "item-a" } },
                    SkillCards = new List<PreviewCardSpec> { new() { TemplateId = "skill-a" } },
                    Signature = "encounter:v1",
                }
            ),
            AnchorStrategy = new FixedAnchorStrategy(
                new BoardPose { Position = new Vector3(8f, 9f, 10f), Rotation = Quaternion.identity }
            ),
            Presentation = new PreviewBoardPresentation { Visible = true, LocalOffset = new Vector3(0f, 1f, 0f) },
            Debug = new PreviewBoardDebugOptions { Enabled = true, ShowAnchorPoint = true },
        };

        coordinator.ShowRequest(request);
        coordinator.Tick();

        Assert.True(renderTarget.Visible);
        Assert.Equal("Encounter", renderTarget.LastRenderModel!.Data.Title);
        Assert.Single(renderTarget.LastRenderModel.Data.ItemCards);
        Assert.Single(renderTarget.LastRenderModel.Data.SkillCards);
        Assert.Equal(new Vector3(8f, 9f, 10f), renderTarget.LastRenderModel.Pose.Position);
        Assert.True(renderTarget.LastRenderModel.Debug.ShowAnchorPoint);
    }

    private sealed class RecordingRenderTarget : IBoardRenderTarget
    {
        public bool Visible { get; private set; }
        public BoardRenderModel? LastRenderModel { get; private set; }

        public void Render(BoardRenderModel renderModel)
        {
            LastRenderModel = renderModel;
            Visible = renderModel.Presentation.Visible;
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
        }
    }

    private sealed class StubAnchorStrategy : IBoardAnchorStrategy
    {
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;

        public StubAnchorStrategy(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
        }

        public bool TryResolve(out BoardPose pose)
        {
            pose = new BoardPose
            {
                Position = _position,
                Rotation = _rotation,
            };
            return true;
        }
    }

    private sealed class StaticPreviewDataSource : IPreviewDataSource
    {
        private readonly PreviewBoardModel _model;

        public StaticPreviewDataSource(PreviewBoardModel model)
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
