using System;
using System.IO;
using BazaarPlusPlus.Game.Lobby;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class MainMenuVersionLabelFormatterTests
{
    [Fact]
    public void Build_AppendsPluginVersionToGameVersion()
    {
        var text = MainMenuVersionLabelFormatter.Build("1.2.3", "1.9.0");

        Assert.Equal(" Version: 1.2.3 | BPP 1.9.0 ", text);
    }

    [Fact]
    public void Build_FallsBackToGameVersion_WhenPluginVersionMissing()
    {
        var text = MainMenuVersionLabelFormatter.Build("1.2.3", "");

        Assert.Equal(" Version: 1.2.3 ", text);
    }

    [Fact]
    public void PatchSource_TargetsVersionShowBuildVersionLabel_AndUsesPluginVersion()
    {
        var sourcePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "Patches",
                "Lobby",
                "MainMenuVersionLabelPatches.cs"
            )
        );

        var source = File.ReadAllText(sourcePath);

        Assert.Contains("VersionShow", source, StringComparison.Ordinal);
        Assert.Contains("BuildVersionLabel", source, StringComparison.Ordinal);
        Assert.Contains("MyPluginInfo.PLUGIN_VERSION", source, StringComparison.Ordinal);
        Assert.Contains("versionLabel", source, StringComparison.Ordinal);
    }
}
