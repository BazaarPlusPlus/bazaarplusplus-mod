using System;
using System.IO;
using Xunit;

namespace BazaarPlusPlus.Tests.MonsterPreview;

public sealed class MonsterPreviewDebugControllerSourceTests
{
    [Fact]
    public void Controller_uses_fixed_default_anchor_pose()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var sourceFile = Path.Combine(
            repositoryRoot,
            "Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs"
        );
        var source = File.ReadAllText(sourceFile);

        Assert.Contains("MonsterPreviewDefaults.DefaultAnchorPose.Position", source);
        Assert.Contains("MonsterPreviewDefaults.DefaultAnchorPose.Rotation", source);
        Assert.DoesNotContain("DefaultAnchorPath", source);
        Assert.DoesNotContain("FindDefaultAnchorTransform", source);
    }
}
