#nullable enable
using System;

namespace BazaarPlusPlus.Game.Settings;

internal readonly struct BppDockButtonBounds(float minX, float maxX, float minY, float maxY)
{
    internal float MinX { get; } = minX;
    internal float MaxX { get; } = maxX;
    internal float MinY { get; } = minY;
    internal float MaxY { get; } = maxY;
    internal float CenterX => (MinX + MaxX) * 0.5f;
    internal float CenterY => (MinY + MaxY) * 0.5f;
    internal float Width => Math.Max(0f, MaxX - MinX);
    internal float Height => Math.Max(0f, MaxY - MinY);
    internal bool IsValid =>
        float.IsFinite(MinX)
        && float.IsFinite(MaxX)
        && float.IsFinite(MinY)
        && float.IsFinite(MaxY)
        && MaxX >= MinX
        && MaxY >= MinY;

    internal static BppDockButtonBounds FromCenter(
        float centerX,
        float centerY,
        float width,
        float height
    )
    {
        var halfWidth = Math.Max(0f, width) * 0.5f;
        var halfHeight = Math.Max(0f, height) * 0.5f;
        return new BppDockButtonBounds(
            centerX - halfWidth,
            centerX + halfWidth,
            centerY - halfHeight,
            centerY + halfHeight
        );
    }

    internal bool Contains(BppDockButtonBounds other) =>
        IsValid
        && other.IsValid
        && other.MinX >= MinX
        && other.MaxX <= MaxX
        && other.MinY >= MinY
        && other.MaxY <= MaxY;

    internal bool Overlaps(BppDockButtonBounds other) =>
        IsValid
        && other.IsValid
        && MinX < other.MaxX
        && MaxX > other.MinX
        && MinY < other.MaxY
        && MaxY > other.MinY;
}

internal readonly struct BppDockButtonObstacle(
    string name,
    BppDockButtonBounds bounds,
    bool isActive
)
{
    internal string Name { get; } = name;
    internal BppDockButtonBounds Bounds { get; } = bounds;
    internal bool IsActive { get; } = isActive;
}
