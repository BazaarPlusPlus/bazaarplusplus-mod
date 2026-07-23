#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.ReportAssets;

internal readonly record struct ReportAssetPixelBounds(int X, int Y, int Width, int Height);

internal static class ReportAssetAlphaCrop
{
    internal const byte DefaultAlphaThreshold = 8;

    internal static bool TryResolveBounds(
        ReadOnlySpan<byte> rgbaPixels,
        int width,
        int height,
        int padding,
        out ReportAssetPixelBounds bounds,
        byte alphaThreshold = DefaultAlphaThreshold
    )
    {
        ValidateInput(rgbaPixels.Length, width, height, padding);

        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (rgbaPixels[(y * width + x) * 4 + 3] < alphaThreshold)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            bounds = default;
            return false;
        }

        minX = Math.Max(0, minX - padding);
        minY = Math.Max(0, minY - padding);
        maxX = Math.Min(width - 1, maxX + padding);
        maxY = Math.Min(height - 1, maxY + padding);
        bounds = new ReportAssetPixelBounds(
            minX,
            minY,
            checked(maxX - minX + 1),
            checked(maxY - minY + 1)
        );
        return true;
    }

    internal static byte[] Crop(
        ReadOnlySpan<byte> rgbaPixels,
        int width,
        int height,
        ReportAssetPixelBounds bounds
    )
    {
        ValidateInput(rgbaPixels.Length, width, height, padding: 0);
        if (
            bounds.X < 0
            || bounds.Y < 0
            || bounds.Width <= 0
            || bounds.Height <= 0
            || bounds.X + bounds.Width > width
            || bounds.Y + bounds.Height > height
        )
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }

        if (bounds is { X: 0, Y: 0 } && bounds.Width == width && bounds.Height == height)
            return rgbaPixels.ToArray();

        var destinationStride = checked(bounds.Width * 4);
        var sourceStride = checked(width * 4);
        var destination = new byte[checked(destinationStride * bounds.Height)];
        for (var row = 0; row < bounds.Height; row++)
        {
            rgbaPixels
                .Slice(checked((bounds.Y + row) * sourceStride + bounds.X * 4), destinationStride)
                .CopyTo(destination.AsSpan(row * destinationStride, destinationStride));
        }
        return destination;
    }

    private static void ValidateInput(int byteLength, int width, int height, int padding)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (padding < 0)
            throw new ArgumentOutOfRangeException(nameof(padding));
        if (byteLength != checked(width * height * 4))
            throw new InvalidDataException("RGBA pixel byte length is invalid.");
    }
}
