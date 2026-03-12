using System;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class MonsterLockShowcaseControllerTests
{
    [Fact]
    public void ShouldShowForLock_returns_false_when_locked_card_is_null()
    {
        var controller = new MonsterLockShowcaseController();

        var result = controller.ShouldShowForLock(null, isShowcaseCard: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldHideForUnlock_returns_false_for_showcase_card()
    {
        var controller = new MonsterLockShowcaseController();

        var result = controller.ShouldHideForUnlock(Guid.NewGuid(), isShowcaseCard: true);

        Assert.False(result);
    }

    [Fact]
    public void ShouldHideForUnlock_returns_true_for_non_showcase_unlock()
    {
        var controller = new MonsterLockShowcaseController();

        var result = controller.ShouldHideForUnlock(null, isShowcaseCard: false);

        Assert.True(result);
    }
}
