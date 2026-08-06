using BazaarPlusPlus.Game.MusicNotes;
using Xunit;

namespace MusicNoteLetterMathTests;

public class MusicNoteLetterMathTests
{
    private const int SocketCount = 10;

    [Fact]
    public void NoAnchor_EverySocketUndetermined()
    {
        var result = MusicNoteLetterMath.InferLetters(new int?[SocketCount]);

        Assert.Equal(SocketCount, result.Length);
        Assert.All(result, letter => Assert.Null(letter));
    }

    [Fact]
    public void AnchorAtFirstSocket_LettersCycleFromAnchor()
    {
        var placed = new int?[SocketCount];
        placed[0] = 0; // A on socket 0

        var result = MusicNoteLetterMath.InferLetters(placed);

        Assert.Equal(new int?[] { 0, 1, 2, 3, 4, 5, 6, 0, 1, 2 }, result);
    }

    [Fact]
    public void AnchorMidBoard_NegativeOffsetsWrapAround()
    {
        var placed = new int?[SocketCount];
        placed[3] = 1; // B on socket 3

        var result = MusicNoteLetterMath.InferLetters(placed);

        // Socket 0 sits three letters before B: B(1) - 3 wraps to F(5).
        Assert.Equal(new int?[] { 5, 6, 0, 1, 2, 3, 4, 5, 6, 0 }, result);
    }

    [Fact]
    public void PlacedLetterAlwaysWinsOverInference()
    {
        var placed = new int?[SocketCount];
        placed[0] = 0; // A on socket 0 (anchor)
        placed[5] = 3; // D on socket 5, diverging from the inferred F(5)

        var result = MusicNoteLetterMath.InferLetters(placed);

        Assert.Equal(3, result[5]);
        Assert.Equal(0, result[0]);
        Assert.Equal(6, result[6]);
    }

    [Fact]
    public void EmptyBoard_ReturnsEmpty()
    {
        var result = MusicNoteLetterMath.InferLetters(Array.Empty<int?>());

        Assert.Empty(result);
    }
}
