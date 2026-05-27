#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel;

internal readonly struct HistoryPanelPreviewTextureSize
{
    public HistoryPanelPreviewTextureSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }
}

internal readonly struct HistoryPanelPreviewBoardPlacement
{
    public HistoryPanelPreviewBoardPlacement(int width, int height, int offsetX, int offsetY, float scale)
    {
        Width = width;
        Height = height;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Scale = scale;
    }

    public int Width { get; }

    public int Height { get; }

    public int OffsetX { get; }

    public int OffsetY { get; }

    public float Scale { get; }
}

internal static class HistoryPanelPreviewTextureGeometry
{
    public const int NativeBoardWidth = 2400;
    public const int NativeBoardHeight = 600;

    private const int MinTextureWidth = NativeBoardWidth;
    private const int MinTextureHeight = NativeBoardHeight;
    private const int MaxTextureWidth = 4096;
    private const int MaxTextureHeight = 2048;

    public static HistoryPanelPreviewTextureSize ResolveTextureSize(int width, int height)
    {
        var clampedWidth = Clamp(width, 256, MaxTextureWidth);
        var clampedHeight = Clamp(height, 128, MaxTextureHeight);
        var scale = System.Math.Max(
            1.0,
            System.Math.Max(
                (double)clampedWidth / NativeBoardWidth,
                (double)clampedHeight / NativeBoardHeight
            )
        );

        return new HistoryPanelPreviewTextureSize(
            Clamp((int)System.Math.Round(NativeBoardWidth * scale), MinTextureWidth, MaxTextureWidth),
            Clamp((int)System.Math.Round(NativeBoardHeight * scale), MinTextureHeight, MaxTextureHeight)
        );
    }

    public static HistoryPanelPreviewBoardPlacement ResolveBoardPlacement(int width, int height)
    {
        var clampedWidth = Clamp(width, 1, MaxTextureWidth);
        var clampedHeight = Clamp(height, 1, MaxTextureHeight);
        var scale = System.Math.Min(
            (double)clampedWidth / NativeBoardWidth,
            (double)clampedHeight / NativeBoardHeight
        );
        if (scale <= 0)
            scale = 1.0;

        var boardWidth = Clamp(
            (int)System.Math.Round(NativeBoardWidth * scale),
            1,
            clampedWidth
        );
        var boardHeight = Clamp(
            (int)System.Math.Round(NativeBoardHeight * scale),
            1,
            clampedHeight
        );

        return new HistoryPanelPreviewBoardPlacement(
            boardWidth,
            boardHeight,
            (clampedWidth - boardWidth) / 2,
            (clampedHeight - boardHeight) / 2,
            (float)scale
        );
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;
        return value > max ? max : value;
    }
}
