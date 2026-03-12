using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace BazaarPlusPlus.Tests.MonsterPreview;

public sealed class PreviewBoardSessionTests
{
    [Fact]
    public void Show_and_tick_render_latest_request_state()
    {
        var renderTarget = new RecordingRenderTarget();
        var session = new PreviewBoardSession(renderTarget);
        var model = new PreviewBoardModel { Title = "Hydra", Signature = "hydra:v1" };
        var request = new PreviewBoardRequest
        {
            DataSource = new StaticPreviewDataSource(model),
            AnchorStrategy = new FixedAnchorStrategy(
                new BoardPose { Position = new Vector3(1f, 2f, 3f), Rotation = Quaternion.Euler(0f, 45f, 0f) }
            ),
            Presentation = new PreviewBoardPresentation { Visible = true },
            Debug = new PreviewBoardDebugOptions { Enabled = true, ShowAnchorPoint = true },
        };

        session.Show(request);
        session.Tick();

        Assert.True(renderTarget.Visible);
        Assert.NotNull(renderTarget.LastRenderModel);
        Assert.Equal("Hydra", renderTarget.LastRenderModel!.Data.Title);
        Assert.Equal(new Vector3(1f, 2f, 3f), renderTarget.LastRenderModel.Pose.Position);
        Assert.True(renderTarget.LastRenderModel.Debug.ShowAnchorPoint);
    }

    [Fact]
    public void Hide_turns_off_visibility_without_requiring_new_data()
    {
        var renderTarget = new RecordingRenderTarget();
        var session = new PreviewBoardSession(renderTarget);
        session.Show(
            new PreviewBoardRequest
            {
                DataSource = new StaticPreviewDataSource(new PreviewBoardModel { Title = "Shown" }),
                AnchorStrategy = new FixedAnchorStrategy(new BoardPose()),
                Presentation = new PreviewBoardPresentation { Visible = true },
            }
        );
        session.Tick();

        session.Hide();

        Assert.False(renderTarget.Visible);
    }

    [Fact]
    public void Tick_skips_render_when_data_signature_is_unchanged()
    {
        var renderTarget = new RecordingRenderTarget();
        var session = new PreviewBoardSession(renderTarget);
        var request = new PreviewBoardRequest
        {
            DataSource = new StaticPreviewDataSource(
                new PreviewBoardModel { Title = "Hydra", Signature = "same" }
            ),
            AnchorStrategy = new FixedAnchorStrategy(new BoardPose()),
            Presentation = new PreviewBoardPresentation { Visible = true },
        };

        session.Show(request);
        session.Tick();
        session.Tick();

        Assert.Equal(1, renderTarget.RenderCount);
    }

    [Fact]
    public void Tick_rerenders_when_pose_changes_even_if_data_does_not()
    {
        var renderTarget = new RecordingRenderTarget();
        var anchor = new MutableAnchorStrategy(
            new BoardPose { Position = new Vector3(1f, 0f, 0f), Rotation = Quaternion.identity }
        );
        var session = new PreviewBoardSession(renderTarget);
        session.Show(
            new PreviewBoardRequest
            {
                DataSource = new StaticPreviewDataSource(
                    new PreviewBoardModel { Title = "Hydra", Signature = "same" }
                ),
                AnchorStrategy = anchor,
                Presentation = new PreviewBoardPresentation { Visible = true },
            }
        );

        session.Tick();
        anchor.Pose = new BoardPose { Position = new Vector3(2f, 0f, 0f), Rotation = Quaternion.identity };
        session.Tick();

        Assert.Equal(2, renderTarget.RenderCount);
        Assert.Equal(new Vector3(2f, 0f, 0f), renderTarget.LastRenderModel!.Pose.Position);
    }

    private sealed class RecordingRenderTarget : IBoardRenderTarget
    {
        public bool Visible { get; private set; }

        public BoardRenderModel? LastRenderModel { get; private set; }

        public int RenderCount { get; private set; }

        public void Render(BoardRenderModel renderModel)
        {
            LastRenderModel = renderModel;
            Visible = renderModel.Presentation.Visible;
            RenderCount++;
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
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

    private sealed class MutableAnchorStrategy : IBoardAnchorStrategy
    {
        public BoardPose Pose { get; set; }

        public MutableAnchorStrategy(BoardPose pose)
        {
            Pose = pose;
        }

        public bool TryResolve(out BoardPose? pose)
        {
            pose = Pose;
            return true;
        }
    }
}
