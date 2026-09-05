using BazaarPlusPlus.Game.MusicNotes;
using Xunit;

namespace PureBehavior.Tests;

public sealed class MusicNoteVisualLeaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_refresh_preserves_original_rendering_flag(bool original)
    {
        var target = new Visual { Hidden = original };
        var lease = Create();
        for (var i = 0; i < 3; i++)
        {
            lease.BeginRefresh();
            lease.Suppress(target);
            lease.EndRefresh();
            Assert.True(target.Hidden);
        }
        lease.Restore();
        Assert.Equal(original, target.Hidden);
        lease.Restore();
        Assert.Equal(original, target.Hidden);
    }

    [Fact]
    public void Removed_visual_is_restored_while_remaining_visual_stays_hidden()
    {
        var removed = new Visual();
        var retained = new Visual();
        var lease = Create();
        lease.BeginRefresh();
        lease.Suppress(removed);
        lease.Suppress(retained);
        lease.EndRefresh();
        lease.BeginRefresh();
        lease.Suppress(retained);
        lease.EndRefresh();
        Assert.False(removed.Hidden);
        Assert.True(retained.Hidden);
        lease.Restore();
        Assert.False(retained.Hidden);
    }

    private static MusicNoteVisualLease<Visual> Create() =>
        new(v => v.Hidden, (v, hidden) => v.Hidden = hidden);

    private sealed class Visual
    {
        internal bool Hidden { get; set; }
    }
}
