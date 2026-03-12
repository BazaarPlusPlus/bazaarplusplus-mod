#pragma warning disable CS0436
using System.Collections.Generic;

namespace BazaarPlusPlus;

internal sealed class MonsterPreviewBoardRenderTarget : IBoardRenderTarget
{
    private readonly MonsterPreviewBoard _board;

    public MonsterPreviewBoardRenderTarget()
        : this(
            new MonsterPreviewBoard(
                "MonsterPreviewBoard",
                new MonsterPreviewItemCardFactory(),
                new MonsterPreviewSkillCardFactory()
            )
        ) { }

    internal MonsterPreviewBoardRenderTarget(MonsterPreviewBoard board)
    {
        _board = board;
    }

    public void Dispose()
    {
        _board?.Dispose();
    }

    public void Render(BoardRenderModel renderModel)
    {
        if (_board == null || !_board.IsAlive)
            return;

        renderModel ??= new BoardRenderModel();
        _board.SetPresentation(renderModel.Presentation ?? new PreviewBoardPresentation());
        _board.UpdateAnchor(renderModel.Pose.Position, renderModel.Pose.Rotation);
        _board.SetVisible(renderModel.Presentation.Visible);
        _ = _board.RebuildAsync(
            renderModel.Data?.ItemCards ?? new List<PreviewCardSpec>(),
            renderModel.Data?.SkillCards ?? new List<PreviewCardSpec>(),
            () => false
        );
    }

    public void SetVisible(bool visible)
    {
        if (_board == null || !_board.IsAlive)
            return;

        if (!visible)
            _board.Clear();
        _board.SetVisible(visible);
    }

}
