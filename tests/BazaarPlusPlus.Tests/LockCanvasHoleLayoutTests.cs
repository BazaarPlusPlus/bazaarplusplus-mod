using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class LockCanvasHoleLayoutTests
{
    [Fact]
    public void Calculate_returns_four_blockers_around_center_hole()
    {
        var canvas = new HoleRect(0f, 0f, 1000f, 800f);
        var hole = new HoleRect(300f, 200f, 200f, 100f);

        var layout = LockCanvasHoleLayout.Calculate(canvas, hole);

        Assert.Equal(new HoleRect(0f, 0f, 1000f, 200f), layout.Top);
        Assert.Equal(new HoleRect(0f, 300f, 1000f, 500f), layout.Bottom);
        Assert.Equal(new HoleRect(0f, 200f, 300f, 100f), layout.Left);
        Assert.Equal(new HoleRect(500f, 200f, 500f, 100f), layout.Right);
    }

    [Fact]
    public void Calculate_clamps_hole_to_canvas_bounds()
    {
        var canvas = new HoleRect(0f, 0f, 1000f, 800f);
        var hole = new HoleRect(-50f, 100f, 200f, 900f);

        var layout = LockCanvasHoleLayout.Calculate(canvas, hole);

        Assert.Equal(new HoleRect(0f, 0f, 1000f, 100f), layout.Top);
        Assert.Equal(new HoleRect(0f, 800f, 1000f, 0f), layout.Bottom);
        Assert.Equal(new HoleRect(0f, 100f, 0f, 700f), layout.Left);
        Assert.Equal(new HoleRect(150f, 100f, 850f, 700f), layout.Right);
    }
}
