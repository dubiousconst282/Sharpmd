# Sharpmd
Vectorizing SPMD compiler for C#/CIL

WIP

## Tentative ideas / TODOs
- Full scalarization support: everything works, apart from exception handling and async/yield stuff
- Implement "Whole Function Vectorization", "Partial Linearization", etc in DistIL
  - [p] Predication, linearization, vector widening
  - [p] Lowering of vector IR
  - [ ] Analyses for uniform/sequential/small stride opts
  - [ ] Automatic SoA transform of non-escaping structs within call-graph
  - [ ] SoA buffer intrinsic (`SoaBuffer<T>`) for manual opts
- Misc opts to amend auto-SoA
  - [p] "Extrinsification" pass to undo/redirect CoreLib intrinsics
  - [ ] Aggressive struct SROA
- [ ] Roslyn analyser for perf hints and codegen inspection

### Complications and concerns
- How to deal with managed refs?
  - Gathers only take ptrs, so mem accesses will have to be scalarized
  - Pin all global parameters before dispatch (maybe even once per gather? pinning seems cheap)
- How would debugging work? Line mappings are easy but vars are not
  - Runtime lib should have full support to exec scalar code as is, vectorization left purely as an optimization step
  - Alternatively, could generate "shadow variables" with first lane values
- Is the output any fast and actually useful?

### Examples

```cs
// Dispatch-like interface
var buffer = new uint[width * height];

SpmdRunner.DispatchRange2D(width, height, (x, y) => {
    buffer[x + y * width] = (x/8 + y/8) % 2 != 0 ? 0xFFFFFF : 0xA0A0A0;
});

// Output:
public void spmd__DispatchRange2D(int w, int h) {
    for (int y = 0; y < h; y++) {
        var x = vec_LaneIdx;
        for (int i = 0; i < w; i += vec_Width) {
            var mask = x < w;
            var value = v_select((x/8 + y/8) % 2 != 0, 0xFFFFFF, 0xA0A0A0);
            v_scatter(buffer, x + y * width, value, mask);
            x += vec_width;
        }
    }
}


// Direct vector call (zero overhead intrinsic)
var res = SpmdRunner.InvokeSIMD<Vector128<float>>(laneIdx => Math.Sqrt(buffer[laneIdx] * 3.0f));

var res = Vector128.Sqrt(Vector128.LoadChecked(buffer) * 3.0f);


// Extra opt ideas


// Atomics:
int r = Interlocked.Add(ref dest, x);
int r = Interlocked.Add(ref dest, 1);

vint r = Interlocked.Add(ref dest, WaveSum(x)) + WavePrefixSum(x);
vint r = Interlocked.Add(ref dest, GetLaneCount()) + GetLaneIndex();

// Append scalarization:
list.Add(x);
array[i++] = x;

memcpy(dest.ptr + dest.count, mask_compress(x, mask));
dest.count += popcnt(mask);

// Alloc scalarization:
Span<int> buf = new int[len];

int[] buf_ = new int[WaveSum(len)];
fixed (buf_);
OffsetUniformPtr<int> lane_buf = lea(buf_, WavePrefixSum(len));
```


