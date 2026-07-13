using BazaarPlusPlus.Game.VoiceSubtitles;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class VersionLabelScannerTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void Mount_is_delayed_only_when_both_anchor_and_game_font_are_unavailable(
        bool anchorHasChineseCoverage,
        bool gameChineseUiFontReady,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            VersionLabelScanner.ShouldDelayMount(anchorHasChineseCoverage, gameChineseUiFontReady)
        );
    }
}
