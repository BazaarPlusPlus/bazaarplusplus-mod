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
}
