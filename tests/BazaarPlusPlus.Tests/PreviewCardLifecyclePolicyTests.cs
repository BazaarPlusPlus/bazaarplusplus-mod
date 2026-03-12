using BazaarPlusPlus;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class PreviewCardLifecyclePolicyTests
{
    [Fact]
    public void ShouldReturnToPool_ReturnsTrueForItemPreviewCards()
    {
        Assert.True(PreviewCardLifecyclePolicy.ShouldReturnToPool(PreviewCardKind.Item));
    }

    [Fact]
    public void ShouldReturnToPool_ReturnsTrueForSkillPreviewCards()
    {
        Assert.True(PreviewCardLifecyclePolicy.ShouldReturnToPool(PreviewCardKind.Skill));
    }

    [Fact]
    public void ShouldRefreshAfterInstantiate_ReturnsFalseForItemPreviewCards()
    {
        Assert.False(PreviewCardLifecyclePolicy.ShouldRefreshAfterInstantiate(PreviewCardKind.Item));
    }

    [Fact]
    public void ShouldRefreshAfterInstantiate_ReturnsTrueForSkillPreviewCards()
    {
        Assert.True(PreviewCardLifecyclePolicy.ShouldRefreshAfterInstantiate(PreviewCardKind.Skill));
    }
}
