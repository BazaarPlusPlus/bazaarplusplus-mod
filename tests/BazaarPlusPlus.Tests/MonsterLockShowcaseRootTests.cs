using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class MonsterLockShowcaseRootTests
{
    [Fact]
    public void SetVisible_false_hides_root_without_anchor_dependency()
    {
        var root = new MonsterLockShowcaseRoot();

        root.SetVisible(false);

        Assert.False(root.Visible);
    }

    [Fact]
    public void SetVisible_true_marks_root_visible()
    {
        var root = new MonsterLockShowcaseRoot();

        root.SetVisible(true);

        Assert.True(root.Visible);
    }
}
