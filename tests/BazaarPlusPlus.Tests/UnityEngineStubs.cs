using System;

namespace UnityEngine;

public struct Vector2 : IEquatable<Vector2>
{
    public float x;
    public float y;

    public Vector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    public static Vector2 zero => new(0f, 0f);

    public bool Equals(Vector2 other) => x.Equals(other.x) && y.Equals(other.y);

    public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(x, y);

    public static bool operator ==(Vector2 left, Vector2 right) => left.Equals(right);

    public static bool operator !=(Vector2 left, Vector2 right) => !left.Equals(right);
}

public struct Vector3 : IEquatable<Vector3>
{
    public float x;
    public float y;
    public float z;

    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public static Vector3 zero => new(0f, 0f, 0f);

    public static Vector3 one => new(1f, 1f, 1f);

    public static Vector3 operator +(Vector3 left, Vector3 right)
        => new(left.x + right.x, left.y + right.y, left.z + right.z);

    public static Vector3 operator *(Vector3 vector, float scalar)
        => new(vector.x * scalar, vector.y * scalar, vector.z * scalar);

    public bool Equals(Vector3 other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z);

    public override bool Equals(object? obj) => obj is Vector3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(x, y, z);

    public static bool operator ==(Vector3 left, Vector3 right) => left.Equals(right);

    public static bool operator !=(Vector3 left, Vector3 right) => !left.Equals(right);
}

public struct Quaternion : IEquatable<Quaternion>
{
    public float x;
    public float y;
    public float z;
    public float w;

    public Quaternion(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    public static Quaternion identity => new(0f, 0f, 0f, 1f);

    public static Quaternion Euler(float x, float y, float z) => new(x, y, z, 1f);

    public static Quaternion Euler(Vector3 euler) => new(euler.x, euler.y, euler.z, 1f);

    public static Quaternion operator *(Quaternion left, Quaternion right)
        => new(left.x + right.x, left.y + right.y, left.z + right.z, 1f);

    public static Vector3 operator *(Quaternion rotation, Vector3 point) => point;

    public bool Equals(Quaternion other)
    {
        return x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);
    }

    public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(x, y, z, w);

    public static bool operator ==(Quaternion left, Quaternion right) => left.Equals(right);

    public static bool operator !=(Quaternion left, Quaternion right) => !left.Equals(right);
}

public sealed class Transform
{
    public Vector3 position;
    public Quaternion rotation;
}
