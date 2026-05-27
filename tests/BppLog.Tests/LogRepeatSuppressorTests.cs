using System.Collections.Generic;
using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class LogRepeatSuppressorTests
{
    private const int Info = 0;
    private const int Warning = 1;
    private const int Error = 2;

    private static (
        LogRepeatSuppressor Suppressor,
        List<(int Level, string Message)> Output
    ) CreateSuppressor(int maxPatternLength = 3)
    {
        var output = new List<(int, string)>();
        var suppressor = new LogRepeatSuppressor(
            writeSink: (lvl, msg) => output.Add((lvl, msg)),
            formatSummary: (text, _) => $"[BPP][Logger] {text}",
            maxPatternLength: maxPatternLength
        );
        return (suppressor, output);
    }

    [Fact]
    public void Write_PassesThroughDistinctMessages()
    {
        var (sut, output) = CreateSuppressor();

        sut.Write(Info, "a");
        sut.Write(Info, "b");
        sut.Write(Info, "c");

        Assert.Equal(new[] { (Info, "a"), (Info, "b"), (Info, "c") }, output);
    }

    [Fact]
    public void Write_SuppressesIdenticalRepeats_UntilFlush()
    {
        var (sut, output) = CreateSuppressor();

        sut.Write(Info, "ping");
        sut.Write(Info, "ping");
        sut.Write(Info, "ping");
        sut.Write(Info, "ping");

        Assert.Single(output);
        Assert.Equal((Info, "ping"), output[0]);

        sut.Flush();

        Assert.Equal(2, output.Count);
        Assert.Equal(Info, output[1].Level);
        Assert.Contains("repeated 3 additional time(s)", output[1].Message);
    }

    [Fact]
    public void Write_BreaksSuppressionWhenMessageChanges_EmitsSummaryThenNewLine()
    {
        var (sut, output) = CreateSuppressor();

        sut.Write(Info, "ping");
        sut.Write(Info, "ping");
        sut.Write(Info, "ping");
        sut.Write(Info, "pong");

        Assert.Equal(3, output.Count);
        Assert.Equal((Info, "ping"), output[0]);
        Assert.Contains("repeated 2 additional time(s)", output[1].Message);
        Assert.Equal((Info, "pong"), output[2]);
    }

    [Fact]
    public void Write_DetectsMultiLineRepeatedSequence()
    {
        var (sut, output) = CreateSuppressor();

        sut.Write(Info, "a");
        sut.Write(Info, "b");
        sut.Write(Info, "a");
        sut.Write(Info, "b");
        sut.Write(Info, "a");
        sut.Write(Info, "b");

        Assert.Equal(2, output.Count);
        Assert.Equal((Info, "a"), output[0]);
        Assert.Equal((Info, "b"), output[1]);

        sut.Flush();

        Assert.Equal(3, output.Count);
        Assert.Contains("2-message sequence", output[2].Message);
        Assert.Contains("repeated 2 additional time(s)", output[2].Message);
    }

    [Fact]
    public void Write_DistinguishesByLevel()
    {
        var (sut, output) = CreateSuppressor();

        sut.Write(Info, "msg");
        sut.Write(Warning, "msg");

        Assert.Equal(2, output.Count);
        Assert.Equal((Info, "msg"), output[0]);
        Assert.Equal((Warning, "msg"), output[1]);
    }

    [Fact]
    public void Write_SummaryUsesLevelOfFirstSuppressedEntry()
    {
        var (sut, output) = CreateSuppressor();

        sut.Write(Error, "boom");
        sut.Write(Error, "boom");
        sut.Write(Error, "boom");

        sut.Flush();

        Assert.Equal(2, output.Count);
        Assert.Equal(Error, output[1].Level);
    }

    [Fact]
    public void Write_PartialSequenceMatch_FlushesBufferOnDivergence()
    {
        var (sut, output) = CreateSuppressor();

        sut.Write(Info, "a");
        sut.Write(Info, "b");
        sut.Write(Info, "a");
        sut.Write(Info, "b");
        sut.Write(Info, "a");
        sut.Write(Info, "c");

        Assert.Contains((Info, "c"), output);
        Assert.Contains((Info, "a"), output);
    }

    [Fact]
    public void Flush_OnEmptySuppressor_ProducesNoOutput()
    {
        var (sut, output) = CreateSuppressor();

        sut.Flush();

        Assert.Empty(output);
    }

    [Fact]
    public void Flush_AfterDistinctMessages_ProducesNoExtraOutput()
    {
        var (sut, output) = CreateSuppressor();

        sut.Write(Info, "a");
        sut.Write(Info, "b");

        sut.Flush();

        Assert.Equal(2, output.Count);
    }

    [Fact]
    public void Write_MaxPatternLengthBoundsLookback()
    {
        var (sut, output) = CreateSuppressor(maxPatternLength: 2);

        sut.Write(Info, "a");
        sut.Write(Info, "b");
        sut.Write(Info, "c");
        sut.Write(Info, "a");
        sut.Write(Info, "b");
        sut.Write(Info, "c");

        sut.Flush();

        Assert.Contains((Info, "a"), output);
        Assert.Contains((Info, "b"), output);
        Assert.Contains((Info, "c"), output);
    }
}
