namespace UnityEngine;

public struct Vector2
{
    public float x;
    public float y;

    public Vector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }
}

public struct Vector3
{
    public float x;
    public float y;
    public float z;

    public static Vector3 zero => new(0f, 0f, 0f);
    public static Vector3 one => new(1f, 1f, 1f);

    public Vector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
}

public struct Quaternion
{
    public float x;
    public float y;
    public float z;
    public float w;

    public static Quaternion identity => new(0f, 0f, 0f, 1f);

    public Quaternion(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }
}

public class Transform { }
