#pragma warning disable CS0436
namespace BazaarPlusPlus;

internal sealed class PreviewBoardSession
{
    private readonly IBoardRenderTarget _renderTarget;

    private PreviewBoardRequest _request;
    private string _lastSignature = string.Empty;
    private BoardPose _lastPose;

    public PreviewBoardSession(IBoardRenderTarget renderTarget)
    {
        _renderTarget = renderTarget;
    }

    public void Show(PreviewBoardRequest request)
    {
        _request = request;
        BppLog.Info(
            "PreviewBoardSession",
            $"Show request received hasDataSource={request?.DataSource != null} hasAnchor={request?.AnchorStrategy != null} visible={request?.Presentation?.Visible ?? false}"
        );
    }

    public void Hide()
    {
        _renderTarget.SetVisible(false);
        _request = null;
        _lastSignature = string.Empty;
        _lastPose = null;
        BppLog.Info("PreviewBoardSession", "Hide called; cleared cached signature and pose");
    }

    public void Tick()
    {
        if (_request == null)
            return;

        var model = ResolveModel(_request);
        var pose = ResolvePose(_request);
        if (model == null || pose == null)
        {
            BppLog.Info(
                "PreviewBoardSession",
                $"Tick skipped modelNull={model == null} poseNull={pose == null}"
            );
            return;
        }

        var signature = string.IsNullOrWhiteSpace(model.Signature)
            ? PreviewBoardSignature.Build(model)
            : model.Signature;
        if (!ShouldRender(signature, pose))
        {
            BppLog.Info("PreviewBoardSession", "Tick skipped because signature and pose are unchanged");
            return;
        }

        model.Signature = signature;
        var renderModel = new BoardRenderModel
        {
            Data = model,
            Debug = _request.Debug,
            Pose = pose,
            Presentation = _request.Presentation,
        };
        _renderTarget.Render(renderModel);
        _lastSignature = signature;
        _lastPose = ClonePose(pose);
        BppLog.Info(
            "PreviewBoardSession",
            $"Rendered signature={signature} pose={pose.Position} items={model.ItemCards?.Count ?? 0} skills={model.SkillCards?.Count ?? 0}"
        );
    }

    private static PreviewBoardModel ResolveModel(PreviewBoardRequest request)
    {
        if (request.DataSource != null && request.DataSource.TryBuild(out var model) && model != null)
            return model;

        return request.InitialModel;
    }

    private static BoardPose ResolvePose(PreviewBoardRequest request)
    {
        if (request.AnchorStrategy != null && request.AnchorStrategy.TryResolve(out var pose) && pose != null)
            return pose;

        return request.Pose;
    }

    private bool ShouldRender(string signature, BoardPose pose)
    {
        return signature != _lastSignature || !SamePose(_lastPose, pose);
    }

    private static bool SamePose(BoardPose left, BoardPose right)
    {
        if (left == null)
            return false;

        return left.Position == right.Position && left.Rotation == right.Rotation;
    }

    private static BoardPose ClonePose(BoardPose pose)
    {
        return new BoardPose
        {
            Position = pose.Position,
            Rotation = pose.Rotation,
        };
    }
}
