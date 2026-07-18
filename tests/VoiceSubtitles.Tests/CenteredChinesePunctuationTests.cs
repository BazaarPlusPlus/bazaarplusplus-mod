#nullable enable

using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.VoiceSubtitles;
using Xunit;

namespace VoiceSubtitles.Tests;

public sealed class CenteredChinesePunctuationTests
{
    [Theory]
    [InlineData("字幕。", "字幕｡")]
    [InlineData("字幕！", "字幕!")]
    [InlineData("字幕？", "字幕?")]
    [InlineData("「字幕。」", "「字幕｡」")]
    [InlineData("『字幕！』", "『字幕!』")]
    public void Centered_line_uses_the_halfwidth_terminal_punctuation(
        string source,
        string expected
    )
    {
        Assert.Equal(
            expected,
            VoiceLineDisplay.ConvertCenteredChineseTrailingPunctuation(
                source,
                SubtitlePosition.TopCenter
            )
        );
    }

    [Theory]
    [InlineData(SubtitlePosition.TopLeft, "字幕。")]
    [InlineData(SubtitlePosition.TopRight, "字幕！")]
    [InlineData(SubtitlePosition.TopCenter, "字幕")]
    [InlineData(SubtitlePosition.TopCenter, "字幕，")]
    [InlineData(SubtitlePosition.TopCenter, "「字幕」")]
    public void Noneligible_line_keeps_its_original_text(SubtitlePosition position, string source)
    {
        Assert.Equal(
            source,
            VoiceLineDisplay.ConvertCenteredChineseTrailingPunctuation(source, position)
        );
    }
}
