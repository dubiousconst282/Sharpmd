namespace Sharpmd;

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

/// <summary> SIMD polyfills. </summary>
public static unsafe class SimdOps {
    public static Vector128<int> Gather32x4(ref int baseAddr, Vector128<int> idx) {
        var res = Vector128.CreateScalarUnsafe(Unsafe.Add(ref baseAddr, idx.GetElement(0)));
        res = Vector128.WithElement(res, 1, Unsafe.Add(ref baseAddr, idx.GetElement(1)));
        res = Vector128.WithElement(res, 2, Unsafe.Add(ref baseAddr, idx.GetElement(2)));
        res = Vector128.WithElement(res, 3, Unsafe.Add(ref baseAddr, idx.GetElement(3)));
        return res;
    }
    public static Vector256<int> Gather32x8(ref int baseAddr, Vector256<int> idx) {
        // TODO: Pinning only costs one stack spill, profile whether it is worth using it combined with gather instrs

        // Splitting vectors generates more optimal asm.
        return Vector256.Create(Gather32x4(ref baseAddr, idx.GetLower()),
                                Gather32x4(ref baseAddr, idx.GetUpper()));
    }
    public static Vector512<int> Gather32x16(ref int baseAddr, Vector512<int> idx) {
        return Vector512.Create(Gather32x8(ref baseAddr, idx.GetLower()),
                                Gather32x8(ref baseAddr, idx.GetUpper()));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Scatter32x4(ref int baseAddr, Vector128<int> idx, Vector128<int> value, uint mask) {
        if ((mask & 1) != 0) Unsafe.Add(ref baseAddr, idx.GetElement(0)) = value.GetElement(0);
        if ((mask & 2) != 0) Unsafe.Add(ref baseAddr, idx.GetElement(1)) = value.GetElement(1);
        if ((mask & 4) != 0) Unsafe.Add(ref baseAddr, idx.GetElement(2)) = value.GetElement(2);
        if ((mask & 8) != 0) Unsafe.Add(ref baseAddr, idx.GetElement(3)) = value.GetElement(3);
    }
    public static void Scatter32x8(ref int baseAddr, Vector256<int> idx, Vector256<int> value, uint mask) {
        Scatter32x4(ref baseAddr, idx.GetLower(), value.GetLower(), mask);
        Scatter32x4(ref baseAddr, idx.GetUpper(), value.GetUpper(), mask >> 4);
    }

    public static void Scatter32x4(ref int baseAddr, Vector128<int> idx, Vector128<int> value, Vector128<int> mask) {
        Scatter32x4(ref baseAddr, idx, value, mask.ExtractMostSignificantBits());
    }
    public static void Scatter32x8(ref int baseAddr, Vector256<int> idx, Vector256<int> value, Vector256<int> mask) {
        Scatter32x8(ref baseAddr, idx, value, mask.ExtractMostSignificantBits());
    }
    public static void Scatter32x16(ref int baseAddr, Vector512<int> idx, Vector512<int> value, Vector512<int> mask) {
        uint s_mask = (uint)mask.ExtractMostSignificantBits();
        Scatter32x8(ref baseAddr, idx.GetLower(), value.GetLower(), s_mask);
        Scatter32x8(ref baseAddr, idx.GetUpper(), value.GetUpper(), s_mask >> 8);
    }

    public static Vector128<int> ShiftLeft(Vector128<int> a, Vector128<int> b) {
        if (Avx2.IsSupported) {
            return Avx2.ShiftLeftLogicalVariable(a, b.AsUInt32());
        }
        if (AdvSimd.IsSupported) {
            return AdvSimd.ShiftArithmetic(a, b);
        }
        throw new NotImplementedException();
    }
    public static Vector128<int> ShiftRightArithmetic(Vector128<int> a, Vector128<int> b) {
        if (Avx2.IsSupported) {
            return Avx2.ShiftRightArithmeticVariable(a, b.AsUInt32());
        }
        if (AdvSimd.IsSupported) {
            return AdvSimd.ShiftArithmetic(a, -b);
        }
        throw new NotImplementedException();
    }
    public static Vector128<int> ShiftRightLogical(Vector128<int> a, Vector128<int> b) {
        if (Avx2.IsSupported) {
            return Avx2.ShiftRightLogicalVariable(a, b.AsUInt32());
        }
        if (AdvSimd.IsSupported) {
            return AdvSimd.ShiftLogical(a, -b);
        }
        throw new NotImplementedException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ConditionalThrow_IndexOutOfRange(bool cond) {
        if (cond) {
            ThrowHelper();
        }
        static void ThrowHelper() => throw new IndexOutOfRangeException();
    }
}