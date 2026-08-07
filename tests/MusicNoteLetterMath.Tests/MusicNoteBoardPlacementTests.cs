using BazaarPlusPlus.Game.MusicNotes;
using Xunit;

namespace MusicNoteLetterMathTests;

public class MusicNoteBoardPlacementTests
{
    private static bool[] Board(string layout)
    {
        // 'x' = blocked, '.' = free
        var blocked = new bool[layout.Length];
        for (var i = 0; i < layout.Length; i++)
            blocked[i] = layout[i] == 'x';
        return blocked;
    }

    [Fact]
    public void SmallItem_FreeSocket_CanCover()
    {
        Assert.True(MusicNoteBoardPlacement.CanCoverSocket(Board(".........."), 3, 1));
    }

    [Fact]
    public void SmallItem_BlockedSocket_CannotCover()
    {
        Assert.False(MusicNoteBoardPlacement.CanCoverSocket(Board("...x......"), 3, 1));
    }

    [Fact]
    public void MediumItem_AnyCoveringAnchorFree_CanCover()
    {
        // Target 3: anchors 2 (covers 2,3) and 3 (covers 3,4). Socket 2 blocked, 3-4 free.
        Assert.True(MusicNoteBoardPlacement.CanCoverSocket(Board("..x......."), 3, 2));
    }

    [Fact]
    public void MediumItem_AllCoveringAnchorsBlocked_CannotCover()
    {
        // Target 3: anchor 2 needs 2+3, anchor 3 needs 3+4. Sockets 2 and 4 blocked.
        Assert.False(MusicNoteBoardPlacement.CanCoverSocket(Board("..x.x....."), 3, 2));
    }

    [Fact]
    public void LargeItem_NeedsFullSpanSomewhereOverTarget()
    {
        // Target 5, size 3: anchors 3,4,5. Blocked at 4 kills anchors 3/4; anchor 5 needs 5,6,7.
        Assert.True(MusicNoteBoardPlacement.CanCoverSocket(Board("....x....."), 5, 3));
        // Also block 7: anchor 5 dies too.
        Assert.False(MusicNoteBoardPlacement.CanCoverSocket(Board("....x..x.."), 5, 3));
    }

    [Fact]
    public void EdgeSockets_ClampAnchorRange()
    {
        // Target 0, size 3: only anchor 0 (covers 0,1,2).
        Assert.True(MusicNoteBoardPlacement.CanCoverSocket(Board(".........."), 0, 3));
        Assert.False(MusicNoteBoardPlacement.CanCoverSocket(Board(".x........"), 0, 3));
        // Target 9 (last), size 2: only anchor 8.
        Assert.True(MusicNoteBoardPlacement.CanCoverSocket(Board(".........."), 9, 2));
        Assert.False(MusicNoteBoardPlacement.CanCoverSocket(Board("........x."), 9, 2));
    }

    [Fact]
    public void DegenerateInputs_ReturnFalse()
    {
        Assert.False(MusicNoteBoardPlacement.CanCoverSocket(Board(".........."), -1, 1));
        Assert.False(MusicNoteBoardPlacement.CanCoverSocket(Board(".........."), 10, 1));
        Assert.False(MusicNoteBoardPlacement.CanCoverSocket(Board(".........."), 3, 0));
        Assert.False(MusicNoteBoardPlacement.CanCoverSocket(Board(".."), 1, 3));
    }
}
