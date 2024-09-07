using System.Runtime.InteropServices;
using System.Text;

using Sharpmd;


var buffer = new float[16];
for (int i = 0; i < buffer.Length; i++) buffer[i] = i;

SpmdRunner.DispatchRange(buffer.Length, (i) => {
    float v = buffer[i];

    if (v < 3.0f)
        v = v * v;
    else
        v = MathF.Sqrt(v);

    buffer[i] = v;
});


for (int i = 0; i < buffer.Length; i++) Console.WriteLine($"{i}: {buffer[i]}");


