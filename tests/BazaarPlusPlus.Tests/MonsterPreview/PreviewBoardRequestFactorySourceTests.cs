using System;
using System.IO;
using Xunit;

namespace BazaarPlusPlus.Tests.MonsterPreview;

public sealed class PreviewBoardRequestFactorySourceTests
{
    [Fact]
    public void Factory_does_not_expose_duplicate_showcase_presentation_helper()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var sourceFile = Path.Combine(
            repositoryRoot,
            "Game/MonsterPreview/Architecture/PreviewBoardRequestFactory.cs"
        );
        var source = File.ReadAllText(sourceFile);

        Assert.DoesNotContain("public static PreviewBoardPresentation CreateShowcasePresentation()", source);
    }
}
