namespace Sharpmd.Redirects;

using System.Numerics;
using System.Runtime.CompilerServices;

// https://github.com/dotnet/runtime/blob/03f16f1a85fca5fe659338b284d70571990ee935/src/libraries/System.Private.CoreLib/src/System/Numerics/Vector3.cs

[Redirect(typeof(Vector3))]
public static class SN_Vector3 {
    public static float get_Item(in Vector3 self, int index) {
        if ((uint)index >= 3u) throw new IndexOutOfRangeException();

        return index == 0 ? self.X :
               index == 1 ? self.Y :
                            self.Z;
    }
    public static void set_Item(ref Vector3 self, int index, float value) {
        if ((uint)index >= 3u) throw new IndexOutOfRangeException();
        
        if (index == 0) self.X = value;
        else if (index == 1) self.Y = value;
        else self.Z = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool op_Equality(Vector4 left, Vector4 right)
    {
        return (left.X == right.X)
            && (left.Y == right.Y)
            && (left.Z == right.Z)
            && (left.W == right.W);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool op_Inequality(Vector4 left, Vector4 right)
    {
        return !(left == right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 op_UnaryNegation(Vector3 value)
    {
        return new Vector3(-value.X, -value.Y, -value.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Negate(Vector4 value)
    {
        return -value;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Abs(Vector3 value)
    {
        return new Vector3(
            MathF.Abs(value.X),
            MathF.Abs(value.Y),
            MathF.Abs(value.Z)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Clamp(Vector3 value1, Vector3 min, Vector3 max)
    {
        // We must follow HLSL behavior in the case user specified min value is bigger than max value.
        return Min(Max(value1, min), max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Cross(Vector3 vector1, Vector3 vector2)
    {
        return new Vector3(
            (vector1.Y * vector2.Z) - (vector1.Z * vector2.Y),
            (vector1.Z * vector2.X) - (vector1.X * vector2.Z),
            (vector1.X * vector2.Y) - (vector1.Y * vector2.X)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector3 value1, Vector3 value2)
    {
        float distanceSquared = DistanceSquared(value1, value2);
        return MathF.Sqrt(distanceSquared);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DistanceSquared(Vector3 value1, Vector3 value2)
    {
        Vector3 difference = value1 - value2;
        return Dot(difference, difference);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector3 a, Vector3 b)
    {
        return MathF.FusedMultiplyAdd(a.X, b.X, MathF.FusedMultiplyAdd(a.Y, b.Y, a.Z * b.Z));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Length(in Vector3 self)
    {
        float lengthSquared = LengthSquared(self);
        return MathF.Sqrt(lengthSquared);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LengthSquared(in Vector3 self)
    {
        return Dot(self, self);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Lerp(Vector3 value1, Vector3 value2, float amount)
    {
        return (value1 * (1f - amount)) + (value2 * amount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Max(Vector3 value1, Vector3 value2)
    {
        return new Vector3(
            (value1.X > value2.X) ? value1.X : value2.X,
            (value1.Y > value2.Y) ? value1.Y : value2.Y,
            (value1.Z > value2.Z) ? value1.Z : value2.Z
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Min(Vector3 value1, Vector3 value2)
    {
        return new Vector3(
            (value1.X < value2.X) ? value1.X : value2.X,
            (value1.Y < value2.Y) ? value1.Y : value2.Y,
            (value1.Z < value2.Z) ? value1.Z : value2.Z
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Normalize(Vector3 value)
    {
        return value / value.Length();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Reflect(Vector3 vector, Vector3 normal)
    {
        float dot = Dot(vector, normal);
        return vector - (2 * dot * normal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 SquareRoot(Vector3 value)
    {
        return new Vector3(
            MathF.Sqrt(value.X),
            MathF.Sqrt(value.Y),
            MathF.Sqrt(value.Z)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Transform(Vector3 position, Matrix4x4 matrix)
    {
        return new Vector3(
            (position.X * matrix.M11) + (position.Y * matrix.M21) + (position.Z * matrix.M31) + matrix.M41,
            (position.X * matrix.M12) + (position.Y * matrix.M22) + (position.Z * matrix.M32) + matrix.M42,
            (position.X * matrix.M13) + (position.Y * matrix.M23) + (position.Z * matrix.M33) + matrix.M43
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Transform(Vector3 value, Quaternion rotation)
    {
        float x2 = rotation.X + rotation.X;
        float y2 = rotation.Y + rotation.Y;
        float z2 = rotation.Z + rotation.Z;

        float wx2 = rotation.W * x2;
        float wy2 = rotation.W * y2;
        float wz2 = rotation.W * z2;
        float xx2 = rotation.X * x2;
        float xy2 = rotation.X * y2;
        float xz2 = rotation.X * z2;
        float yy2 = rotation.Y * y2;
        float yz2 = rotation.Y * z2;
        float zz2 = rotation.Z * z2;

        return new Vector3(
            value.X * (1.0f - yy2 - zz2) + value.Y * (xy2 - wz2) + value.Z * (xz2 + wy2),
            value.X * (xy2 + wz2) + value.Y * (1.0f - xx2 - zz2) + value.Z * (yz2 - wx2),
            value.X * (xz2 - wy2) + value.Y * (yz2 + wx2) + value.Z * (1.0f - xx2 - yy2)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 TransformNormal(Vector3 normal, Matrix4x4 matrix)
    {
        return new Vector3(
            (normal.X * matrix.M11) + (normal.Y * matrix.M21) + (normal.Z * matrix.M31),
            (normal.X * matrix.M12) + (normal.Y * matrix.M22) + (normal.Z * matrix.M32),
            (normal.X * matrix.M13) + (normal.Y * matrix.M23) + (normal.Z * matrix.M33)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Equals(in Vector3 self, object? obj)
    {
        return (obj is Vector3 other) && Equals(self, other);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Equals(in Vector3 self, Vector3 other)
    {
        // This function needs to account for floating-point equality around NaN
        // and so must behave equivalently to the underlying float/double.Equals
        return self.X.Equals(other.X)
            && self.Y.Equals(other.Y)
            && self.Z.Equals(other.Z);
    }
}