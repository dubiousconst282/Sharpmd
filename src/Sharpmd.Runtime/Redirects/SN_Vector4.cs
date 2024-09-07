namespace Sharpmd.Redirects;

using System.Numerics;
using System.Runtime.CompilerServices;

// https://github.com/dotnet/runtime/blob/03f16f1a85fca5fe659338b284d70571990ee935/src/libraries/System.Private.CoreLib/src/System/Numerics/Vector3.cs

[Redirect(typeof(Vector4))]
public static class SN_Vector4 {

    public static float get_Item(in Vector4 self, int index) {
        if ((uint)index >= 4u) throw new IndexOutOfRangeException();

        return index == 0 ? self.X :
               index == 1 ? self.Y :
               index == 2 ? self.Z : self.W;
    }
    public static void set_Item(ref Vector4 self, int index, float value) {
        if ((uint)index >= 4u) throw new IndexOutOfRangeException();
        
        if (index == 0) self.X = value;
        else if (index == 1) self.Y = value;
        else if (index == 2) self.Z = value;
        else self.W = value;
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
    public static Vector4 op_UnaryNegation(Vector4 value)
    {
        return new Vector4(-value.X, -value.Y, -value.Z, -value.W);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Negate(Vector4 value)
    {
        return -value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Abs(Vector4 value)
    {
        return new Vector4(
            MathF.Abs(value.X),
            MathF.Abs(value.Y),
            MathF.Abs(value.Z),
            MathF.Abs(value.W)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Clamp(Vector4 value1, Vector4 min, Vector4 max)
    {
        // We must follow HLSL behavior in the case user specified min value is bigger than max value.
        return Min(Max(value1, min), max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector4 value1, Vector4 value2)
    {
        float distanceSquared = DistanceSquared(value1, value2);
        return MathF.Sqrt(distanceSquared);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DistanceSquared(Vector4 value1, Vector4 value2)
    {
        Vector4 difference = value1 - value2;
        return Dot(difference, difference);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector4 a, Vector4 b)
    {
        return MathF.FusedMultiplyAdd(a.X, b.X, MathF.FusedMultiplyAdd(a.Y, b.Y, MathF.FusedMultiplyAdd(a.Z, b.Z, a.W * b.W)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Length(in Vector4 self)
    {
        float lengthSquared = LengthSquared(self);
        return MathF.Sqrt(lengthSquared);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LengthSquared(in Vector4 self)
    {
        return Dot(self, self);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Lerp(Vector4 value1, Vector4 value2, float amount)
    {
        return (value1 * (1.0f - amount)) + (value2 * amount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Max(Vector4 value1, Vector4 value2)
    {
        return new Vector4(
            (value1.X > value2.X) ? value1.X : value2.X,
            (value1.Y > value2.Y) ? value1.Y : value2.Y,
            (value1.Z > value2.Z) ? value1.Z : value2.Z,
            (value1.W > value2.W) ? value1.W : value2.W
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Min(Vector4 value1, Vector4 value2)
    {
        return new Vector4(
            (value1.X < value2.X) ? value1.X : value2.X,
            (value1.Y < value2.Y) ? value1.Y : value2.Y,
            (value1.Z < value2.Z) ? value1.Z : value2.Z,
            (value1.W < value2.W) ? value1.W : value2.W
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Normalize(Vector4 vector)
    {
        return vector / vector.Length();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 SquareRoot(Vector4 value)
    {
        return new Vector4(
            MathF.Sqrt(value.X),
            MathF.Sqrt(value.Y),
            MathF.Sqrt(value.Z),
            MathF.Sqrt(value.W)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Transform(Vector2 position, Matrix4x4 matrix)
    {
        return new Vector4(
            (position.X * matrix.M11) + (position.Y * matrix.M21) + matrix.M41,
            (position.X * matrix.M12) + (position.Y * matrix.M22) + matrix.M42,
            (position.X * matrix.M13) + (position.Y * matrix.M23) + matrix.M43,
            (position.X * matrix.M14) + (position.Y * matrix.M24) + matrix.M44
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Transform(Vector2 value, Quaternion rotation)
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

        return new Vector4(
            value.X * (1.0f - yy2 - zz2) + value.Y * (xy2 - wz2),
            value.X * (xy2 + wz2) + value.Y * (1.0f - xx2 - zz2),
            value.X * (xz2 - wy2) + value.Y * (yz2 + wx2),
            1.0f
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Transform(Vector3 position, Matrix4x4 matrix)
    {
        return new Vector4(
            (position.X * matrix.M11) + (position.Y * matrix.M21) + (position.Z * matrix.M31) + matrix.M41,
            (position.X * matrix.M12) + (position.Y * matrix.M22) + (position.Z * matrix.M32) + matrix.M42,
            (position.X * matrix.M13) + (position.Y * matrix.M23) + (position.Z * matrix.M33) + matrix.M43,
            (position.X * matrix.M14) + (position.Y * matrix.M24) + (position.Z * matrix.M34) + matrix.M44
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Transform(Vector3 value, Quaternion rotation)
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

        return new Vector4(
            value.X * (1.0f - yy2 - zz2) + value.Y * (xy2 - wz2) + value.Z * (xz2 + wy2),
            value.X * (xy2 + wz2) + value.Y * (1.0f - xx2 - zz2) + value.Z * (yz2 - wx2),
            value.X * (xz2 - wy2) + value.Y * (yz2 + wx2) + value.Z * (1.0f - xx2 - yy2),
            1.0f
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Transform(Vector4 vector, Matrix4x4 matrix)
    {
        return new Vector4(
            (vector.X * matrix.M11) + (vector.Y * matrix.M21) + (vector.Z * matrix.M31) + (vector.W * matrix.M41),
            (vector.X * matrix.M12) + (vector.Y * matrix.M22) + (vector.Z * matrix.M32) + (vector.W * matrix.M42),
            (vector.X * matrix.M13) + (vector.Y * matrix.M23) + (vector.Z * matrix.M33) + (vector.W * matrix.M43),
            (vector.X * matrix.M14) + (vector.Y * matrix.M24) + (vector.Z * matrix.M34) + (vector.W * matrix.M44)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Transform(Vector4 value, Quaternion rotation)
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

        return new Vector4(
            value.X * (1.0f - yy2 - zz2) + value.Y * (xy2 - wz2) + value.Z * (xz2 + wy2),
            value.X * (xy2 + wz2) + value.Y * (1.0f - xx2 - zz2) + value.Z * (yz2 - wx2),
            value.X * (xz2 - wy2) + value.Y * (yz2 + wx2) + value.Z * (1.0f - xx2 - yy2),
            value.W);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Equals(in Vector4 self, Vector4 other)
    {
        // This function needs to account for floating-point equality around NaN
        // and so must behave equivalently to the underlying float/double.Equals

        return self.X.Equals(other.X)
            && self.Y.Equals(other.Y)
            && self.Z.Equals(other.Z)
            && self.W.Equals(other.W);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Equals(in Vector4 self, object? obj)
    {
        return (obj is Vector4 other) && Equals(self, other);
    }
}