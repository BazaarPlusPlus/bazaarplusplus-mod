#pragma warning disable CS0436
using System.Collections.Generic;

namespace BazaarPlusPlus;

internal sealed class MonsterPreviewBoardRenderTarget : IBoardRenderTarget
{
    private MonsterPreviewBoard _board;

    public MonsterPreviewBoardRenderTarget()
        : this(CreateBoard()) { }

    internal MonsterPreviewBoardRenderTarget(MonsterPreviewBoard board)
    {
        _board = board;
        BppLog.Info(
            "MonsterPreviewBoardRenderTarget",
            $"Constructed boardExists={_board != null} boardAlive={_board?.IsAlive ?? false}"
        );
    }

    public void Dispose()
    {
        _board?.Dispose();
        _board = null;
    }

    public void Render(BoardRenderModel renderModel)
    {
        if (!EnsureBoard())
        {
            BppLog.Info("MonsterPreviewBoardRenderTarget", "Render skipped because board could not be created");
            return;
        }

        renderModel ??= new BoardRenderModel();
        BppLog.Info(
            "MonsterPreviewBoardRenderTarget",
            $"Render visible={renderModel.Presentation?.Visible ?? false} items={renderModel.Data?.ItemCards?.Count ?? 0} skills={renderModel.Data?.SkillCards?.Count ?? 0} pose={renderModel.Pose?.Position}"
        );
        _board.SetPresentation(renderModel.Presentation ?? new PreviewBoardPresentation());
        _board.SetDebugOptions(renderModel.Debug ?? new PreviewBoardDebugOptions());
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
        if (!EnsureBoard())
        {
            BppLog.Info("MonsterPreviewBoardRenderTarget", $"SetVisible({visible}) skipped because board could not be created");
            return;
        }

        if (!visible)
            _board.Clear();
        _board.SetVisible(visible);
        BppLog.Info("MonsterPreviewBoardRenderTarget", $"SetVisible visible={visible}");
    }

    private bool EnsureBoard()
    {
        if (_board != null && _board.IsAlive)
            return true;

        BppLog.Warn(
            "MonsterPreviewBoardRenderTarget",
            $"Board missing or dead; recreating boardExists={_board != null} boardAlive={_board?.IsAlive ?? false}"
        );
        _board = CreateBoard();
        return _board != null && _board.IsAlive;
    }

    private static MonsterPreviewBoard CreateBoard()
    {
        var board = new MonsterPreviewBoard(
            "MonsterPreviewBoard",
            new MonsterPreviewItemCardFactory(),
            new MonsterPreviewSkillCardFactory()
        );
        BppLog.Info(
            "MonsterPreviewBoardRenderTarget",
            $"CreateBoard created boardAlive={board?.IsAlive ?? false}"
        );
        return board;
    }
}
