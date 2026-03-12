#pragma warning disable CS0436
namespace BazaarPlusPlus;

internal sealed class BoardView : IBoardRenderTarget
{
    private readonly BoardDebugOverlay _debugOverlay = new BoardDebugOverlay();

    public bool Visible { get; private set; }

    public BoardPose LastPose { get; private set; } = new BoardPose();

    public BoardLayoutSnapshot LastSnapshot { get; private set; } = new BoardLayoutSnapshot();

    public BoardDebugOverlay LastDebugState => _debugOverlay;

    public void Render(BoardRenderModel renderModel)
    {
        renderModel ??= new BoardRenderModel();

        Visible = renderModel.Presentation?.Visible ?? false;
        LastPose = renderModel.Pose ?? new BoardPose();
        LastSnapshot = BoardLayoutSnapshot.Build(renderModel.Data);
        _debugOverlay.Apply(renderModel.Debug);
    }

    public void SetVisible(bool visible)
    {
        Visible = visible;
    }
}
