#pragma warning disable CS0436
#nullable enable
using System;

namespace BazaarPlusPlus;

internal readonly struct HoleRect : IEquatable<HoleRect>
{
    public HoleRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public bool Equals(HoleRect other)
    {
        return X.Equals(other.X)
            && Y.Equals(other.Y)
            && Width.Equals(other.Width)
            && Height.Equals(other.Height);
    }

    public override bool Equals(object? obj)
    {
        return obj is HoleRect other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Width, Height);
    }

    public static bool operator ==(HoleRect left, HoleRect right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(HoleRect left, HoleRect right)
    {
        return !left.Equals(right);
    }
}

internal readonly struct LockCanvasBlockers : IEquatable<LockCanvasBlockers>
{
    public LockCanvasBlockers(HoleRect top, HoleRect bottom, HoleRect left, HoleRect right)
    {
        Top = top;
        Bottom = bottom;
        Left = left;
        Right = right;
    }

    public HoleRect Top { get; }

    public HoleRect Bottom { get; }

    public HoleRect Left { get; }

    public HoleRect Right { get; }

    public bool Equals(LockCanvasBlockers other)
    {
        return Top.Equals(other.Top)
            && Bottom.Equals(other.Bottom)
            && Left.Equals(other.Left)
            && Right.Equals(other.Right);
    }

    public override bool Equals(object? obj)
    {
        return obj is LockCanvasBlockers other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Top, Bottom, Left, Right);
    }

    public static bool operator ==(LockCanvasBlockers left, LockCanvasBlockers right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LockCanvasBlockers left, LockCanvasBlockers right)
    {
        return !left.Equals(right);
    }
}

internal static class LockCanvasHoleLayout
{
    public static LockCanvasBlockers Calculate(HoleRect canvas, HoleRect hole)
    {
        var left = Clamp(hole.X, canvas.X, canvas.X + canvas.Width);
        var top = Clamp(hole.Y, canvas.Y, canvas.Y + canvas.Height);
        var right = Clamp(hole.X + hole.Width, canvas.X, canvas.X + canvas.Width);
        var bottom = Clamp(hole.Y + hole.Height, canvas.Y, canvas.Y + canvas.Height);

        var clampedWidth = right - left;
        var clampedHeight = bottom - top;

        return new LockCanvasBlockers(
            top: new HoleRect(canvas.X, canvas.Y, canvas.Width, top - canvas.Y),
            bottom: new HoleRect(canvas.X, bottom, canvas.Width, canvas.Y + canvas.Height - bottom),
            left: new HoleRect(canvas.X, top, left - canvas.X, clampedHeight),
            right: new HoleRect(right, top, canvas.X + canvas.Width - right, clampedHeight)
        );
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }
}
