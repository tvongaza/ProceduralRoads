// Minimal stand-ins for the UnityEngine types the road logic uses.
// Behavior mirrors Unity's managed math so compiled mod sources run headless.

// ReSharper disable InconsistentNaming
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

    public float sqrMagnitude => x * x + y * y;
    public float magnitude => (float)System.Math.Sqrt(x * x + y * y);

    public void Normalize()
    {
        float m = magnitude;
        if (m > 1e-5f) { x /= m; y /= m; }
        else { x = 0; y = 0; }
    }

    public static float Distance(Vector2 a, Vector2 b)
    {
        float dx = a.x - b.x, dy = a.y - b.y;
        return (float)System.Math.Sqrt(dx * dx + dy * dy);
    }

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    public static Vector2 operator *(Vector2 a, float d) => new(a.x * d, a.y * d);
    public static Vector2 operator *(float d, Vector2 a) => new(a.x * d, a.y * d);

    public override bool Equals(object? other) =>
        other is Vector2 v && v.x == x && v.y == y;

    public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);

    public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
    public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

    public override string ToString() => $"({x:F1}, {y:F1})";
}

public struct Vector3
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

    public static float Distance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
        return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

public struct Vector2Int
{
    public int x;
    public int y;

    public Vector2Int(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
}

public static class Mathf
{
    public const float PI = (float)System.Math.PI;

    public static float Sqrt(float f) => (float)System.Math.Sqrt(f);
    public static float Abs(float f) => System.Math.Abs(f);
    public static float Min(float a, float b) => System.Math.Min(a, b);
    public static float Max(float a, float b) => System.Math.Max(a, b);
    public static float Cos(float f) => (float)System.Math.Cos(f);
    public static float Sin(float f) => (float)System.Math.Sin(f);
    public static float Pow(float f, float p) => (float)System.Math.Pow(f, p);
    public static float Round(float f) => (float)System.Math.Round(f);
    public static int RoundToInt(float f) => (int)System.Math.Round(f);

    public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

    public static float Clamp(float value, float min, float max) =>
        value < min ? min : value > max ? max : value;

    public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

    public static float SmoothStep(float from, float to, float t)
    {
        t = Clamp01(t);
        t = -2f * t * t * t + 3f * t * t;
        return to * t + from * (1f - t);
    }
}
