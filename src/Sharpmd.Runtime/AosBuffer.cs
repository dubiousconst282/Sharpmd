namespace Sharpmd;

using System.Numerics;
using System.Runtime.CompilerServices;

public unsafe class AosBuffer<T> where T : unmanaged
{
    public readonly byte* Buffer;
    public readonly int Length;

    public AosBuffer(int length) {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Length = length;
        Buffer = AllocateBuffer(length);
    }

    public T this[int index] {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public Span<E> GetFieldSpan<E>(Func<T, E> cb) where E : unmanaged => throw new PlatformNotSupportedException();
    
    private static byte* AllocateBuffer(int length) {
        throw new PlatformNotSupportedException();
    }
}