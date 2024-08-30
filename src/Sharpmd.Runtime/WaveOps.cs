namespace Sharpmd;

using System.Runtime.CompilerServices;

public static class WaveOps {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsFirstLane() => true;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int GetLaneCount() => 1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ulong GetActiveLaneMask() => 1;
}
