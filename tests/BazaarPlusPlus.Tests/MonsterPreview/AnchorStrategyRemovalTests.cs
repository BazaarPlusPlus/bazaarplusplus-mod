using System.IO;
using Xunit;

namespace BazaarPlusPlus.Tests.MonsterPreview;

public sealed class AnchorStrategyRemovalTests
{
    [Fact]
    public void Repository_no_longer_references_tracked_transform_anchor_strategy()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var anchorStrategyFile = Path.Combine(
            repositoryRoot,
            "Game/MonsterPreview/Anchor/TrackedTransformAnchorStrategy.cs"
        );
        var anchorStrategyTestsFile = Path.Combine(
            repositoryRoot,
            "tests/BazaarPlusPlus.Tests/MonsterPreview/AnchorStrategyTests.cs"
        );

        Assert.False(File.Exists(anchorStrategyFile));
        var testSource = File.ReadAllText(anchorStrategyTestsFile);
        Assert.DoesNotContain("TrackedTransformAnchorStrategy", testSource);
    }
}
