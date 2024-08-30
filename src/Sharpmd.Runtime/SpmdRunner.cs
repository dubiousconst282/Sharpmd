namespace Sharpmd;

using System.Runtime.CompilerServices;

public static class SpmdRunner {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DispatchRange(int count, Action<int> kernel) {
        for (int i = 0; i < count; i++) {
            kernel.Invoke(i);
        }
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DispatchRange2D(int width, int height, Action<int, int> kernel) {
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                kernel(x, y);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static V InvokeSIMD<V, S>(Func<int, S> kernel) where V : struct => throw new PlatformNotSupportedException();
}